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

    // R142-comments-notes-1 x R136-services-autofill-grouped-sheets-1: the WPF host's
    // ExecuteAutofill fan-out builds one AutofillCommand PER grouped sheet (see the plain
    // CompositeWorkbookCommand construction above this test's sibling, not a call through
    // WorkbookSession.AutofillDragRange -- that method belongs to the Avalonia shell only, see
    // MainWindow.cs's two call sites). AutofillCommand.Apply carries a source cell's legacy note
    // and threaded comment to the fill destination (R142-comments-notes-1); this pins that the
    // same carrying happens on EVERY grouped sheet reached through this WPF fan-out, not just the
    // active sheet, i.e. that the grouped-sheet composite doesn't only forward cell values while
    // dropping annotations on the non-active members.
    [Fact]
    public void ExecuteAutofill_WithGroupedSheets_CarriesCommentToEveryGroupedSheet()
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
                var sheet2A1 = new CellAddress(sheet2.Id, 1, 1);
                var sheet2A2 = new CellAddress(sheet2.Id, 2, 1);
                // Group Editing mirrors the OPERATION to every grouped sheet's own data, not the
                // active sheet's literal cell content (confirmed by the sibling FansOutToEveryGroupedSheet
                // test above, where sheet2 gets its OWN value incremented from its OWN source cell) --
                // so each sheet needs its own source-cell note/threaded comment for this test to
                // observe a genuine per-sheet carry rather than accidentally passing on a value that
                // was never actually copied across sheets.
                sheet1.SetCell(sheet1A1, new NumberValue(1));
                sheet1.Comments[sheet1A1] = "Sheet1 note";
                sheet1.CommentAuthors[sheet1A1] = "Alice";
                sheet1.ShownComments.Add(sheet1A1);
                sheet1.ThreadedComments[sheet1A1] = new ThreadedComment("Sheet1 thread") { Id = "{SRC-ID-1}" };
                sheet2.SetCell(sheet2A1, new NumberValue(1));
                sheet2.Comments[sheet2A1] = "Sheet2 note";
                sheet2.CommentAuthors[sheet2A1] = "Bob";
                sheet2.ShownComments.Add(sheet2A1);
                sheet2.ThreadedComments[sheet2A1] = new ThreadedComment("Sheet2 thread") { Id = "{SRC-ID-2}" };

                var sourceRange = new GridRange(sheet1A1, sheet1A1);
                var fillRange = new GridRange(sheet1A2, sheet1A2);

                InvokeExecuteAutofill(window, sourceRange, fillRange, ctrlHeld: true);
                PumpDispatcher();

                sheet1.GetValue(sheet1A2).Should().Be(new NumberValue(2));
                sheet1.Comments[sheet1A2].Should().Be("Sheet1 note",
                    "the active sheet's own fill-handle drag must still carry the note as before");

                sheet2.GetValue(sheet2A2).Should().Be(new NumberValue(2),
                    "Excel's Group Editing mode mirrors a fill-handle drag to every other grouped sheet");
                sheet2.Comments.Should().ContainKey(sheet2A2).WhoseValue.Should().Be("Sheet2 note",
                    "the grouped-sheet fan-out builds an independent AutofillCommand per sheet, remapped " +
                    "onto that sheet's own source cell, and R142-comments-notes-1's comment-carrying " +
                    "must run for each of them off their own sheet's data, not only the active sheet's");
                sheet2.CommentAuthors[sheet2A2].Should().Be("Bob");
                sheet2.ShownComments.Should().Contain(sheet2A2);
                sheet2.ThreadedComments[sheet2A2].Text.Should().Be("Sheet2 thread");
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
