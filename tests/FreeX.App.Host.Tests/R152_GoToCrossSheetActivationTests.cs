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
/// Round 152 remediation: FindGoToMenuItem_Click's success branch (MainWindow.HomeEditing.cs)
/// pointed SheetGrid.SelectedRange at the target range but never assigned _currentSheetId, never
/// called UpdateViewport(), and never called RefreshSheetTabs() -- so a cross-sheet F5/Ctrl+G Go To
/// (e.g. "Sheet2!B5" typed while Sheet1 is active) left the grid rendering Sheet1 (SheetGrid.ActiveSheetId
/// is only ever assigned inside UpdateViewport, per MainWindow.Viewport.cs), and the Formula Bar
/// read Sheet1's cell at that row/col (Sheet.GetCell(CellAddress) ignores address.Sheet) instead of
/// Sheet2's. This mirrors the Name Box's own NavigateNameBoxTo (MainWindow.Editing.cs), which already
/// does all three steps.
/// </summary>
public sealed class R152_GoToCrossSheetActivationTests
{
    [Fact]
    public void F5GoTo_WithCrossSheetReference_SwitchesActiveSheetAndFormulaBar()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var sheet1 = harness.Workbook.Sheets[0];
            var sheet2 = harness.Workbook.AddSheet("Sheet2");
            sheet2.SetCell(new CellAddress(sheet2.Id, 5, 2), new TextValue("Target"));
            harness.SelectActiveCell(sheet1.Id, 1, 1);

            harness.InvokeFindGoToMenuItemWithReference("Sheet2!B5");

            harness.CurrentSheetId.Should().Be(sheet2.Id,
                "a cross-sheet Go To must activate the target sheet, matching the Name Box's NavigateNameBoxTo");
            harness.SheetGridActiveSheetId.Should().Be(sheet2.Id,
                "SheetGrid.ActiveSheetId is only ever assigned inside UpdateViewport, so it lagging behind means the grid is still rendering the old sheet");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet2.Id, 5, 2),
                new CellAddress(sheet2.Id, 5, 2)));
            harness.FormulaBarText.Should().Be("Target",
                "the Formula Bar must show the destination cell's own content, not the old sheet's cell at the same row/col");
        });
    }

    [Fact]
    public void F5GoTo_WithSameSheetReference_LeavesActiveSheetUnchanged()
    {
        // Sibling no-regression case: a same-sheet Go To must not spuriously flip _currentSheetId or
        // otherwise disturb the already-correct single-sheet navigation path.
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var sheet1 = harness.Workbook.Sheets[0];
            sheet1.SetCell(new CellAddress(sheet1.Id, 5, 2), new TextValue("Local"));
            harness.SelectActiveCell(sheet1.Id, 1, 1);

            harness.InvokeFindGoToMenuItemWithReference("B5");

            harness.CurrentSheetId.Should().Be(sheet1.Id);
            harness.SheetGridActiveSheetId.Should().Be(sheet1.Id);
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet1.Id, 5, 2),
                new CellAddress(sheet1.Id, 5, 2)));
            harness.FormulaBarText.Should().Be("Local");
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

        public SheetId CurrentSheetId => _window.CurrentSheetIdForTest;

        public SheetId SheetGridActiveSheetId => ((SheetGridView)_window.FindName("SheetGrid")).ActiveSheetId;

        public GridRange? SelectedRange => ((SheetGridView)_window.FindName("SheetGrid")).SelectedRange;

        public string FormulaBarText => ((TextBox)_window.FindName("FormulaBar")).Text;

        public void SelectActiveCell(SheetId sheetId, uint row, uint col)
        {
            _window.SetCurrentSheetForFormulaPointForTest(sheetId);
            _window.SetActiveCellForTest(new CellAddress(sheetId, row, col));
            PumpDispatcher();
        }

        /// <summary>
        /// Drives the real F5 entry point end to end: invokes FindGoToMenuItem_Click (exactly as the
        /// keyboard shortcut / ribbon command does) which synchronously opens a genuinely modal
        /// GoToDialog via ShowDialog(). While that call blocks and pumps the dispatcher, a queued
        /// callback locates the dialog through the owner window's OwnedWindows (no test-only seam),
        /// types <paramref name="referenceText"/> into its reference box, and invokes the dialog's
        /// own private Accept() -- the same code path the OK button/Enter key drives.
        /// </summary>
        public void InvokeFindGoToMenuItemWithReference(string referenceText)
        {
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
                        dialog.Close();
                }), DispatcherPriority.ApplicationIdle);

                _findGoToMenuItemClick.Invoke(_window, [_window, new RoutedEventArgs()]);
                PumpDispatcher();
            }
            finally
            {
                HeadlessMessageBox.Handler = previousHandler;
            }
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
