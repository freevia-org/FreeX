using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for R68-commands-group-outline-6-1, carried over from
/// R68_SubtotalReplaceRangeCorrectionTests when "Share FreeX subtotal execution" moved the
/// "Replace current subtotals" composite off MainWindow into <see cref="WorkbookSession"/>.
///
/// Before the fix: the composite [RemoveSubtotalRowsCommand(sheetRange), SubtotalCommand(sheetRange)]
/// built BOTH commands with the SAME pre-removal sheetRange. RemoveSubtotalRowsCommand deletes the old
/// subtotal rows (shifting every row below them up), but the new SubtotalCommand's range still spanned
/// the old (larger) extent -- so once the block shrank, that same absolute range reached past the
/// restored data and swept in whatever had shifted up to fill the vacated rows, folding unrelated
/// content that used to sit just below the subtotaled block into the new subtotal pass.
///
/// The session-level tests next door cover replace-existing generally, but only assert that one
/// trailing cell stays empty. This case is the one that actually reproduces the reported bug: real
/// unrelated content sits directly below the block, with no blank-row gap, and must survive a second
/// replace pass driven by the stale (post-first-pass) extent.
/// </summary>
public sealed class WorkbookSessionSubtotalReplaceRangeTests
{
    [Fact]
    public void ExecuteSubtotalOptions_ReplaceExisting_DoesNotFoldInUnrelatedRowsBelow()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        SeedSubtotalData(sheet);
        var session = CreateSession(workbook);

        session.SelectRange(Range(sheet, 1, 1, 7, 2));
        session.ExecuteSubtotalOptions(CreateSumOptions()).Success.Should().BeTrue();

        // The first pass inserted group + grand-total subtotal rows below the original 7-row block;
        // place unrelated content directly adjacent below it, exactly like the reported scenario
        // (no blank-row gap).
        var usedAfterFirst = sheet.GetUsedRange()!.Value;
        usedAfterFirst.End.Row.Should().BeGreaterThan(7, "the first Subtotal pass must have inserted rows");
        var unrelatedRow = usedAfterFirst.End.Row + 1;
        sheet.SetCell(Address(sheet, unrelatedRow, 1), new TextValue("UNRELATED"));

        // Re-Subtotal with Replace over the CURRENT (stale, post-first-pass) block extent.
        session.SelectRange(Range(sheet, 1, 1, usedAfterFirst.End.Row, 2));

        var result = session.ExecuteSubtotalOptions(CreateSumOptions(replaceExisting: true));

        result.Success.Should().BeTrue();
        sheet.GetValue(unrelatedRow, 1).Should().Be(
            new TextValue("UNRELATED"),
            "the replace pass must not fold rows that shifted up into the vacated subtotal-row space into the new subtotal scan");
        sheet.GetCell(Address(sheet, unrelatedRow, 2)).Should().BeNull(
            "the unrelated row must not receive a subtotal formula from the new pass");
    }

    private static void SeedSubtotalData(Sheet sheet)
    {
        SetText(sheet, 1, 1, "Category");
        SetText(sheet, 1, 2, "Value");

        SetText(sheet, 2, 1, "A");
        SetNumber(sheet, 2, 2, 10);
        SetText(sheet, 3, 1, "A");
        SetNumber(sheet, 3, 2, 20);
        SetText(sheet, 4, 1, "A");
        SetNumber(sheet, 4, 2, 30);

        SetText(sheet, 5, 1, "B");
        SetNumber(sheet, 5, 2, 1);
        SetText(sheet, 6, 1, "B");
        SetNumber(sheet, 6, 2, 2);
        SetText(sheet, 7, 1, "B");
        SetNumber(sheet, 7, 2, 3);
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }

    private static SubtotalInputOptions CreateSumOptions(bool replaceExisting = false) =>
        new(
            GroupColumnOffset: 0,
            SubtotalColumnOffsets: [1],
            FunctionNumber: 9,
            ReplaceExisting: replaceExisting,
            PageBreakBetweenGroups: false,
            SummaryBelowData: true);

    private static GridRange Range(Sheet sheet, uint startRow, uint startColumn, uint endRow, uint endColumn) =>
        new(Address(sheet, startRow, startColumn), Address(sheet, endRow, endColumn));

    private static void SetText(Sheet sheet, uint row, uint column, string value) =>
        sheet.SetCell(Address(sheet, row, column), new TextValue(value));

    private static void SetNumber(Sheet sheet, uint row, uint column, double value) =>
        sheet.SetCell(Address(sheet, row, column), new NumberValue(value));

    private static CellAddress Address(Sheet sheet, uint row, uint column) =>
        new(sheet.Id, row, column);
}
