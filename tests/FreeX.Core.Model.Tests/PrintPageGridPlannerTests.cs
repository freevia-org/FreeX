using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class PrintPageGridPlannerTests
{
    [Fact]
    public void BuildIndexes_OverThenDown_WalksColumnsWithinEachRowBand()
    {
        var pages = PrintPageGridPlanner.BuildIndexes(3, 2, WorksheetPageOrder.OverThenDown);

        pages.Select(page => (page.PageIndex, page.SheetPageNumber, page.RowPageIndex, page.ColumnPageIndex))
            .Should()
            .Equal(
                (0, 1, 0, 0),
                (1, 2, 0, 1),
                (2, 3, 1, 0),
                (3, 4, 1, 1),
                (4, 5, 2, 0),
                (5, 6, 2, 1));
    }

    [Fact]
    public void BuildIndexes_DownThenOver_WalksRowsWithinEachColumnBand()
    {
        var pages = PrintPageGridPlanner.BuildIndexes(3, 2, WorksheetPageOrder.DownThenOver);

        pages.Select(page => (page.PageIndex, page.SheetPageNumber, page.RowPageIndex, page.ColumnPageIndex))
            .Should()
            .Equal(
                (0, 1, 0, 0),
                (1, 2, 1, 0),
                (2, 3, 2, 0),
                (3, 4, 0, 1),
                (4, 5, 1, 1),
                (5, 6, 2, 1));
    }

    [Fact]
    public void BuildVisualIndexes_DownThenOver_WalksVisualGridAndAssignsPrintPageNumbers()
    {
        var pages = PrintPageGridPlanner.BuildVisualIndexes(3, 2, WorksheetPageOrder.DownThenOver);

        pages.Select(page => (page.PageIndex, page.SheetPageNumber, page.RowPageIndex, page.ColumnPageIndex))
            .Should()
            .Equal(
                (0, 1, 0, 0),
                (3, 4, 0, 1),
                (1, 2, 1, 0),
                (4, 5, 1, 1),
                (2, 3, 2, 0),
                (5, 6, 2, 1));
    }

    [Fact]
    public void BuildVisualIndexes_OverThenDown_MatchesVisualGridPageNumbers()
    {
        var pages = PrintPageGridPlanner.BuildVisualIndexes(3, 2, WorksheetPageOrder.OverThenDown);

        pages.Select(page => (page.PageIndex, page.SheetPageNumber, page.RowPageIndex, page.ColumnPageIndex))
            .Should()
            .Equal(
                (0, 1, 0, 0),
                (1, 2, 0, 1),
                (2, 3, 1, 0),
                (3, 4, 1, 1),
                (4, 5, 2, 0),
                (5, 6, 2, 1));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(-1, 2)]
    [InlineData(2, -1)]
    public void BuildIndexes_EmptyOrInvalidGrid_ReturnsNoPages(int rowPageCount, int columnPageCount)
    {
        var pages = PrintPageGridPlanner.BuildIndexes(
            rowPageCount,
            columnPageCount,
            WorksheetPageOrder.OverThenDown);

        pages.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(-1, 2)]
    [InlineData(2, -1)]
    public void BuildVisualIndexes_EmptyOrInvalidGrid_ReturnsNoPages(int rowPageCount, int columnPageCount)
    {
        var pages = PrintPageGridPlanner.BuildVisualIndexes(
            rowPageCount,
            columnPageCount,
            WorksheetPageOrder.OverThenDown);

        pages.Should().BeEmpty();
    }

    [Fact]
    public void Build_BindsIndexesToRowAndColumnPlans()
    {
        var rowPlans = new[]
        {
            new PrintPageRowPlan([], [1u, 2u]),
            new PrintPageRowPlan([], [3u, 4u])
        };
        var columnPlans = new[]
        {
            new PrintPageColumnPlan([], [1u]),
            new PrintPageColumnPlan([], [2u])
        };

        var pages = PrintPageGridPlanner.Build(rowPlans, columnPlans, WorksheetPageOrder.OverThenDown);

        pages.Should().HaveCount(4);
        pages[0].Should().Be(new PrintPageGridEntry(0, 1, 0, 0, rowPlans[0], columnPlans[0]));
        pages[1].Should().Be(new PrintPageGridEntry(1, 2, 0, 1, rowPlans[0], columnPlans[1]));
        pages[2].Should().Be(new PrintPageGridEntry(2, 3, 1, 0, rowPlans[1], columnPlans[0]));
        pages[3].Should().Be(new PrintPageGridEntry(3, 4, 1, 1, rowPlans[1], columnPlans[1]));
    }

    [Fact]
    public void Build_NullPlans_Throws()
    {
        var columnPlans = new[] { new PrintPageColumnPlan([], [1u]) };
        var rowPlans = new[] { new PrintPageRowPlan([], [1u]) };

        var missingRows = () => PrintPageGridPlanner.Build(null!, columnPlans, WorksheetPageOrder.OverThenDown);
        var missingColumns = () => PrintPageGridPlanner.Build(rowPlans, null!, WorksheetPageOrder.OverThenDown);

        missingRows.Should().Throw<ArgumentNullException>();
        missingColumns.Should().Throw<ArgumentNullException>();
    }
}
