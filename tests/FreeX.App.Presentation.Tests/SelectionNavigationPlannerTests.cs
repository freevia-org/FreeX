using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests;

public sealed class SelectionNavigationPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Fact]
    public void TryAdvanceWithinSelection_TabCyclesRowsInsideSingleArea()
    {
        var area = Range(1, 1, 2, 2);

        Plan([area], Cell(1, 1), isTab: true, forward: true).Target.Should().Be(Cell(1, 2));
        Plan([area], Cell(1, 2), isTab: true, forward: true).Target.Should().Be(Cell(2, 1));
        Plan([area], Cell(2, 2), isTab: true, forward: true).Target.Should().Be(Cell(1, 1));
        Plan([area], Cell(1, 1), isTab: true, forward: false).Target.Should().Be(Cell(2, 2));
    }

    [Fact]
    public void TryAdvanceWithinSelection_EnterCyclesColumnsInsideSingleArea()
    {
        var area = Range(1, 1, 2, 2);

        Plan([area], Cell(1, 1), isTab: false, forward: true).Target.Should().Be(Cell(2, 1));
        Plan([area], Cell(2, 1), isTab: false, forward: true).Target.Should().Be(Cell(1, 2));
        Plan([area], Cell(2, 2), isTab: false, forward: true).Target.Should().Be(Cell(1, 1));
        Plan([area], Cell(1, 1), isTab: false, forward: false).Target.Should().Be(Cell(2, 2));
    }

    [Fact]
    public void TryAdvanceWithinSelection_ForwardCrossesMultiAreaBoundaryInSelectionOrder()
    {
        var areas = new[] { Range(1, 1, 1, 2), Range(1, 4, 1, 4) };

        var fromLastArea = Plan(areas, Cell(1, 4), isTab: true, forward: true);
        fromLastArea.Target.Should().Be(Cell(1, 1));
        fromLastArea.SourceAreaIndex.Should().Be(1);
        fromLastArea.TargetAreaIndex.Should().Be(0);
        fromLastArea.CrossedAreaBoundary.Should().BeTrue();

        Plan(areas, Cell(1, 1), isTab: true, forward: true).Target.Should().Be(Cell(1, 2));
        Plan(areas, Cell(1, 2), isTab: true, forward: true).Target.Should().Be(Cell(1, 4));
    }

    [Fact]
    public void TryAdvanceWithinSelection_EnterCrossesVerticalMultiAreaBoundary()
    {
        var areas = new[] { Range(1, 1, 2, 1), Range(4, 1, 4, 1) };

        Plan(areas, Cell(4, 1), isTab: false, forward: true).Target.Should().Be(Cell(1, 1));
        Plan(areas, Cell(1, 1), isTab: false, forward: true).Target.Should().Be(Cell(2, 1));
        Plan(areas, Cell(2, 1), isTab: false, forward: true).Target.Should().Be(Cell(4, 1));
    }

    [Fact]
    public void TryAdvanceWithinSelection_BackwardCrossesToPreviousAreaEnd()
    {
        var areas = new[] { Range(1, 1, 1, 2), Range(1, 4, 2, 4) };

        var plan = Plan(areas, Cell(1, 1), isTab: true, forward: false);

        plan.Target.Should().Be(Cell(2, 4));
        plan.SourceAreaIndex.Should().Be(0);
        plan.TargetAreaIndex.Should().Be(1);
        plan.CrossedAreaBoundary.Should().BeTrue();
    }

    [Fact]
    public void TryAdvanceWithinSelection_CurrentOutsideAreasFallsBackToLastArea()
    {
        var areas = new[] { Range(1, 1, 1, 2), Range(3, 3, 3, 4) };

        var plan = Plan(areas, Cell(8, 8), isTab: true, forward: true);

        plan.SourceAreaIndex.Should().Be(1);
        plan.TargetAreaIndex.Should().Be(0);
        plan.Target.Should().Be(Cell(1, 1));
        plan.CrossedAreaBoundary.Should().BeTrue();
    }

    [Fact]
    public void TryAdvanceWithinSelection_RejectsSingleCellAndSingleMergedRegion()
    {
        SelectionNavigationPlanner.TryAdvanceWithinSelection(
                [Range(1, 1, 1, 1)], null, Cell(1, 1), isTab: true, forward: true, out _)
            .Should().BeFalse();

        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var merged = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 4));
        sheet.AddMergedRegion(merged);

        SelectionNavigationPlanner.TryAdvanceWithinSelection(
                [merged], sheet, merged.Start, isTab: true, forward: true, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void GetNextCorner_CyclesUniqueCornersClockwise()
    {
        var range = Range(2, 3, 5, 7);

        SelectionNavigationPlanner.GetNextCorner(range, Cell(2, 3)).Should().Be(Cell(2, 7));
        SelectionNavigationPlanner.GetNextCorner(range, Cell(2, 7)).Should().Be(Cell(5, 7));
        SelectionNavigationPlanner.GetNextCorner(range, Cell(5, 7)).Should().Be(Cell(5, 3));
        SelectionNavigationPlanner.GetNextCorner(range, Cell(5, 3)).Should().Be(Cell(2, 3));
        SelectionNavigationPlanner.GetNextCorner(range, Cell(3, 4)).Should().Be(range.Start);
    }

    [Fact]
    public void GetNextCorner_DeduplicatesSingleRowColumnAndCellCorners()
    {
        SelectionNavigationPlanner.GetNextCorner(Range(4, 2, 4, 6), Cell(4, 2)).Should().Be(Cell(4, 6));
        SelectionNavigationPlanner.GetNextCorner(Range(2, 4, 6, 4), Cell(2, 4)).Should().Be(Cell(6, 4));
        SelectionNavigationPlanner.GetNextCorner(Range(5, 5, 5, 5), Cell(5, 5)).Should().Be(Cell(5, 5));
    }

    private static SelectionNavigationPlan Plan(
        IReadOnlyList<GridRange> areas,
        CellAddress current,
        bool isTab,
        bool forward)
    {
        SelectionNavigationPlanner.TryAdvanceWithinSelection(
                areas, null, current, isTab, forward, out var plan)
            .Should().BeTrue();
        return plan;
    }

    private static CellAddress Cell(uint row, uint column) => new(SheetId, row, column);

    private static GridRange Range(uint startRow, uint startColumn, uint endRow, uint endColumn) =>
        new(Cell(startRow, startColumn), Cell(endRow, endColumn));
}
