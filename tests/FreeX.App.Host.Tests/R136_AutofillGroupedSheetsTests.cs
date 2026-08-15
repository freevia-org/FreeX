using System.Reflection;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R136-services-autofill-grouped-sheets-1 (src/FreeX.App.Host/MainWindow.CellsCommands.cs,
/// ExecuteAutofill) -- WPF host twin of the FreeX.App.Services.WorkbookSession.AutofillDragRange
/// fix (see R136_AutofillDragRangeGroupedSheetsTests in FreeX.App.Services.Tests).
///
/// Excel's Group Editing mode mirrors every edit made on the active sheet -- including a
/// fill-handle drag -- to every other grouped sheet, matching this same file's ExecuteFillCells
/// (routed through _session.FillSelectedRange, which already fans out via
/// CurrentGroupedEditSheetIds) and CreateFlashFillCommand. ExecuteAutofill used to run a single
/// AutofillCommand against _currentSheetId only, so dragging the fill handle silently ignored
/// any other sheet in the group.
/// </summary>
public sealed class R136_AutofillGroupedSheetsTests
{
    [Fact]
    public void ExecuteAutofill_WithGroupedSheets_FansOutToEveryGroupedSheet()
    {
        StaTestRunner.RunClipboardIsolated(() =>
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
                NullUserMessageService.Instance);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet1 = workbook.GetSheetAt(0);
                var sheet2 = workbook.AddSheet("Sheet2");
                GroupSheets(window, sheet1.Id, sheet2.Id);

                var sheet1A1 = new CellAddress(sheet1.Id, 1, 1);
                var sheet1A2 = new CellAddress(sheet1.Id, 2, 1);
                var sheet2A2 = new CellAddress(sheet2.Id, 2, 1);
                sheet1.SetCell(sheet1A1, new NumberValue(1));
                sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(1));

                var sourceRange = new GridRange(sheet1A1, sheet1A1);
                var fillRange = new GridRange(sheet1A2, sheet1A2);

                // ctrlHeld: true flips a single numeric source cell into series (increment) mode
                // (AutofillCommand.WantsSingleCellSeriesDefault) so the fill's effect (1 -> 2) is
                // observably different from a plain copy, making the fan-out unmistakable.
                InvokeExecuteAutofill(window, sourceRange, fillRange, ctrlHeld: true);
                PumpDispatcher();

                sheet1.GetValue(sheet1A2).Should().Be(new NumberValue(2),
                    "the active sheet's own fill-handle drag must still autofill as before");
                sheet2.GetValue(sheet2A2).Should().Be(new NumberValue(2),
                    "Excel's Group Editing mode mirrors a fill-handle drag to every other grouped " +
                    "sheet, remapped onto that sheet's own cells");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    // Sibling no-regression: without a sheet group, only the active sheet is touched.
    [Fact]
    public void ExecuteAutofill_WithoutGroupedSheets_OnlyAffectsActiveSheet()
    {
        StaTestRunner.RunClipboardIsolated(() =>
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
                NullUserMessageService.Instance);

            try
            {
                window.Show();
                PumpDispatcher();

                var workbook = workbookRef.Current;
                var sheet1 = workbook.GetSheetAt(0);
                var sheet2 = workbook.AddSheet("Sheet2");

                var sheet1A1 = new CellAddress(sheet1.Id, 1, 1);
                var sheet1A2 = new CellAddress(sheet1.Id, 2, 1);
                var sheet2A2 = new CellAddress(sheet2.Id, 2, 1);
                sheet1.SetCell(sheet1A1, new NumberValue(1));

                var sourceRange = new GridRange(sheet1A1, sheet1A1);
                var fillRange = new GridRange(sheet1A2, sheet1A2);

                InvokeExecuteAutofill(window, sourceRange, fillRange, ctrlHeld: true);
                PumpDispatcher();

                sheet1.GetValue(sheet1A2).Should().Be(new NumberValue(2));
                (sheet2.GetCell(sheet2A2)?.Value ?? BlankValue.Instance).Should().Be(
                    BlankValue.Instance,
                    "without an active sheet group, a fill-handle drag must touch only the active sheet");
            }
            finally
            {
                MainWindowTestCleanup.CloseWithoutSavePrompt(window);
                PumpDispatcher();
            }
        });
    }

    private static void InvokeExecuteAutofill(
        MainWindow window, GridRange sourceRange, GridRange fillRange, bool ctrlHeld)
    {
        var method = typeof(MainWindow).GetMethod("ExecuteAutofill", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteAutofill");
        method.Invoke(window, [sourceRange, fillRange, ctrlHeld]);
    }

    private static void GroupSheets(MainWindow window, params SheetId[] sheetIds)
    {
        var field = typeof(MainWindow).GetField("_groupedSheetIds", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_groupedSheetIds");
        var groupedSheetIds = (HashSet<SheetId>)field.GetValue(window)!;
        groupedSheetIds.Clear();
        foreach (var sheetId in sheetIds)
            groupedSheetIds.Add(sheetId);
    }
}
