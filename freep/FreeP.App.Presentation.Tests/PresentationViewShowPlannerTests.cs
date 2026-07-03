using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationViewShowPlannerTests
{
    [Fact]
    public void Default_state_keeps_existing_snap_intents_enabled()
    {
        var state = PresentationViewShowState.Default;

        state.ShowGridlines.Should().BeTrue();
        state.ShowGuides.Should().BeTrue();
        PresentationViewShowPlanner.BuildPlans(state)
            .Select(plan => plan.CommandId)
            .Should()
            .Equal(
                PresentationViewShowPlanner.GridlinesCommandId,
                PresentationViewShowPlanner.GuidesCommandId);
    }

    [Fact]
    public void Gridlines_toggle_flips_only_grid_visibility_and_snap_intent()
    {
        var state = new PresentationViewShowState(
            ShowGridlines: true,
            ShowGuides: true);

        PresentationViewShowPlanner.TryToggle(
                state,
                PresentationViewShowPlanner.GridlinesCommandId,
                out var result)
            .Should()
            .BeTrue();

        result.State.ShowGridlines.Should().BeFalse();
        result.State.ShowGuides.Should().BeTrue();
        result.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void Guides_toggle_flips_only_guides_visibility_and_shape_snap_intent()
    {
        var state = new PresentationViewShowState(
            ShowGridlines: true,
            ShowGuides: false);

        PresentationViewShowPlanner.TryToggle(
                state,
                PresentationViewShowPlanner.GuidesCommandId,
                out var result)
            .Should()
            .BeTrue();

        result.State.ShowGridlines.Should().BeTrue();
        result.State.ShowGuides.Should().BeTrue();
        result.IsChecked.Should().BeTrue();
    }

    [Fact]
    public void Unknown_command_id_does_not_mutate_state()
    {
        PresentationViewShowPlanner.TryToggle(
                PresentationViewShowState.Default,
                "freep.view.zoom",
                out _)
            .Should()
            .BeFalse();
    }
}
