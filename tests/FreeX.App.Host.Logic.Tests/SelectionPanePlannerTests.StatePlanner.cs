using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class SelectionPanePlannerTests
{
    [Fact]
    public void SelectionPaneDialogStatePlanner_FilterItems_AppliesSearchAndKindFilters()
    {
        var picture = DialogState(SelectionPaneObjectKind.Picture, "Logo", isVisible: true);
        var hiddenShape = DialogState(SelectionPaneObjectKind.Shape, "Process Box", isVisible: false);
        var textBox = DialogState(SelectionPaneObjectKind.TextBox, "Quarter Notes", isVisible: true);

        var visibleMatches = SelectionPaneDialogStatePlanner.FilterItems(
            [picture, hiddenShape, textBox],
            "  notes  ",
            "Visible");
        var shapeMatches = SelectionPaneDialogStatePlanner.FilterItems(
            [picture, hiddenShape, textBox],
            "shape",
            "All");

        visibleMatches.Should().Equal(textBox);
        shapeMatches.Should().Equal(hiddenShape);
    }

    [Fact]
    public void SelectionPaneDialogStatePlanner_FindMoveTargetIndex_UsesSupportedDrawingObjectStack()
    {
        var frontPicture = DialogState(SelectionPaneObjectKind.Picture, "Front", isVisible: true);
        var shape = DialogState(SelectionPaneObjectKind.Shape, "Shape", isVisible: true);
        var backPicture = DialogState(SelectionPaneObjectKind.Picture, "Back", isVisible: true);

        var forwardTarget = SelectionPaneDialogStatePlanner.FindMoveTargetIndex(
            [frontPicture, shape, backPicture],
            currentIndex: 2,
            forward: true);
        var backwardTarget = SelectionPaneDialogStatePlanner.FindMoveTargetIndex(
            [frontPicture, shape, backPicture],
            currentIndex: 0,
            forward: false);

        forwardTarget.Should().Be(1);
        backwardTarget.Should().Be(1);
    }

    [Fact]
    public void SelectionPaneDialogStatePlanner_PlanMove_ReordersAgainstMixedSupportedTarget()
    {
        var frontPicture = DialogState(SelectionPaneObjectKind.Picture, "Front", isVisible: true);
        var shape = DialogState(SelectionPaneObjectKind.Shape, "Shape", isVisible: true);
        var backPicture = DialogState(SelectionPaneObjectKind.Picture, "Back", isVisible: true);

        var plan = SelectionPaneDialogStatePlanner.PlanMove(
            [frontPicture, shape, backPicture],
            backPicture.Id,
            forward: true);

        plan.Should().NotBeNull();
        plan!.OrderedIds.Should().Equal(frontPicture.Id, backPicture.Id, shape.Id);
        plan.MoveChanges.Should().Equal(new SelectionPaneMoveChange(
            SelectionPaneObjectKind.Picture,
            backPicture.Id,
            Forward: true));
    }

    [Fact]
    public void SelectionPaneDialogStatePlanner_PlanDragReorder_ReordersAndPlansAdjacentMoves()
    {
        var front = DialogState(SelectionPaneObjectKind.Picture, "Front", isVisible: true);
        var middle = DialogState(SelectionPaneObjectKind.Picture, "Middle", isVisible: true);
        var back = DialogState(SelectionPaneObjectKind.Picture, "Back", isVisible: true);

        var plan = SelectionPaneDialogStatePlanner.PlanDragReorder(
            [front, middle, back],
            draggedId: back.Id,
            targetId: front.Id);

        plan.Should().NotBeNull();
        plan!.OrderedIds.Should().Equal(back.Id, front.Id, middle.Id);
        plan.MoveChanges.Should().Equal(
            new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, back.Id, Forward: true),
            new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, back.Id, Forward: true));
    }

    [Fact]
    public void SelectionPaneDialogStatePlanner_PlanDragReorder_CanInsertAfterTarget()
    {
        var front = DialogState(SelectionPaneObjectKind.Picture, "Front", isVisible: true);
        var middle = DialogState(SelectionPaneObjectKind.Picture, "Middle", isVisible: true);
        var back = DialogState(SelectionPaneObjectKind.Picture, "Back", isVisible: true);

        var plan = SelectionPaneDialogStatePlanner.PlanDragReorder(
            [front, middle, back],
            draggedId: front.Id,
            targetId: back.Id,
            placement: SelectionPaneDropPlacement.After);

        plan.Should().NotBeNull();
        plan!.OrderedIds.Should().Equal(middle.Id, back.Id, front.Id);
        plan.MoveChanges.Should().Equal(
            new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, front.Id, Forward: false),
            new SelectionPaneMoveChange(SelectionPaneObjectKind.Picture, front.Id, Forward: false));
    }

    [Fact]
    public void SelectionPaneDialogStatePlanner_PlanDropVisual_AllowsMixedSupportedInsertionCue()
    {
        var front = DialogState(SelectionPaneObjectKind.Picture, "Front", isVisible: true);
        var back = DialogState(SelectionPaneObjectKind.Shape, "Back", isVisible: true);

        var plan = SelectionPaneDialogStatePlanner.PlanDropVisual(
            [front, back],
            draggedId: back.Id,
            targetId: front.Id,
            placement: SelectionPaneDropPlacement.Before);

        plan.Should().Be(new SelectionPaneDropVisualPlan(
            front.Id,
            SelectionPaneDropPlacement.Before,
            IsAllowed: true));
    }

    [Fact]
    public void SelectionPaneDialogStatePlanner_PlanDropVisual_RejectsSameItemAndUnsupportedChartCues()
    {
        var picture = DialogState(SelectionPaneObjectKind.Picture, "Picture", isVisible: true);
        var chart = DialogState(SelectionPaneObjectKind.Chart, "Chart", isVisible: true);

        var sameItem = SelectionPaneDialogStatePlanner.PlanDropVisual(
            [picture, chart],
            draggedId: picture.Id,
            targetId: picture.Id,
            placement: SelectionPaneDropPlacement.After);
        var chartCue = SelectionPaneDialogStatePlanner.PlanDropVisual(
            [picture, chart],
            draggedId: picture.Id,
            targetId: chart.Id,
            placement: SelectionPaneDropPlacement.After);

        sameItem.Should().Be(new SelectionPaneDropVisualPlan(
            picture.Id,
            SelectionPaneDropPlacement.After,
            IsAllowed: false));
        chartCue.Should().Be(new SelectionPaneDropVisualPlan(
            chart.Id,
            SelectionPaneDropPlacement.After,
            IsAllowed: false));
    }

    [Fact]
    public void SelectionPaneDialogStatePlanner_PlanDropVisual_RejectsNoOpAdjacentCue()
    {
        var front = DialogState(SelectionPaneObjectKind.Picture, "Front", isVisible: true);
        var back = DialogState(SelectionPaneObjectKind.Picture, "Back", isVisible: true);

        var plan = SelectionPaneDialogStatePlanner.PlanDropVisual(
            [front, back],
            draggedId: front.Id,
            targetId: back.Id,
            placement: SelectionPaneDropPlacement.Before);

        plan.Should().Be(new SelectionPaneDropVisualPlan(
            back.Id,
            SelectionPaneDropPlacement.Before,
            IsAllowed: false));
    }

    [Fact]
    public void SelectionPaneDialogStatePlanner_PlanDragReorder_HandlesLargeListsWithConsolidatedLookup()
    {
        const int itemCount = 5_000;
        var items = Enumerable.Range(0, itemCount)
            .Select(index => DialogState(SelectionPaneObjectKind.Picture, $"Picture {index}", isVisible: true))
            .ToArray();
        var dragged = items[^1];
        var target = items[0];

        var plan = SelectionPaneDialogStatePlanner.PlanDragReorder(
            items,
            draggedId: dragged.Id,
            targetId: target.Id);

        plan.Should().NotBeNull();
        plan!.OrderedIds[0].Should().Be(dragged.Id);
        plan.OrderedIds[1].Should().Be(target.Id);
        plan.OrderedIds.Should().HaveCount(itemCount);
        plan.MoveChanges.Should().HaveCount(itemCount - 1);
        plan.MoveChanges.Should().OnlyContain(move =>
            move.Kind == SelectionPaneObjectKind.Picture &&
            move.Id == dragged.Id &&
            move.Forward);
    }

    [Fact]
    public void SelectionPaneDialog_FilterItems_ReturnsOriginalListForDefaultView()
    {
        var items = new[]
        {
            DialogState(SelectionPaneObjectKind.Picture, "Logo", isVisible: true),
            DialogState(SelectionPaneObjectKind.Shape, "Process Box", isVisible: false)
        };

        var filtered = SelectionPaneDialogStatePlanner.FilterItems(items, " ", "");

        filtered.Should().BeSameAs(items);
    }
}
