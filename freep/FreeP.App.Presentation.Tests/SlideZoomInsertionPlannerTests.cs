using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideZoomInsertionPlannerTests
{
    [Fact]
    public void Builds_native_slide_zoom_for_a_different_slide()
    {
        var presentation = BuildPresentation();

        var options = SlideZoomInsertionPlanner.BuildTargetOptions(presentation.Slides, 0);
        options.Should().ContainSingle(option => option.Id == "slide-2");

        SlideZoomInsertionPlanner.TryBuildPlan(
            presentation,
            currentSlideIndex: 0,
            targetSlideId: "slide-2",
            out var plan).Should().BeTrue();

        plan.TargetSlideNumericId.Should().Be(257);
        plan.TargetDisplayName.Should().Contain("Target");
    }

    [Fact]
    public void Editing_session_inserts_native_zoom_and_undoes_it()
    {
        var presentation = BuildPresentation();
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        var shape = session.InsertSlideZoom("slide-2");

        shape.Kind.Should().Be(SlideShapeKind.Zoom);
        shape.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Zoom);
        shape.PreservedObject.ZoomTargetSlideNumericId.Should().Be(257);
        shape.PreservedObject.RawXml.Should().Contain("slidezoom");
        presentation.Slides[0].Shapes.Should().Contain(shape);

        session.Undo();
        presentation.Slides[0].Shapes.Should().NotContain(shape);
        session.Redo();
        presentation.Slides[0].Shapes.Should().ContainSingle(item => item.Kind == SlideShapeKind.Zoom);
    }

    [Fact]
    public void Rejects_current_slide_as_zoom_target()
    {
        var presentation = BuildPresentation();

        SlideZoomInsertionPlanner.TryBuildPlan(
            presentation,
            currentSlideIndex: 0,
            targetSlideId: "slide-1",
            out _).Should().BeFalse();
    }

    private static Presentation BuildPresentation()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", NumericId = 256 });
        presentation.Slides.Add(new Slide { Id = "slide-2", NumericId = 257, Title = "Target" });
        return presentation;
    }
}
