using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FluentAssertions;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Round 141 remediation: the r141 fix wave corrected WorkbookSession.GoToReference/
/// TryResolveReferenceRange so sheet-scoped defined names resolve (and correctly beat a
/// workbook-global name of the same name on that sheet) for Avalonia hyperlink navigation, but
/// the WPF F5 Go To dialog (MainWindow.HomeEditing.cs's FindGoToMenuItem_Click) never routed
/// through that fix at all -- it built a <c>GoToDialog</c> straight against the workbook-global
/// <c>_workbook.NamedRanges</c> dictionary, so a WPF user pressing F5 and typing a sheet-scoped
/// name still got "Reference is not valid".
///
/// These tests drive the real F5 entry point (FindGoToMenuItem_Click, via reflection, exactly as
/// the ribbon/keyboard shortcut invokes it) through a genuinely modal GoToDialog -- not the
/// GoToDialogPlanner/WorkbookReferenceNavigator helpers directly and not a hand-wired resolver --
/// so the coverage proves the production wiring in MainWindow.HomeEditing.cs supplies the
/// sheet-scope-aware resolver, not merely that the resolver mechanism works in isolation.
/// </summary>
public sealed class R141_GoToDialogSheetScopedNameTests
{
    [Fact]
    public void F5GoTo_WithSheetScopedNameOnActiveSheet_NavigatesToScopedRange()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var sheet1 = harness.Workbook.Sheets[0];
            var scopedRange = new GridRange(new CellAddress(sheet1.Id, 6, 2), new CellAddress(sheet1.Id, 7, 3));
            // Defined with sheet scope only -- no matching entry in the workbook-global NamedRanges
            // dictionary, exactly like a name created via Name Manager with scope = current sheet.
            harness.Workbook.DefineNamedRange("ScopedOnly", scopedRange, metadata: null, scopeSheetId: sheet1.Id);

            harness.InvokeFindGoToMenuItemWithReference("ScopedOnly");

