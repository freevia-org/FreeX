using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class SelectionStyleCommandPlannerTests
{
    private static readonly CellColor Accent = new(33, 115, 70);

    [Fact]
    public void ResolveRanges_MergesCompleteRowBandSelectionIntoOneRectangle()
    {
        var sheetId = SheetId.New();
        var active = Range(sheetId, 4, 1, 4, 3);
        var selectedRanges = new[]
        {
            Range(sheetId, 1, 1, 1, 3),
            Range(sheetId, 2, 1, 2, 3),
            Range(sheetId, 3, 1, 3, 3),
            Range(sheetId, 4, 1, 4, 3)
        };

        var ranges = SelectionStyleCommandPlanner.ResolveRanges(active, selectedRanges);

        ranges.Should().ContainSingle()
            .Which.Should().Be(Range(sheetId, 1, 1, 4, 3));
    }

    [Fact]
    public void ResolveRanges_LeavesIncompleteSelectionsSplit()
    {
        var sheetId = SheetId.New();
        var active = Range(sheetId, 4, 1, 4, 3);
        var selectedRanges = new[]
        {
            Range(sheetId, 1, 1, 1, 3),
            Range(sheetId, 3, 1, 3, 3),
            Range(sheetId, 4, 1, 4, 3)
        };

        var ranges = SelectionStyleCommandPlanner.ResolveRanges(active, selectedRanges);

        ranges.Should().Equal(selectedRanges);
    }

    [Fact]
    public void CreatePerCellStyleCommand_UsesMergedSelectionForInsideBorders()
    {
        var workbook = new Workbook("SelectionStyleCommandPlannerTests");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        var active = Range(sheet.Id, 4, 1, 4, 3);
        var selectedRanges = new[]
        {
            Range(sheet.Id, 1, 1, 1, 3),
            Range(sheet.Id, 2, 1, 2, 3),
            Range(sheet.Id, 3, 1, 3, 3),
            Range(sheet.Id, 4, 1, 4, 3)
        };
        var ranges = SelectionStyleCommandPlanner.ResolveRanges(active, selectedRanges);

        var command = SelectionStyleCommandPlanner.CreatePerCellStyleCommand(
            [sheet.Id],
            ranges,
            (range, address) => BorderShortcutService.GetInsideBorderDiff(
                range,
                address,
                BorderStyle.Thin,
                Accent),
            "Inside Borders");

        command.Apply(context).Success.Should().BeTrue();

        var expected = new CellBorder(BorderStyle.Thin, Accent);
        GetStyle(workbook, sheet, 1, 1).BorderBottom.Should().Be(expected);
        GetStyle(workbook, sheet, 1, 1).BorderRight.Should().Be(expected);
        GetStyle(workbook, sheet, 2, 2).BorderTop.Should().Be(expected);
        GetStyle(workbook, sheet, 2, 2).BorderRight.Should().Be(expected);
        GetStyle(workbook, sheet, 2, 2).BorderBottom.Should().Be(expected);
        GetStyle(workbook, sheet, 2, 2).BorderLeft.Should().Be(expected);
        GetStyle(workbook, sheet, 4, 3).BorderTop.Should().Be(expected);
        GetStyle(workbook, sheet, 4, 3).BorderLeft.Should().Be(expected);
    }

    [Fact]
    public void CreateApplyStyleCommand_AppliesStyleToAllSelectedRanges()
    {
        var workbook = new Workbook("SelectionStyleApplyTests");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);
        var ranges = new[]
        {
            Range(sheet.Id, 1, 1, 1, 2),
            Range(sheet.Id, 3, 1, 3, 2)
        };

        var command = SelectionStyleCommandPlanner.CreateApplyStyleCommand(
            [sheet.Id],
            ranges,
            new StyleDiff(Bold: true),
            "Apply Style");

        command.Apply(context).Success.Should().BeTrue();

        GetStyle(workbook, sheet, 1, 1).Bold.Should().BeTrue();
        GetStyle(workbook, sheet, 1, 2).Bold.Should().BeTrue();
        GetStyle(workbook, sheet, 2, 1).Bold.Should().BeFalse();
        GetStyle(workbook, sheet, 3, 1).Bold.Should().BeTrue();
        GetStyle(workbook, sheet, 3, 2).Bold.Should().BeTrue();
    }

    [Fact]
    public void CreateRangeCommand_AppliesNonStyleCommandsToAllRangesAndGroupedSheets()
    {
        var workbook = new Workbook("SelectionRangeCommandTests");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var context = new TestCommandContext(workbook);
        SetText(sheet1, 1, 1, "clear");
        SetText(sheet1, 2, 1, "keep");
        SetText(sheet1, 3, 1, "clear");
        SetText(sheet2, 1, 1, "clear");
        SetText(sheet2, 2, 1, "keep");
        SetText(sheet2, 3, 1, "clear");
        var ranges = new[]
        {
            Range(sheet1.Id, 1, 1, 1, 1),
            Range(sheet1.Id, 3, 1, 3, 1)
        };

        var command = SelectionStyleCommandPlanner.CreateRangeCommand(
            [sheet1.Id, sheet2.Id],
            ranges,
            (sheetId, range) => new ClearContentsCommand(sheetId, range),
            "Clear Contents");

        command.Apply(context).Success.Should().BeTrue();

        sheet1.GetCell(new CellAddress(sheet1.Id, 1, 1))!.Value.Should().Be(BlankValue.Instance);
        sheet1.GetCell(new CellAddress(sheet1.Id, 2, 1))!.Value.Should().Be(new TextValue("keep"));
        sheet1.GetCell(new CellAddress(sheet1.Id, 3, 1))!.Value.Should().Be(BlankValue.Instance);
        sheet2.GetCell(new CellAddress(sheet2.Id, 1, 1))!.Value.Should().Be(BlankValue.Instance);
        sheet2.GetCell(new CellAddress(sheet2.Id, 2, 1))!.Value.Should().Be(new TextValue("keep"));
        sheet2.GetCell(new CellAddress(sheet2.Id, 3, 1))!.Value.Should().Be(BlankValue.Instance);
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));

    private static void SetText(Sheet sheet, uint row, uint col, string value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new TextValue(value)));

    private static CellStyle GetStyle(Workbook workbook, Sheet sheet, uint row, uint col)
    {
        var address = new CellAddress(sheet.Id, row, col);
        var styleId = sheet.GetCell(address)?.StyleId ??
            sheet.GetStyleOnly(row, col) ??
            StyleId.Default;
        return workbook.GetStyle(styleId);
    }
}
