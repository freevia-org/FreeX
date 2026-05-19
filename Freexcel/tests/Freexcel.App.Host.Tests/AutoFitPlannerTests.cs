using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class AutoFitPlannerTests
{
    [Fact]
    public void PlanRowHeights_MeasuresEachSelectedRowIndependently()
    {
        var sheetId = SheetId.New();
        var selection = Range(sheetId, row1: 2, col1: 3, row2: 3, col2: 4);

        var plan = AutoFitPlanner.PlanRowHeights(
            selection,
            usedRange: selection,
            (row, col) => (row, col) switch
            {
                (2, 3) => "short",
                (3, 4) => "first\nsecond\nthird",
                _ => null
            },
            defaultHeight: 20);

        plan.Should().Equal(
            new AutoFitSizePlan(2, AutoFitSizingService.EstimateRowHeight(["short"], 20)),
            new AutoFitSizePlan(3, AutoFitSizingService.EstimateRowHeight(["first\nsecond\nthird"], 20)));
    }

    [Fact]
    public void PlanColumnWidths_MeasuresEachSelectedColumnIndependently()
    {
        var sheetId = SheetId.New();
        var selection = Range(sheetId, row1: 2, col1: 3, row2: 3, col2: 4);

        var plan = AutoFitPlanner.PlanColumnWidths(
            selection,
            usedRange: selection,
            (row, col) => (row, col) switch
            {
                (2, 3) => "short",
                (3, 4) => "a much longer display value",
                _ => null
            },
            defaultWidth: 8.43);

        plan.Should().Equal(
            new AutoFitSizePlan(3, AutoFitSizingService.EstimateColumnWidth(["short"], 8.43)),
            new AutoFitSizePlan(4, AutoFitSizingService.EstimateColumnWidth(["a much longer display value"], 8.43)));
    }

    [Fact]
    public void PlanColumnWidths_ForWholeColumnSelection_BoundsMeasurementsToUsedRows()
    {
        var sheetId = SheetId.New();
        var wholeColumns = Range(sheetId, row1: 1, col1: 2, row2: CellAddress.MaxRow, col2: 3);
        var usedRange = Range(sheetId, row1: 10, col1: 1, row2: 12, col2: 5);
        var visited = new List<(uint Row, uint Col)>();

        AutoFitPlanner.PlanColumnWidths(
            wholeColumns,
            usedRange,
            (row, col) =>
            {
                visited.Add((row, col));
                return row == 11 && col == 3 ? "wide text" : null;
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
        var sheetId = SheetId.New();
        var wholeRows = Range(sheetId, row1: 4, col1: 1, row2: 5, col2: CellAddress.MaxCol);
        var usedRange = Range(sheetId, row1: 1, col1: 7, row2: 20, col2: 9);
        var visited = new List<(uint Row, uint Col)>();

        AutoFitPlanner.PlanRowHeights(
            wholeRows,
            usedRange,
            (row, col) =>
            {
                visited.Add((row, col));
                return row == 5 && col == 8 ? "first\nsecond" : null;
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
        var sheetId = SheetId.New();
        var wholeColumns = Range(sheetId, row1: 1, col1: 2, row2: CellAddress.MaxRow, col2: 3);
        var wholeRows = Range(sheetId, row1: 4, col1: 1, row2: 5, col2: CellAddress.MaxCol);

        AutoFitPlanner.PlanColumnWidths(wholeColumns, usedRange: null, (_, _) => "", defaultWidth: 8.43)
            .Should().BeEmpty();
        AutoFitPlanner.PlanRowHeights(wholeRows, usedRange: null, (_, _) => "", defaultHeight: 20)
            .Should().BeEmpty();
    }

    [Fact]
    public void PlanAutoFit_ForOppositeWholeAxisSelection_NoOps()
    {
        var sheetId = SheetId.New();
        var wholeColumns = Range(sheetId, row1: 1, col1: 2, row2: CellAddress.MaxRow, col2: 3);
        var wholeRows = Range(sheetId, row1: 4, col1: 1, row2: 5, col2: CellAddress.MaxCol);
        var usedRange = Range(sheetId, row1: 10, col1: 7, row2: 12, col2: 9);

        AutoFitPlanner.PlanRowHeights(wholeColumns, usedRange, (_, _) => "", defaultHeight: 20)
            .Should().BeEmpty();
        AutoFitPlanner.PlanColumnWidths(wholeRows, usedRange, (_, _) => "", defaultWidth: 8.43)
            .Should().BeEmpty();
    }

    private static GridRange Range(SheetId sheetId, uint row1, uint col1, uint row2, uint col2) =>
        new(new CellAddress(sheetId, row1, col1), new CellAddress(sheetId, row2, col2));
}
