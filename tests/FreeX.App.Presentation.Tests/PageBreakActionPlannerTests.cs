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
    public void ResetAll_ClearsEverything()
    {
        var plan = PageBreakActionPlanner.ResetAll();

        plan.RowBreaks.Should().BeEmpty();
        plan.ColumnBreaks.Should().BeEmpty();
        plan.Status.Should().Be("Reset all page breaks");
    }
}
