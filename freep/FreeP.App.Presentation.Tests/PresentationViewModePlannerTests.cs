using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationViewModePlannerTests
{
    [Fact]
    public void Default_mode_exposes_the_implemented_workspace_views()
    {
        PresentationViewModePlanner.BuildPlans(PresentationViewModeState.Normal)
            .Select(plan => (plan.CommandId, plan.IsChecked))
            .Should()
            .Equal(
                (PresentationViewModePlanner.NormalCommandId, true),
                (PresentationViewModePlanner.SlideSorterCommandId, false),
                (PresentationViewModePlanner.NotesPageCommandId, false));
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
    public void Notes_page_is_a_selectable_exclusive_view_mode()
    {
        var notesPage = PresentationViewModePlanner.BuildPlan(
            PresentationViewMode.NotesPage,
            PresentationViewModeState.Normal);

        var state = PresentationViewModePlanner.Select(PresentationViewModeState.Normal, notesPage);

        state.Mode.Should().Be(PresentationViewMode.NotesPage);
        PresentationViewModePlanner.BuildPlan(PresentationViewMode.Normal, state).IsChecked.Should().BeFalse();
        PresentationViewModePlanner.BuildPlan(PresentationViewMode.SlideSorter, state).IsChecked.Should().BeFalse();
        PresentationViewModePlanner.BuildPlan(PresentationViewMode.NotesPage, state).IsChecked.Should().BeTrue();
    }
}
