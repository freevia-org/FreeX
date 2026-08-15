using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for replacing existing subtotal rows through the shared workbook session.
/// The replacement pass must resolve its range after removing old subtotal rows so adjacent user
/// data is never pulled into the new subtotal scan.
/// </summary>
public sealed class R68_SubtotalReplaceRangeCorrectionTests
{
    [Fact]
    public void ExecuteSubtotalOptions_ReplaceCurrentSubtotals_DoesNotFoldInUnrelatedRowsBelow()
    {
        var workbook = new Workbook("SubtotalReplaceRange");
        var sheet = workbook.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        SeedSubtotalData(sheet, sheetId);
        var session = CreateSession(workbook);
        session.SelectRange(Range(sheetId, 1, 1, 7, 2));

        var firstResult = session.ExecuteSubtotalOptions(CreateOptions(replaceExisting: false));
        firstResult.Success.Should().BeTrue(firstResult.ErrorMessage);

        var usedAfterFirst = sheet.GetUsedRange()!.Value;
        usedAfterFirst.End.Row.Should().BeGreaterThan(7, "the first pass must insert subtotal rows");
        var unrelatedRow = usedAfterFirst.End.Row + 1;
        sheet.SetCell(new CellAddress(sheetId, unrelatedRow, 1), new TextValue("UNRELATED"));
        session.SelectRange(Range(sheetId, 1, 1, usedAfterFirst.End.Row, 2));

        var replaceResult = session.ExecuteSubtotalOptions(CreateOptions(replaceExisting: true));

        replaceResult.Success.Should().BeTrue(replaceResult.ErrorMessage);
        sheet.GetValue(unrelatedRow, 1).Should().Be(
            new TextValue("UNRELATED"),
            "the replace pass must not fold rows shifted into the old subtotal space into the new scan");
        sheet.GetCell(unrelatedRow, 2).Should().BeNull(
            "the unrelated row must not receive a subtotal formula from the new pass");
    }

    [Fact]
    public void ExecuteSubtotalOptions_FirstTimeApply_RemainsUnaffected()
    {
        var workbook = new Workbook("SubtotalFirstApply");
        var sheet = workbook.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        SeedSubtotalData(sheet, sheetId);
        var session = CreateSession(workbook);
        session.SelectRange(Range(sheetId, 1, 1, 7, 2));

        var result = session.ExecuteSubtotalOptions(CreateOptions(replaceExisting: false));

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetUsedRange()!.Value.End.Row.Should().BeGreaterThan(7,
            "a first apply must still insert the expected subtotal rows");
        session.CanUndo.Should().BeTrue();
        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.GetUsedRange()!.Value.End.Row.Should().Be(7);
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static SubtotalInputOptions CreateOptions(bool replaceExisting) =>
        new(
            GroupColumnOffset: 0,
            SubtotalColumnOffsets: [1],
            FunctionNumber: 9,
            ReplaceExisting: replaceExisting,
            PageBreakBetweenGroups: false,
            SummaryBelowData: true);

    private static GridRange Range(
        SheetId sheetId,
        uint startRow,
        uint startColumn,
        uint endRow,
        uint endColumn) =>
        new(
            new CellAddress(sheetId, startRow, startColumn),
            new CellAddress(sheetId, endRow, endColumn));

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
