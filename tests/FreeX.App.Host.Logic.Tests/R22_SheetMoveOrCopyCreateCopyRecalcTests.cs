using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Free.Shared.AppServices;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for finding R22-calc-engine-dependency-2: the WPF "Move or Copy Sheet"
/// dialog's "Create a copy" branch (<c>SheetCtxMoveOrCopy_Click</c> in
/// <c>src/FreeX.App.Host/MainWindow.SheetTabs.cs</c>) must recalculate the workbook after
/// duplicating and repositioning the sheet, exactly like its plain-Move sibling branch already
/// does -- otherwise a copy that lands inside a 3-D span reference (e.g.
/// <c>=SUM(Sheet1:Sheet3!A1)</c>) leaves the dependent formula showing a stale value.
/// </summary>
public sealed class R22_SheetMoveOrCopyCreateCopyRecalcTests
{
    [Fact]
    public void MoveOrCopy_CreateCopy_LandingCopyInsideThreeDSpan_RecalculatesSpanFormula()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;

            // Order: Sheet1, Sheet2, Sheet3, SheetX. SheetX starts *outside* the Sheet1:Sheet3
            // span. Sheet1!B1 = SUM(Sheet1:Sheet3!A1) = 30 (10 + 10 + 10), excluding SheetX.
            var sheet1 = workbook.GetSheetAt(0);
            var sheet2 = workbook.AddSheet("Sheet2");
            var sheet3 = workbook.AddSheet("Sheet3");
            var sheetX = workbook.AddSheet("SheetX");

            sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(10));
            sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(10));
            sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(10));
            sheetX.SetCell(new CellAddress(sheetX.Id, 1, 1), new NumberValue(100));
            sheet1.SetFormula(new CellAddress(sheet1.Id, 1, 2), "SUM(Sheet1:Sheet3!A1)");

            harness.RecalculateWorkbook();
            sheet1.GetValue(1, 2).Should().Be(
                new NumberValue(30),
                "SheetX sits outside the Sheet1:Sheet3 span before the copy");

            // Right-click SheetX -> Move or Copy... -> "Create a copy" -> Before: Sheet2. This
            // resolves (pre-duplication) to InsertBeforeIndex 1, landing the duplicate between
            // Sheet1 and Sheet2 -- inside the Sheet1:Sheet3 span.
            QueueOwnedWindowInteraction(harness.Window, dialog =>
            {
                var createCopyBox = GetField<CheckBox>(dialog, "_createCopyBox");
                var beforeSheetBox = GetField<ListBox>(dialog, "_beforeSheetBox");
                createCopyBox.IsChecked = true;
                beforeSheetBox.SelectedIndex = 1; // "Sheet2" (original order: Sheet1,Sheet2,Sheet3,SheetX)
                var okButton = GetField<Button>(dialog, "_okButton");
                okButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            });

            InvokeSheetTabContextMenuClick(harness.Window, "SheetCtxMoveOrCopy_Click", sheetX.Id);
            PumpDispatcher();

            // The click handler itself (no test-driven recalc call) must have already
            // recalculated Sheet1!B1 to include the duplicate's A1 = 100.
            sheet1.GetValue(1, 2).Should().Be(
                new NumberValue(130),
                "the SheetX copy now sits inside the Sheet1:Sheet3 span and the Create-a-copy " +
                "branch must recalculate just like the plain-Move branch does");
        });
    }

    private static void QueueOwnedWindowInteraction(MainWindow window, Action<Window> interact)
    {
        void PollForDialog()
        {
            var owned = window.OwnedWindows.Cast<Window>().FirstOrDefault();
            if (owned is null)
            {
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)PollForDialog);
                return;
            }

            interact(owned);
        }

        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)PollForDialog);
    }

    private static void InvokeSheetTabContextMenuClick(MainWindow window, string methodName, SheetId clickedSheetId)
    {
        var tab = new SheetTabViewModel(clickedSheetId, "irrelevant", null);
        var placementTarget = new Border { DataContext = tab };
        var menuItem = new MenuItem();
        var contextMenu = new ContextMenu { PlacementTarget = placementTarget };
        contextMenu.Items.Add(menuItem);

        var method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(object), typeof(RoutedEventArgs)]);
        method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
        method!.Invoke(window, [menuItem, new RoutedEventArgs()]);
    }

    private static T GetField<T>(object instance, string fieldName) where T : class
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(instance.GetType().Name, fieldName);
        return (T)field.GetValue(instance)!;
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed class MainWindowHarness : IDisposable
    {
        public MainWindow Window { get; }
        public Workbook Workbook { get; }

        public MainWindowHarness()
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            Window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                new RecordingUserMessageService());

            Window.Show();
            PumpDispatcher();

            // MainWindow_Loaded (fired by Show() above) replaces the constructor-supplied
            // workbook with a fresh one via CreateNewWorkbook() -- capture the *live* workbook
            // afterward so the test operates on the same Workbook instance MainWindow's
            // sheet-tab handlers actually use.
            Workbook = workbookRef.Current;
        }

        public void RecalculateWorkbook()
        {
            var method = typeof(MainWindow).GetMethod("RecalculateWorkbook", BindingFlags.Instance | BindingFlags.NonPublic, [])
                ?? throw new MissingMethodException(nameof(MainWindow), "RecalculateWorkbook");
            method.Invoke(Window, []);
        }

        public void Dispose()
        {
            foreach (Window ownedWindow in Window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            Window.SuppressNextClosePrompt();
            Window.Close();
            PumpDispatcher();
        }
    }

    /// <summary>
    /// No-op <see cref="IUserMessageService"/> for tests that construct <see cref="MainWindow"/>
    /// directly and don't want real WPF MessageBox windows popping up.
    /// </summary>
    private sealed class RecordingUserMessageService : IUserMessageService
    {
        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => false;
        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.Ok;
    }
}
