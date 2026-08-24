using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationViewModePlannerTests
{
    [Fact]
    public void Default_mode_exposes_only_the_implemented_normal_and_sorter_views()
    {
        PresentationViewModePlanner.BuildPlans(PresentationViewModeState.Normal)
            .Select(plan => (plan.CommandId, plan.IsChecked))
            .Should()
            .Equal(
                (PresentationViewModePlanner.NormalCommandId, true),
                (PresentationViewModePlanner.SlideSorterCommandId, false));
    }

    [Fact]
    public void Selecting_slide_sorter_makes_it_the_exclusive_checked_view()
    {
        var sorter = PresentationViewModePlanner.BuildPlan(
            PresentationViewMode.SlideSorter,
            PresentationViewModeState.Normal);

        var state = PresentationViewModePlanner.Select(PresentationViewModeState.Normal, sorter);

        state.Mode.Should().Be(PresentationViewMode.SlideSorter);
        PresentationViewModePlanner.BuildPlan(PresentationViewMode.Normal, state).IsChecked.Should().BeFalse();
        PresentationViewModePlanner.BuildPlan(PresentationViewMode.SlideSorter, state).IsChecked.Should().BeTrue();
    }

    [Fact]
    public void Unknown_command_is_not_treated_as_a_view_mode()
    {
        PresentationViewModePlanner.TryBuildPlan(
                "freep.view.notes-page",
                PresentationViewModeState.Normal,
                out _)
            .Should()
            .BeFalse();
    }
}
