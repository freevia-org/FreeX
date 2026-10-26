using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for finding R16-cross-sheet-3d-recalc-1: moving/reordering a sheet in the
/// WPF host must trigger a workbook recalculation so 3-D span aggregates whose span membership
/// changed by the move (e.g. =SUM(Sheet1:Sheet3!A1)) pick up the new value instead of keeping a
/// stale cached one.
/// </summary>
public sealed class R16_wpf_sheet_move_Tests
{
    [Fact]
    public void SheetTabDragDropCommit_MovingSheetOutOfSpan_RecalculatesThreeDSpanFormula()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;

            // Order: Sheet1, Sheet2, Sheet4, Sheet3 -- Sheet4 sits *inside* the Sheet1:Sheet3 span
            // (spans cover every sheet positioned between the named endpoints, inclusive).
            var sheet1 = workbook.GetSheetAt(0);
            var sheet2 = workbook.AddSheet("Sheet2");
            var sheet4 = workbook.AddSheet("Sheet4");
            var sheet3 = workbook.AddSheet("Sheet3");

            sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
            sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
            sheet4.SetCell(new CellAddress(sheet4.Id, 1, 1), new NumberValue(100));
            sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));
            sheet1.SetFormula(new CellAddress(sheet1.Id, 1, 2), "SUM(Sheet1:Sheet3!A1)");

            harness.RecalculateWorkbook();
            sheet1.GetValue(1, 2).Should().Be(new NumberValue(106), "Sheet4 currently sits inside the Sheet1:Sheet3 span");

            // Drag Sheet4 to the end of the tab strip (after Sheet3), moving it out of the span --
            // exactly the gesture handled by CommitPendingSheetTabDragDrop.
            harness.SetDragSheetTabState(sheet4.Id, toIndex: 3);
            harness.InvokeVoid("CommitPendingSheetTabDragDrop");

            workbook.Sheets.Select(s => s.Id).Should().Equal(sheet1.Id, sheet2.Id, sheet3.Id, sheet4.Id);
            sheet1.GetValue(1, 2).Should().Be(
                new NumberValue(6),
                "moving Sheet4 out of the Sheet1:Sheet3 span must recalculate the dependent 3-D SUM instead of leaving the stale pre-move total");
        });
    }

    [Fact]
    public void MoveSheetTabContextMenuCommand_MovingSheetOutOfSpan_RecalculatesThreeDSpanFormula()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = new MainWindowHarness();
            var workbook = harness.Workbook;

            var sheet1 = workbook.GetSheetAt(0);
            var sheet2 = workbook.AddSheet("Sheet2");
            var sheet4 = workbook.AddSheet("Sheet4");
            var sheet3 = workbook.AddSheet("Sheet3");

            sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
            sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
            sheet4.SetCell(new CellAddress(sheet4.Id, 1, 1), new NumberValue(100));
            sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));
            sheet1.SetFormula(new CellAddress(sheet1.Id, 1, 2), "SUM(Sheet1:Sheet3!A1)");

            harness.RecalculateWorkbook();
            sheet1.GetValue(1, 2).Should().Be(new NumberValue(106), "Sheet4 currently sits inside the Sheet1:Sheet3 span");

            // "Move Right" on Sheet4 (right-click context menu / Ctrl+... shortcut) swaps it with
            // Sheet3, moving it out of the span -- exercises MoveSheetTab via SheetCtxMoveRight_Click.
            harness.InvokeSheetTabContextMenuClick("SheetCtxMoveRight_Click", sheet4.Id);

            workbook.Sheets.Select(s => s.Id).Should().Equal(sheet1.Id, sheet2.Id, sheet3.Id, sheet4.Id);
            sheet1.GetValue(1, 2).Should().Be(
                new NumberValue(6),
                "moving Sheet4 out of the Sheet1:Sheet3 span via the Move-Right command must recalculate the dependent 3-D SUM");
        });
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
                NullUserMessageService.Instance);

            Window.Show();
            PumpDispatcher();

            // MainWindow_Loaded (fired by Show() above) replaces the constructor-supplied workbook
            // with a fresh one via CreateNewWorkbook() -- capture the *live* workbook afterward so
            // the test operates on the same Workbook instance MainWindow's handlers use.
            Workbook = workbookRef.Current;
        }

        public void RecalculateWorkbook() => InvokeVoid("RecalculateWorkbook");

        public void SetDragSheetTabState(SheetId draggedSheetId, int toIndex)
        {
            SetField("_dragSheetTabId", (SheetId?)draggedSheetId);
            SetField("_dragSheetTabPendingToIndex", (int?)toIndex);
        }

        public void InvokeVoid(string methodName)
        {
            var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic, [])
                ?? throw new MissingMethodException(nameof(MainWindow), methodName);
            method.Invoke(Window, []);
        }

        public void InvokeSheetTabContextMenuClick(string methodName, SheetId clickedSheetId)
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
            method!.Invoke(Window, [menuItem, new RoutedEventArgs()]);
        }

        private void SetField(string fieldName, object? value)
        {
            var field = typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), fieldName);
            field.SetValue(Window, value);
        }

        public void Dispose()
        {
            foreach (Window ownedWindow in Window.OwnedWindows.Cast<Window>().ToList())
                ownedWindow.Close();
            MainWindowTestCleanup.CloseWithoutSavePrompt(Window);
            PumpDispatcher();
        }
    }

    // r446: delegates to the one fixed implementation -- see DispatcherTestPump.
    private static void PumpDispatcher() => DispatcherTestPump.PumpDispatcher();
}
