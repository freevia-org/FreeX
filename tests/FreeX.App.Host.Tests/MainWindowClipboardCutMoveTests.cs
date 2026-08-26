using System.Reflection;
using System.Windows;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

// Regression coverage for F13 (WPF-host twin of the Avalonia cut+paste-must-move-not-copy fix):
// Ctrl+X then Ctrl+V must route through MoveRangeCommand semantics, not the copy-paste +
// ClearContents combo, so the moved formula's OWN references are left unchanged while OTHER
// formulas that pointed at the cut cells follow the move.
public sealed class MainWindowClipboardCutMoveTests
{
    [Fact]
    public void CutThenPaste_KeepsMovedFormulaOwnReferenceAndUpdatesReferencingFormula()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance,
                platformClipboard: new InMemoryPlatformClipboard());

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var b1 = new CellAddress(sheet.Id, 1, 2);
                var d1 = new CellAddress(sheet.Id, 1, 4);
                sheet.SetCell(a1, new NumberValue(5));
                sheet.SetFormula(b1, "A1");
                // Another cell referencing the cell being cut (B1); Excel updates this reference
                // to follow the move.
                var otherRefCell = new CellAddress(sheet.Id, 2, 1);
                sheet.SetFormula(otherRefCell, "B1");

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(b1, b1);

                InvokeClickHandler(window, "CutBtn_Click");
                PumpDispatcher();

                grid.SelectedRange = new GridRange(d1, d1);
                InvokeClickHandler(window, "PasteBtn_Click");
                PumpDispatcher();

                // Moved cell keeps its own formula reference unchanged.
                sheet.GetCell(d1)!.FormulaText.Should().Be("A1");
                sheet.GetCell(d1)!.Value.Should().Be(new NumberValue(5));
                // Source cell was moved away, not merely cleared.
                sheet.GetCell(b1).Should().BeNull();
                // The other formula that referenced the cut cell now follows the move.
                sheet.GetCell(otherRefCell)!.FormulaText.Should().Be("D1");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void CopyThenPaste_StillOffsetsOwnFormulaReferenceAndLeavesSourceIntact()
    {
        StaTestRunner.Run(() =>
        {
            var initialWorkbook = new Workbook("Book1");
            initialWorkbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = initialWorkbook };
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                initialWorkbook,
                NullUserMessageService.Instance,
                platformClipboard: new InMemoryPlatformClipboard());

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var b1 = new CellAddress(sheet.Id, 1, 2);
                var d1 = new CellAddress(sheet.Id, 1, 4);
                sheet.SetCell(a1, new NumberValue(5));
                sheet.SetFormula(b1, "A1");

                var grid = (GridView)window.FindName("SheetGrid");
                grid.SelectedRange = new GridRange(b1, b1);

                InvokeClickHandler(window, "CopyBtn_Click");
                PumpDispatcher();

                grid.SelectedRange = new GridRange(d1, d1);
                InvokeClickHandler(window, "PasteBtn_Click");
                PumpDispatcher();

                // A plain copy still offsets the formula's own reference by the paste offset.
                sheet.GetCell(d1)!.FormulaText.Should().Be("C1");
                // Source cell is left untouched by a copy.
                sheet.GetCell(b1)!.FormulaText.Should().Be("A1");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    private static void InvokeClickHandler(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(object), typeof(RoutedEventArgs)]);
        method.Should().NotBeNull($"{methodName} should exist as a private click handler on MainWindow");
        method!.Invoke(window, [window, new RoutedEventArgs()]);
    }
}
