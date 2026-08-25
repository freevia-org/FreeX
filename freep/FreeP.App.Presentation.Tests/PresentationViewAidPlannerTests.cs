using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationViewAidPlannerTests
{
    [Fact]
    public void Gridlines_and_guides_follow_the_live_slide_transform()
    {
        var transform = new SlideTransformCore(1, 10, 20, 32, 16);

        var plan = PresentationViewAidPlanner.Build(
            transform,
            new PresentationViewShowState(
                ShowGridlines: true,
                ShowGuides: true,
                ShowNotesPane: false,
                ShowRulers: false));

        plan.Gridlines.Should().Equal(
            new PresentationViewAidLine(18, 20, 18, 36),
            new PresentationViewAidLine(26, 20, 26, 36),
            new PresentationViewAidLine(34, 20, 34, 36),
            new PresentationViewAidLine(10, 28, 42, 28));
        plan.Guides.Should().Equal(
            new PresentationViewAidLine(26, 20, 26, 36),
            new PresentationViewAidLine(10, 28, 42, 28));
    }

    [Fact]
    public void Low_zoom_coarsens_the_visible_grid_without_changing_guide_geometry()
    {
        var transform = new SlideTransformCore(0.25, 10, 20, 256, 128);

        var plan = PresentationViewAidPlanner.Build(transform, PresentationViewShowState.Default);

        plan.Gridlines.Should().HaveCount(10);
        plan.Gridlines[0].Should().Be(new PresentationViewAidLine(18, 20, 18, 52));
        plan.Guides.Should().Equal(
            new PresentationViewAidLine(42, 20, 42, 52),
            new PresentationViewAidLine(10, 36, 74, 36));
    }

    [Fact]
    public void Hidden_aids_and_invalid_transforms_do_not_emit_geometry()
    {
        PresentationViewAidPlanner.Build(
                new SlideTransformCore(1, 0, 0, 100, 50),
                new PresentationViewShowState(false, false))
            .Should()
            .Be(PresentationViewAidPlan.Empty);
        PresentationViewAidPlanner.Build(
                new SlideTransformCore(0, 0, 0, 100, 50),
                PresentationViewShowState.Default)
            .Should()
            .Be(PresentationViewAidPlan.Empty);
    }
}
