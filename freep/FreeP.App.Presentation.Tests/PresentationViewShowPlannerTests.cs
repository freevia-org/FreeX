using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationViewShowPlannerTests
{
    [Fact]
    public void Default_state_hides_optional_view_aids()
    {
        var state = PresentationViewShowState.Default;

        state.ShowRulers.Should().BeFalse();
        state.ShowGridlines.Should().BeFalse();
        state.ShowGuides.Should().BeFalse();
        state.ShowNotesPane.Should().BeTrue();
        PresentationViewShowPlanner.BuildPlans(state)
            .Select(plan => plan.CommandId)
            .Should()
            .Equal(
                PresentationViewShowPlanner.RulerCommandId,
                PresentationViewShowPlanner.GridlinesCommandId,
                PresentationViewShowPlanner.GuidesCommandId,
                PresentationViewShowPlanner.NotesCommandId);
    }

    [Fact]
    public void Gridlines_toggle_flips_only_grid_visibility()
    {
        var state = new PresentationViewShowState(
            ShowRulers: true,
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
    public void Guides_toggle_flips_only_guides_visibility()
    {
        var state = new PresentationViewShowState(
            ShowRulers: true,
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
    public void Notes_toggle_flips_only_notes_pane_visibility()
    {
        PresentationViewShowPlanner.TryToggle(
                PresentationViewShowState.Default,
                PresentationViewShowPlanner.NotesCommandId,
                out var result)
            .Should()
            .BeTrue();

        result.State.ShowGridlines.Should().BeFalse();
        result.State.ShowGuides.Should().BeFalse();
        result.State.ShowNotesPane.Should().BeFalse();
        result.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void Ruler_toggle_flips_only_ruler_chrome_visibility()
    {
        PresentationViewShowPlanner.TryToggle(
                PresentationViewShowState.Default,
                PresentationViewShowPlanner.RulerCommandId,
                out var result)
            .Should()
            .BeTrue();

        result.State.ShowRulers.Should().BeTrue();
        result.State.ShowGridlines.Should().BeFalse();
        result.State.ShowGuides.Should().BeFalse();
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
