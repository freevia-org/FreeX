using FluentAssertions;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationMainWindowPaneAccessibilityPlanTests
{
    [Fact]
    public void Build_ProjectsEveryMainWindowPaneInStableAccessibilityOrder()
    {
        var plan = PresentationMainWindowPaneAccessibilityPlan.Build(new(
            SlideCount: 5,
            SelectedSlideIndex: 2,
            Comments: new(true, 3, 1),
            Accessibility: new(true, 4, 2),
            AltText: new(false, 3),
            ReadingOrder: new(true, 6, 4),
            Proofing: new(false, 2, 0),
            MediaCaptions: new(true, 2, 1),
            SmartArtText: new(true, 7, 3),
            Selection: new(true, 8, 5),
            Animation: new(false, 9, 6)));

        plan.Select(entry => entry.PaneId).Should().Equal(
            PresentationPaneAccessibilityPlanner.Descriptors.Select(descriptor => descriptor.PaneId));
        plan[0].Should().Be(new PresentationPaneAccessibilityState(
            PresentationPaneAccessibilityPlanner.SlidePaneId, true, 5, 2));
        plan[1].Should().Be(new PresentationPaneAccessibilityState(
            PresentationPaneAccessibilityPlanner.NotesPaneId, true, 1));
        plan[3].Should().Be(new PresentationPaneAccessibilityState(
            PresentationPaneAccessibilityPlanner.AccessibilityPaneId, true, 4, 2));
        plan[^1].Should().Be(new PresentationPaneAccessibilityState(
            PresentationPaneAccessibilityPlanner.AnimationPaneId, false, 9, 6));
    }
}