            harness.SelectedRange.Should().Be(scopedRange,
                "the WPF F5 Go To dialog must resolve a sheet-scoped defined name, matching WorkbookSession.GoToReference and the Name Box");
        });
    }

    [Fact]
    public void F5GoTo_WithScopedNameShadowingGlobalName_PrefersScopedRange()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var sheet1 = harness.Workbook.Sheets[0];
            var globalRange = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
            var scopedRange = new GridRange(new CellAddress(sheet1.Id, 9, 9), new CellAddress(sheet1.Id, 9, 9));
            harness.Workbook.DefineNamedRange("Shadowed", globalRange);
            harness.Workbook.DefineNamedRange("Shadowed", scopedRange, metadata: null, scopeSheetId: sheet1.Id);

            harness.InvokeFindGoToMenuItemWithReference("Shadowed");

            harness.SelectedRange.Should().Be(scopedRange,
                "a sheet-scoped name must beat a same-named workbook-global name in the F5 Go To dialog, matching formula evaluation");
        });
    }

    [Fact]
    public void F5GoTo_WithNameScopedToOtherSheet_IsNotResolvedFromActiveSheet()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var sheet1 = harness.Workbook.Sheets[0];
            var sheet2 = harness.Workbook.AddSheet("Sheet2");
            var scopedRange = new GridRange(new CellAddress(sheet2.Id, 6, 2), new CellAddress(sheet2.Id, 6, 2));
            harness.Workbook.DefineNamedRange("Sheet2Only", scopedRange, metadata: null, scopeSheetId: sheet2.Id);
            harness.SelectActiveCell(1, 1);

            harness.InvokeFindGoToMenuItemWithReference("Sheet2Only", expectAccept: false);

            // Not resolvable from Sheet1 (wrong scope): the dialog's Accept() shows the "Reference
            // is not valid" warning and stays open, so the selection is left exactly as it was and
            // the harness closes the still-open dialog itself in TearDown.
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet1.Id, 1, 1),
                new CellAddress(sheet1.Id, 1, 1)));
        });
    }

    [Fact]
    public void F5GoToReferenceList_IncludesSheetScopedNameForActiveSheetButNotOtherSheets()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var sheet1 = harness.Workbook.Sheets[0];
            var sheet2 = harness.Workbook.AddSheet("Sheet2");
            harness.Workbook.DefineNamedRange(
                "ActiveSheetName",
                new GridRange(new CellAddress(sheet1.Id, 4, 4), new CellAddress(sheet1.Id, 4, 4)),
                metadata: null,
                scopeSheetId: sheet1.Id);
            harness.Workbook.DefineNamedRange(
                "OtherSheetName",
                new GridRange(new CellAddress(sheet2.Id, 4, 4), new CellAddress(sheet2.Id, 4, 4)),
                metadata: null,
                scopeSheetId: sheet2.Id);

            var choices = harness.CaptureGoToReferenceChoices();

            choices.Should().Contain("ActiveSheetName");
            choices.Should().NotContain("OtherSheetName");
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _findGoToMenuItemClick;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
            _findGoToMenuItemClick = typeof(MainWindow)
                .GetMethod("FindGoToMenuItem_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "FindGoToMenuItem_Click");
        }

        public Workbook Workbook => _window.Session.Workbook;

        public GridRange? SelectedRange => ((SheetGridView)_window.FindName("SheetGrid")).SelectedRange;

        public void SelectActiveCell(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            _window.SetActiveCellForTest(new CellAddress(sheet.Id, row, col));
            PumpDispatcher();
        }

        /// <summary>
        /// Drives the real F5 entry point end to end: invokes FindGoToMenuItem_Click (exactly as the
        /// keyboard shortcut / ribbon command does) which synchronously opens a genuinely modal
        /// GoToDialog via ShowDialog(). While that call blocks and pumps the dispatcher, a queued
        /// callback locates the dialog through the owner window's OwnedWindows (no test-only seam),
        /// types <paramref name="referenceText"/> into its reference box, and invokes the dialog's
        /// own private Accept() -- the same code path the OK button/Enter key drives -- so the
        /// resolver under test is whatever MainWindow.HomeEditing.cs actually wired into the dialog,
        /// not a resolver the test supplies itself.
        /// </summary>
        public void InvokeFindGoToMenuItemWithReference(string referenceText, bool expectAccept = true)
        {
            // GoToDialog.Accept() shows a blocking real MessageBox via DialogMessageHelper.ShowWarning
            // when the reference does not resolve; install the shared headless seam so that never
            // stalls the STA thread here (mirrors how other WPF host tests avoid the same deadlock,
            // e.g. the Save-on-exit confirmation seam), then restore it so this test cannot leak the
            // override into any other test.
            var previousHandler = HeadlessMessageBox.Handler;
            HeadlessMessageBox.Handler = (_, _) => UserMessageResult.Ok;
            try
            {
                _window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var dialog = _window.OwnedWindows
                        .OfType<Window>()
                        .FirstOrDefault(w => w.GetType().Name == "GoToDialog");
                    if (dialog is null)
                        return;

                    var addressBoxField = dialog.GetType()
                        .GetField("_addressBox", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new MissingFieldException("GoToDialog", "_addressBox");
                    ((TextBox)addressBoxField.GetValue(dialog)!).Text = referenceText;

                    var accept = dialog.GetType()
                        .GetMethod("Accept", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new MissingMethodException("GoToDialog", "Accept");
                    accept.Invoke(dialog, null);

                    if (dialog.IsVisible)
                    {
                        // Accept() rejected the reference (DialogResult was never set, matching
                        // production's "Reference is not valid" path with the warning suppressed
                        // above) -- close the still-open dialog the same way a user Cancel would, so
                        // ShowDialog() below returns and the rest of the handler runs. When
                        // expectAccept is true this is a genuine test failure (the caller asked for a
                        // successful navigation), not a hang -- closing without a DialogResult leaves
                        // SelectedRange null/stale, which the assertion below will catch.
                        dialog.Close();
                    }
                }), DispatcherPriority.ApplicationIdle);

                _findGoToMenuItemClick.Invoke(_window, [_window, new RoutedEventArgs()]);
                PumpDispatcher();
            }
            finally
            {
                HeadlessMessageBox.Handler = previousHandler;
            }
        }

        /// <summary>
        /// Captures the defined-name choices FindGoToMenuItem_Click offers in the dialog's history
        /// list without accepting a reference, mirroring the read-only assertions the Name Box
        /// dropdown tests make on NameBoxDropdownPlanner.Build's output.
        /// </summary>
        public IReadOnlyList<string> CaptureGoToReferenceChoices()
        {
            IReadOnlyList<string>? captured = null;
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = _window.OwnedWindows
                    .OfType<Window>()
                    .FirstOrDefault(w => w.GetType().Name == "GoToDialog");
                if (dialog is null)
                    return;

                var historyListField = dialog.GetType()
                    .GetField("_historyList", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException("GoToDialog", "_historyList");
                var historyList = (ListBox)historyListField.GetValue(dialog)!;
                captured = historyList.Items.Cast<string>().ToArray();

                dialog.Close();
            }), DispatcherPriority.ApplicationIdle);

            _findGoToMenuItemClick.Invoke(_window, [_window, new RoutedEventArgs()]);
            PumpDispatcher();

            return captured ?? throw new InvalidOperationException("GoToDialog was never located to capture its reference choices.");
        }

        public static MainWindowHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                commandBus,
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.Activate();
            window.UpdateLayout();
            PumpDispatcher();
            return new MainWindowHarness(window);
        }

        public void Dispose()
        {
            // Any still-open GoToDialog (e.g. the invalid-reference test's rejected Accept) is owned
            // by _window, so closing it first avoids leaving a modal window behind for the next test.
            foreach (var owned in _window.OwnedWindows.OfType<Window>().ToArray())
                owned.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
