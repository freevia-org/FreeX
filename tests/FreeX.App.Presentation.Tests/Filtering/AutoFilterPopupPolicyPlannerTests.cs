using FluentAssertions;
using Free.Shared.Drawing;
using FreeX.App.Presentation.Filtering;

namespace FreeX.App.Presentation.Tests.Filtering;

public sealed class AutoFilterPopupPolicyPlannerTests
{
    [Fact]
    public void PlanChecklistState_NormalizesSearchSelectionAndEnablement()
    {
        AutoFilterDialogItem[] items =
        [
            new("Alpha", "A", true),
            new("Alpine", "B", false),
            new("Beta", "C", true)
        ];

        var state = AutoFilterMenuPlanner.PlanChecklistState(items, "  al  ");

        state.VisibleItems.Select(item => item.Value).Should().Equal("A", "B");
        state.IsChecklistEnabled.Should().BeTrue();
        state.SelectAllState.Should().BeNull();
        state.IsAddCurrentSelectionVisible.Should().BeTrue();
        state.IsAddCurrentSelectionEnabled.Should().BeTrue();
        state.ShouldClearAddCurrentSelection.Should().BeFalse();
    }

    [Fact]
    public void PlanChecklistState_NoMatchesDisablesChecklistCommands()
    {
        var state = AutoFilterMenuPlanner.PlanChecklistState(
            [new AutoFilterDialogItem("Alpha", "A", true)],
            "missing");

        state.VisibleItems.Should().BeEmpty();
        state.IsChecklistEnabled.Should().BeFalse();
        state.SelectAllState.Should().BeFalse();
        state.IsAddCurrentSelectionVisible.Should().BeTrue();
        state.IsAddCurrentSelectionEnabled.Should().BeFalse();
    }

    [Fact]
    public void PlanChecklistState_EmptySearchClearsAdditiveMode()
    {
        var state = AutoFilterMenuPlanner.PlanChecklistState(
            [new AutoFilterDialogItem("Alpha", "A", true)],
            "   ");

        state.IsAddCurrentSelectionVisible.Should().BeFalse();
        state.IsAddCurrentSelectionEnabled.Should().BeFalse();
        state.ShouldClearAddCurrentSelection.Should().BeTrue();
    }

    [Fact]
    public void Placement_FromPointerAddsNativeHeaderAffordanceOffset()
    {
        var placement = AutoFilterPopupPlacementPlanner.FromPointer(new LayoutPoint(120, 45));

        placement.Anchor.Should().Be(new LayoutPoint(120, 63));
        placement.Edge.Should().Be(AutoFilterPopupPlacementEdge.BottomStart);
    }

    [Fact]
    public void Placement_FromHeaderBoundsUsesBottomStartAnchor()
    {
        var placement = AutoFilterPopupPlacementPlanner.FromHeaderBounds(
            new LayoutRect(10, 20, 80, 24));

        placement.Anchor.Should().Be(new LayoutPoint(10, 44));
        placement.Edge.Should().Be(AutoFilterPopupPlacementEdge.BottomStart);
    }
}
