using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageBreakSelectionPlannerTests
{
    [Fact]
    public void Insert_AddsBothAxesForCellSelection()
    {
        var selection = Range(5, 3, 5, 3);

        var plan = PageBreakSelectionPlanner.Insert(selection, [9u], [7u]);

        plan.RowBreaks.Should().Equal(5u, 9u);
        plan.ColumnBreaks.Should().Equal(3u, 7u);
    }

    [Fact]
    public void Insert_AddsOnlyRowAxisForWholeRowSelection()
    {
        var selection = Range(5, 1, 5, CellAddress.MaxCol);

        var plan = PageBreakSelectionPlanner.Insert(selection, [9u], [7u]);

        plan.RowBreaks.Should().Equal(5u, 9u);
        plan.ColumnBreaks.Should().Equal(7u);
    }

    [Fact]
    public void Insert_AddsOnlyColumnAxisForWholeColumnSelection()
    {
        var selection = Range(1, 3, CellAddress.MaxRow, 3);

        var plan = PageBreakSelectionPlanner.Insert(selection, [9u], [7u]);

        plan.RowBreaks.Should().Equal(9u);
        plan.ColumnBreaks.Should().Equal(3u, 7u);
    }

    [Fact]
    public void Insert_AddsBothAxesForSelectAllSelection()
    {
        var selection = Range(1, 1, CellAddress.MaxRow, CellAddress.MaxCol);

        var plan = PageBreakSelectionPlanner.Insert(selection, [], []);

        plan.RowBreaks.Should().BeEmpty();
        plan.ColumnBreaks.Should().BeEmpty();
    }

    [Fact]
    public void Insert_DoesNotInventBreaksForTopLeftAnchoredUsedRangeSelection()
    {
        var selection = Range(1, 1, 10, 4);

        var plan = PageBreakSelectionPlanner.Insert(selection, [], []);

        plan.RowBreaks.Should().BeEmpty();
        plan.ColumnBreaks.Should().BeEmpty();
    }

    [Fact]
    public void Remove_RespectsSelectionAxisAndPreservesUnrelatedBreaks()
    {
        var wholeRow = Range(5, 1, 5, CellAddress.MaxCol);
        var wholeColumn = Range(1, 3, CellAddress.MaxRow, 3);

        var rowPlan = PageBreakSelectionPlanner.Remove(wholeRow, [5u, 9u], [3u, 7u]);
        var columnPlan = PageBreakSelectionPlanner.Remove(wholeColumn, [5u, 9u], [3u, 7u]);

        rowPlan.RowBreaks.Should().Equal(9u);
        rowPlan.ColumnBreaks.Should().Equal(3u, 7u);
        columnPlan.RowBreaks.Should().Equal(5u, 9u);
        columnPlan.ColumnBreaks.Should().Equal(7u);
    }

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        return new GridRange(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));
    }
}
