using FluentAssertions;
using FreeX.App.Presentation.DataTools;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R68-commands-group-outline-6-1 (src/FreeX.App.Host/MainWindow.DataCommands.cs,
/// SubtotalBtn_Click's "Replace current subtotals" composite, extracted into CreateSubtotalApplyCommand).
///
/// Before the fix: the composite [RemoveSubtotalRowsCommand(sheetRange), SubtotalCommand(sheetRange)]
/// built BOTH commands with the SAME pre-removal sheetRange. RemoveSubtotalRowsCommand deletes the old
/// subtotal rows (shifting every row below them up), but the new SubtotalCommand's range still spanned
/// the old (larger) extent -- so once the block shrank, that same absolute range reached past the
/// restored data and swept in whatever had shifted up to fill the vacated rows (e.g. unrelated content
/// that used to sit just below the subtotaled block), folding it into the new subtotal pass.
///
/// After the fix, CreateSubtotalApplyCommand predicts how many rows RemoveSubtotalRowsCommand is about
/// to delete (by counting existing SUBTOTAL(...) formula rows in the stale range) and shrinks the new
/// SubtotalCommand's range by that count before building the composite, so the new pass only ever scans
/// the actual (post-removal) data block.
/// </summary>
public sealed class R68_SubtotalReplaceRangeCorrectionTests
{
    [Fact]
    public void CreateSubtotalApplyCommand_ReplaceCurrentSubtotals_DoesNotFoldInUnrelatedRowsBelow()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                SeedSubtotalData(sheet, sheetId);

                var ctx = new TestCommandContext(workbook);
                var firstRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 7, 2));
                var applyResult = new SubtotalDialogPlanResult(
                    GroupColumnOffset: 0,
                    SubtotalColumnOffsets: [1],
                    FunctionNumber: 9,
                    ReplaceCurrentSubtotals: false,
                    PageBreakBetweenGroups: false,
                    SummaryBelowData: true);

                var firstCommand = (IWorkbookCommand)R49MainWindowTestHarness.Invoke(
                    window, "CreateSubtotalApplyCommand", sheetId, firstRange, applyResult)!;
                var firstOutcome = firstCommand.Apply(ctx);
                firstOutcome.Success.Should().BeTrue(firstOutcome.ErrorMessage);

                // The first pass inserted group + grand-total subtotal rows below the original 7-row
                // block; place unrelated content directly adjacent below it, exactly like the
                // originally-reported scenario (no blank-row gap).
                var usedAfterFirst = sheet.GetUsedRange()!.Value;
                usedAfterFirst.End.Row.Should().BeGreaterThan(7, "the first Subtotal pass must have inserted rows");
                var unrelatedRow = usedAfterFirst.End.Row + 1;
                sheet.SetCell(new CellAddress(sheetId, unrelatedRow, 1), new TextValue("UNRELATED"));

                // Re-Subtotal with Replace, using the CURRENT (stale, post-first-pass) block extent --
                // exactly what SubtotalBtn_Click passes as sheetRange on a second invocation.
                var secondRange = new GridRange(
                    new CellAddress(sheetId, 1, 1),
                    new CellAddress(sheetId, usedAfterFirst.End.Row, 2));
                var replaceResult = applyResult with { ReplaceCurrentSubtotals = true };

                var secondCommand = (IWorkbookCommand)R49MainWindowTestHarness.Invoke(
                    window, "CreateSubtotalApplyCommand", sheetId, secondRange, replaceResult)!;
                var secondOutcome = secondCommand.Apply(ctx);
                secondOutcome.Success.Should().BeTrue(secondOutcome.ErrorMessage);

                sheet.GetValue(unrelatedRow, 1).Should().Be(
                    new TextValue("UNRELATED"),
                    "the replace pass must not fold rows that shifted up into the vacated subtotal-row space into the new subtotal scan");
                sheet.GetCell(unrelatedRow, 2).Should().BeNull(
                    "the unrelated row must not receive a subtotal formula from the new pass");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void CreateSubtotalApplyCommand_FirstTimeApply_ReturnsPlainSubtotalCommandUnaffected()
    {
        // Sibling/no-regression: a first-time (non-replace) Subtotal apply must still be the plain
        // SubtotalCommand over the caller-supplied range, untouched by the range-correction fix.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                SeedSubtotalData(sheet, sheetId);

                var ctx = new TestCommandContext(workbook);
                var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 7, 2));
                var applyResult = new SubtotalDialogPlanResult(0, [1], 9, false, false, true);

                var command = (IWorkbookCommand)R49MainWindowTestHarness.Invoke(
                    window, "CreateSubtotalApplyCommand", sheetId, range, applyResult)!;

                command.Should().BeOfType<SubtotalCommand>("a first-time apply must not be wrapped in a remove-then-reapply composite");

                var outcome = command.Apply(ctx);
                outcome.Success.Should().BeTrue(outcome.ErrorMessage);
                sheet.GetUsedRange()!.Value.End.Row.Should().BeGreaterThan(7, "the plain apply must still insert the expected subtotal rows");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static void SeedSubtotalData(Sheet sheet, SheetId sheetId)
    {
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Value"));

        sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheetId, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheetId, 4, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheetId, 4, 2), new NumberValue(30));

        sheet.SetCell(new CellAddress(sheetId, 5, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheetId, 5, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheetId, 6, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheetId, 6, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheetId, 7, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheetId, 7, 2), new NumberValue(3));
    }
}
