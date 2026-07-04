using FluentAssertions;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class RowColumnSizingPlannerTests
{
    [Fact]
    public void GetDialogValues_UseExplicitDimensionThenSheetDefault()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var range = Range(sheet.Id, 3, 4, 5, 6);
        sheet.DefaultRowHeight = 18;
        sheet.DefaultColumnWidth = 9.25;
        sheet.RowHeights[3] = 24;
        sheet.ColumnWidths[4] = 14.5;

        RowColumnSizingPlanner.GetRowHeightDialogValue(sheet, range).Should().Be(24);
        RowColumnSizingPlanner.GetColumnWidthDialogValue(sheet, range).Should().Be(14.5);
        RowColumnSizingPlanner.GetRowHeightDialogValue(sheet, Range(sheet.Id, 7, 4, 7, 4)).Should().Be(18);
        RowColumnSizingPlanner.GetColumnWidthDialogValue(sheet, Range(sheet.Id, 3, 8, 3, 8)).Should().Be(9.25);
        RowColumnSizingPlanner.GetRowHeightDialogValue(null, range).Should().Be(20);
        RowColumnSizingPlanner.GetColumnWidthDialogValue(null, range).Should().Be(8.43);
    }

    [Fact]
    public void CreateDimensionCommands_ApplyToSelectionSpans()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        RowColumnSizingPlanner.CreateRowHeightCommand(sheet.Id, Range(sheet.Id, 2, 3, 4, 5), 30)
            .Apply(context)
            .Success.Should()
            .BeTrue();
        RowColumnSizingPlanner.CreateColumnWidthCommand(sheet.Id, Range(sheet.Id, 2, 3, 4, 5), 12)
            .Apply(context)
            .Success.Should()
            .BeTrue();

        sheet.RowHeights.Should().ContainKeys(2u, 3u, 4u);
        sheet.RowHeights.Values.Should().OnlyContain(height => height == 30);
        sheet.ColumnWidths.Should().ContainKeys(3u, 4u, 5u);
        sheet.ColumnWidths.Values.Should().OnlyContain(width => width == 12);
    }

    [Fact]
    public void CreateHiddenCommands_ApplyToSelectionSpans()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        RowColumnSizingPlanner.CreateRowsHiddenCommand(sheet.Id, Range(sheet.Id, 2, 4, 3, 6), hidden: true)
            .Apply(context)
            .Success.Should()
            .BeTrue();
        RowColumnSizingPlanner.CreateColumnsHiddenCommand(sheet.Id, Range(sheet.Id, 2, 4, 3, 6), hidden: true)
            .Apply(context)
            .Success.Should()
            .BeTrue();

        sheet.HiddenRows.Should().Contain([2u, 3u]);
        sheet.HiddenCols.Should().Contain([4u, 5u, 6u]);
    }

    [Fact]
    public void CreateAutoFitCommands_ApplySingleAndCompositePlans()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new TestCommandContext(workbook);

        var rowCommand = RowColumnSizingPlanner.CreateAutoFitRowHeightCommand(
            sheet.Id,
            [new RowColumnSizePlan(2, 21), new RowColumnSizePlan(3, 34)]);
        var columnCommand = RowColumnSizingPlanner.CreateAutoFitColumnWidthCommand(
            sheet.Id,
            [new RowColumnSizePlan(5, 18)]);

        rowCommand.Should().NotBeNull();
        columnCommand.Should().NotBeNull();
        rowCommand!.Apply(context).Success.Should().BeTrue();
        columnCommand!.Apply(context).Success.Should().BeTrue();

        sheet.RowHeights[2].Should().Be(21);
        sheet.RowHeights[3].Should().Be(34);
        sheet.ColumnWidths[5].Should().Be(18);
    }

    [Fact]
    public void CreateAutoFitCommands_ReturnNullWhenThereIsNothingToApply()
    {
        RowColumnSizingPlanner.CreateAutoFitRowHeightCommand(SheetId.New(), []).Should().BeNull();
        RowColumnSizingPlanner.CreateAutoFitColumnWidthCommand(SheetId.New(), []).Should().BeNull();
    }

    [Fact]
    public void PlanRowHeights_MeasuresEachSelectedRowIndependently()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        var selection = Range(sheetId, row1: 2, col1: 3, row2: 3, col2: 4);

        var plan = RowColumnSizingPlanner.PlanAutoFitRowHeights(
            sheet,
            selection,
            usedRange: selection,
            (row, col) => (row, col) switch
            {
                (2, 3) => new AutoFitCellText("short"),
                (3, 4) => new AutoFitCellText("first\nsecond\nthird"),
                _ => null
            },
            defaultHeight: 20);

        plan.Should().Equal(
            new RowColumnSizePlan(2, AutoFitSizingService.EstimateRowHeight(["short"], 20)),
            new RowColumnSizePlan(3, AutoFitSizingService.EstimateRowHeight(["first\nsecond\nthird"], 20)));
    }

    [Fact]
    public void PlanColumnWidths_MeasuresEachSelectedColumnIndependently()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        var selection = Range(sheetId, row1: 2, col1: 3, row2: 3, col2: 4);

        var plan = RowColumnSizingPlanner.PlanAutoFitColumnWidths(
            sheet,
            selection,
            usedRange: selection,
            (row, col) => (row, col) switch
            {
                (2, 3) => new AutoFitCellText("short"),
                (3, 4) => new AutoFitCellText("a much longer display value"),
                _ => null
            },
            defaultWidth: 8.43);

        plan.Should().Equal(
            new RowColumnSizePlan(3, AutoFitSizingService.EstimateColumnWidth(["short"], 8.43)),
            new RowColumnSizePlan(4, AutoFitSizingService.EstimateColumnWidth(["a much longer display value"], 8.43)));
    }

    [Fact]
    public void PlanColumnWidths_ForWholeColumnSelection_BoundsMeasurementsToUsedRows()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        var wholeColumns = Range(sheetId, row1: 1, col1: 2, row2: CellAddress.MaxRow, col2: 3);
        var usedRange = Range(sheetId, row1: 10, col1: 1, row2: 12, col2: 5);
        var visited = new List<(uint Row, uint Col)>();

        RowColumnSizingPlanner.PlanAutoFitColumnWidths(
            sheet,
            wholeColumns,
            usedRange,
            (row, col) =>
            {
                visited.Add((row, col));
                return row == 11 && col == 3 ? new AutoFitCellText("wide text") : null;
            },
            defaultWidth: 8.43);

        visited.Should().OnlyContain(cell =>
            cell.Row >= 10 && cell.Row <= 12 &&
            cell.Col >= 2 && cell.Col <= 3);
        visited.Should().HaveCount(6);
    }

    [Fact]
    public void PlanRowHeights_ForWholeRowSelection_BoundsMeasurementsToUsedColumns()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        var wholeRows = Range(sheetId, row1: 4, col1: 1, row2: 5, col2: CellAddress.MaxCol);
        var usedRange = Range(sheetId, row1: 1, col1: 7, row2: 20, col2: 9);
        var visited = new List<(uint Row, uint Col)>();

        RowColumnSizingPlanner.PlanAutoFitRowHeights(
            sheet,
            wholeRows,
            usedRange,
            (row, col) =>
            {
                visited.Add((row, col));
                return row == 5 && col == 8 ? new AutoFitCellText("first\nsecond") : null;
            },
            defaultHeight: 20);

        visited.Should().OnlyContain(cell =>
            cell.Row >= 4 && cell.Row <= 5 &&
            cell.Col >= 7 && cell.Col <= 9);
        visited.Should().HaveCount(6);
    }

    [Fact]
    public void PlanAutoFit_ForEmptyWholeAxisSelection_NoOps()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        var wholeColumns = Range(sheetId, row1: 1, col1: 2, row2: CellAddress.MaxRow, col2: 3);
        var wholeRows = Range(sheetId, row1: 4, col1: 1, row2: 5, col2: CellAddress.MaxCol);

        RowColumnSizingPlanner.PlanAutoFitColumnWidths(sheet, wholeColumns, usedRange: null, (_, _) => new AutoFitCellText(""), defaultWidth: 8.43)
            .Should().BeEmpty();
        RowColumnSizingPlanner.PlanAutoFitRowHeights(sheet, wholeRows, usedRange: null, (_, _) => new AutoFitCellText(""), defaultHeight: 20)
            .Should().BeEmpty();
    }

    [Fact]
    public void PlanAutoFit_ForOppositeWholeAxisSelection_NoOps()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        var wholeColumns = Range(sheetId, row1: 1, col1: 2, row2: CellAddress.MaxRow, col2: 3);
        var wholeRows = Range(sheetId, row1: 4, col1: 1, row2: 5, col2: CellAddress.MaxCol);
        var usedRange = Range(sheetId, row1: 10, col1: 7, row2: 12, col2: 9);

        RowColumnSizingPlanner.PlanAutoFitRowHeights(sheet, wholeColumns, usedRange, (_, _) => new AutoFitCellText(""), defaultHeight: 20)
            .Should().BeEmpty();
        RowColumnSizingPlanner.PlanAutoFitColumnWidths(sheet, wholeRows, usedRange, (_, _) => new AutoFitCellText(""), defaultWidth: 8.43)
            .Should().BeEmpty();
    }

    private static GridRange Range(SheetId sheetId, uint row1, uint col1, uint row2, uint col2) =>
        new(new CellAddress(sheetId, row1, col1), new CellAddress(sheetId, row2, col2));

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
