using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

/// <summary>
/// Unit tests for the portable planner backing the Page Layout ▸ Breaks menu actions
/// (Insert / Remove / Reset). No running UI.
/// </summary>
public sealed class PageBreakActionPlannerTests
{
    private static CellAddress At(uint row, uint col) => new(default, row, col);

    [Fact]
    public void Insert_AddsRowAndColumnBreaksAtActiveCell()
    {
        var plan = PageBreakActionPlanner.Insert(At(5, 3), [], []);

        plan.RowBreaks.Should().Equal(5u);
        plan.ColumnBreaks.Should().Equal(3u);
        plan.Status.Should().Be("Inserted page breaks");
    }

    [Fact]
    public void Insert_SkipsTopLeftEdgesExcelCannotPlace()
    {
        var plan = PageBreakActionPlanner.Insert(At(1, 1), [], []);

        plan.RowBreaks.Should().BeEmpty();
        plan.ColumnBreaks.Should().BeEmpty();
        plan.Status.Should().Be("No page break to insert at the top-left corner");
    }

    [Fact]
    public void Insert_OnlyRowWhenColumnIsFirst()
    {
        var plan = PageBreakActionPlanner.Insert(At(4, 1), [], []);

        plan.RowBreaks.Should().Equal(4u);
        plan.ColumnBreaks.Should().BeEmpty();
        plan.Status.Should().Be("Inserted a page break above the row");
    }

    [Fact]
    public void Insert_DeduplicatesAndKeepsExistingBreaksSorted()
    {
        var plan = PageBreakActionPlanner.Insert(At(5, 3), [10u, 5u], [3u]);

        plan.RowBreaks.Should().Equal(5u, 10u);
        plan.ColumnBreaks.Should().Equal(3u);
    }

    [Fact]
    public void Plan_InsertHonorsWholeRowAndColumnSelectionAxes()
    {
        var rowPlan = PageBreakActionPlanner.Plan(
            PageBreakMenuAction.Insert,
            Range(5, 1, 5, CellAddress.MaxCol),
            [9u],
            [7u]);
        var columnPlan = PageBreakActionPlanner.Plan(
            PageBreakMenuAction.Insert,
            Range(1, 3, CellAddress.MaxRow, 3),
            [9u],
            [7u]);

        rowPlan.RowBreaks.Should().Equal(5u, 9u);
        rowPlan.ColumnBreaks.Should().Equal(7u);
        rowPlan.Status.Should().Be("Inserted a page break above the row");
        columnPlan.RowBreaks.Should().Equal(9u);
        columnPlan.ColumnBreaks.Should().Equal(3u, 7u);
        columnPlan.Status.Should().Be("Inserted a page break left of the column");
    }

    [Fact]
    public void Remove_ClearsBreaksAdjacentToActiveCell()
    {
        var plan = PageBreakActionPlanner.Remove(At(5, 3), [5u, 10u], [3u, 7u]);

        plan.RowBreaks.Should().Equal(10u);
        plan.ColumnBreaks.Should().Equal(7u);
        plan.Status.Should().Be("Removed page break");
    }

    [Fact]
    public void Remove_ReportsWhenNothingAdjacent()
    {
        var plan = PageBreakActionPlanner.Remove(At(5, 3), [10u], [7u]);

        plan.RowBreaks.Should().Equal(10u);
        plan.ColumnBreaks.Should().Equal(7u);
        plan.Status.Should().Be("No page break next to the selection");
    }

    [Fact]
    public void Plan_RemoveHonorsWholeRowAndColumnSelectionAxes()
    {
        var rowPlan = PageBreakActionPlanner.Plan(
            PageBreakMenuAction.Remove,
            Range(5, 1, 5, CellAddress.MaxCol),
            [5u, 9u],
            [3u, 7u]);
        var columnPlan = PageBreakActionPlanner.Plan(
            PageBreakMenuAction.Remove,
            Range(1, 3, CellAddress.MaxRow, 3),
            [5u, 9u],
            [3u, 7u]);

        rowPlan.RowBreaks.Should().Equal(9u);
        rowPlan.ColumnBreaks.Should().Equal(3u, 7u);
        rowPlan.Status.Should().Be("Removed page break");
        columnPlan.RowBreaks.Should().Equal(5u, 9u);
        columnPlan.ColumnBreaks.Should().Equal(7u);
        columnPlan.Status.Should().Be("Removed page break");
    }

    [Fact]
    public void ResetAll_ClearsEverything()
    {
        var plan = PageBreakActionPlanner.ResetAll();

        plan.RowBreaks.Should().BeEmpty();
        plan.ColumnBreaks.Should().BeEmpty();
        plan.Status.Should().Be("Reset all page breaks");
    }

    [Fact]
    public void Plan_ResetAllClearsBreaks()
    {
        var plan = PageBreakActionPlanner.Plan(
            PageBreakMenuAction.ResetAll,
            Range(5, 3, 5, 3),
            [5u, 9u],
            [3u, 7u]);

        plan.RowBreaks.Should().BeEmpty();
        plan.ColumnBreaks.Should().BeEmpty();
        plan.Status.Should().Be("Reset all page breaks");
    }

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheetId = default(SheetId);
        return new GridRange(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
    }
}
