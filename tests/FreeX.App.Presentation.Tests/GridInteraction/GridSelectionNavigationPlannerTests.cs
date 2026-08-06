using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.GridInteraction;

public sealed class GridSelectionNavigationPlannerTests
{
    [Fact]
    public void PlanCycle_TabAndEnter_AdvanceInTheirNativeTraversalOrder()
    {
        var sheet = CreateSheet();
        var range = Range(sheet, 2, 2, 3, 3);

        GridSelectionNavigationPlanner.PlanCycle(
                sheet,
                range,
                null,
                range.Start,
                GridSelectionCycleKey.Tab,
                forward: true)
            .Should().Be(new GridSelectionCyclePlan(
                new CellAddress(sheet.Id, 2, 3), 0, 0, false, false, false));

        GridSelectionNavigationPlanner.PlanCycle(
                sheet,
                range,
                null,
                range.Start,
                GridSelectionCycleKey.Enter,
                forward: true)
            .Should().Be(new GridSelectionCyclePlan(
                new CellAddress(sheet.Id, 3, 2), 0, 0, false, false, false));
    }

    [Fact]
    public void PlanCycle_MultiAreaForwardAndBackward_MoveInClickOrderAndReportWrap()
    {
        var sheet = CreateSheet();
        var first = Range(sheet, 1, 1, 1, 2);
        var second = Range(sheet, 1, 4, 1, 4);
        GridRange[] areas = [first, second];

        var forwardWrap = GridSelectionNavigationPlanner.PlanCycle(
            sheet,
            second,
            areas,
            second.End,
            GridSelectionCycleKey.Tab,
            forward: true);
        forwardWrap.Should().Be(new GridSelectionCyclePlan(first.Start, 1, 0, true, true, true));

        var forwardNext = GridSelectionNavigationPlanner.PlanCycle(
            sheet,
            first,
            areas,
            first.End,
            GridSelectionCycleKey.Tab,
            forward: true);
        forwardNext.Should().Be(new GridSelectionCyclePlan(second.Start, 0, 1, true, true, false));

        var backwardWrap = GridSelectionNavigationPlanner.PlanCycle(
            sheet,
            first,
            areas,
            first.Start,
            GridSelectionCycleKey.Enter,
            forward: false);
        backwardWrap.Should().Be(new GridSelectionCyclePlan(second.End, 0, 1, true, true, true));
    }

    [Fact]
    public void PlanCycle_SingleMergedCellIsExcluded_ButMergedAreasCanParticipateInMultiAreaCycle()
    {
        var sheet = CreateSheet();
        var merged = Range(sheet, 2, 2, 3, 3);
        sheet.AddMergedRegion(merged);

        GridSelectionNavigationPlanner.PlanCycle(
                sheet,
                merged,
                null,
                merged.Start,
                GridSelectionCycleKey.Tab,
                forward: true)
            .Should().BeNull();

        var other = Range(sheet, 5, 5, 5, 5);
        GridSelectionNavigationPlanner.PlanCycle(
                sheet,
                other,
                [merged, other],
                other.Start,
                GridSelectionCycleKey.Tab,
                forward: true)
            .Should().Be(new GridSelectionCyclePlan(merged.Start, 1, 0, true, true, true));
    }

    [Fact]
    public void PlanCycle_OutOfSyncActiveCellFallsBackToLastArea()
    {
        var sheet = CreateSheet();
        var first = Range(sheet, 1, 1, 1, 2);
        var second = Range(sheet, 4, 4, 4, 5);

        GridSelectionNavigationPlanner.PlanCycle(
                sheet,
                second,
                [first, second],
                new CellAddress(sheet.Id, 9, 9),
                GridSelectionCycleKey.Tab,
                forward: true)
            .Should().Be(new GridSelectionCyclePlan(first.Start, 1, 0, true, true, true));
    }

    [Fact]
    public void WholeRangeFactories_NormalizeAnchorsAndUseWorksheetBounds()
    {
        var sheet = CreateSheet();

        GridSelectionNavigationPlanner.CreateWholeRowsRange(sheet.Id, 7, 3)
            .Should().Be(Range(sheet, 3, 1, 7, CellAddress.MaxCol));
        GridSelectionNavigationPlanner.CreateWholeColumnsRange(sheet.Id, 7, 3)
            .Should().Be(Range(sheet, 1, 3, CellAddress.MaxRow, 7));
        GridSelectionNavigationPlanner.CreateWholeGridRange(sheet.Id)
            .Should().Be(Range(sheet, 1, 1, CellAddress.MaxRow, CellAddress.MaxCol));
    }

    [Fact]
    public void UpdateDisjointSelectionAreas_SeedsAppendsAndReplacesActiveDragArea()
    {
        var sheet = CreateSheet();
        var original = Range(sheet, 1, 1, 1, 1);
        var added = Range(sheet, 3, 3, 3, 3);
        var extended = Range(sheet, 3, 3, 5, 5);

        var areas = GridSelectionNavigationPlanner.AppendDisjointSelectionArea(null, original, added);
        areas.Should().Equal(original, added);

        var updated = GridSelectionNavigationPlanner.UpdateDisjointSelectionAreas(
            areas,
            added,
            extended,
            startNewArea: false);
        updated.Should().BeSameAs(areas);
        updated.Should().Equal(original, extended);
    }

    [Fact]
    public void FormatDragDimensionText_ProjectsRowsAndColumns()
    {
        var sheet = CreateSheet();

        GridSelectionNavigationPlanner.FormatDragDimensionText(Range(sheet, 2, 2, 5, 4))
            .Should().Be("4R x 3C");
    }

    private static Sheet CreateSheet() => new(SheetId.New(), "Sheet1");

    private static GridRange Range(
        Sheet sheet,
        uint startRow,
        uint startCol,
        uint endRow,
        uint endCol) =>
        new(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));
}
