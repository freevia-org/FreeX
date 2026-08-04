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
    public void Cover_image_is_a_single_undoable_native_relationship()
    {
        var presentation = BuildPresentation();
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = session.InsertSlideZoom("slide-2");
        var image = new byte[] { 1, 2, 3, 4 };

        session.SetZoomCoverImage(shape.Id, image, "image/png").Should().BeTrue();
        shape.PreservedObject!.ZoomProperties!.ImageType.Should().Be("cover");
        shape.PreservedObject.RawXml.Should().Contain("imageType=\"cover\"");
        shape.PreservedObject.RawXml.Should().Contain("blipFill");
        shape.PreservedObject.Parts.Values.Should().ContainSingle().Which.Should().BeEquivalentTo(image);
        shape.PreservedObject.SlideRels.Values.Should().ContainSingle(rel =>
            rel.RelType.EndsWith("/image", StringComparison.OrdinalIgnoreCase));

        session.Undo();
        shape.PreservedObject.ZoomProperties!.ImageType.Should().Be("preview");
        shape.PreservedObject.RawXml.Should().NotContain("embed=");
        shape.PreservedObject.Parts.Should().BeEmpty();

        session.Redo();
        shape.PreservedObject.ZoomProperties!.ImageType.Should().Be("cover");
        shape.PreservedObject.Parts.Values.Should().ContainSingle().Which.Should().BeEquivalentTo(image);
    }

    [Fact]
    public void Existing_slide_zoom_can_be_retargeted_and_undone()
    {
        var presentation = BuildPresentation();
        presentation.Slides.Add(new Slide { Id = "slide-3", NumericId = 258, Title = "Slide 3" });
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = session.InsertSlideZoom("slide-2");

        session.SetSlideZoomTarget(shape.Id, "slide-3").Should().BeTrue();
        shape.PreservedObject!.ZoomTargetSlideNumericId.Should().Be(258);
        shape.PreservedObject.RawXml.Should().Contain("sldId=\"258\"");
        shape.AlternativeText.Should().Contain("Slide 3");

        session.Undo();
        shape.PreservedObject.ZoomTargetSlideNumericId.Should().Be(257);
        shape.PreservedObject.RawXml.Should().Contain("sldId=\"257\"");
        session.Redo();
        shape.PreservedObject.ZoomTargetSlideNumericId.Should().Be(258);
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

    [Fact]
    public void Predicts_writer_slide_id_for_unsaved_target()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });

        SlideZoomInsertionPlanner.TryBuildPlan(
            presentation,
            currentSlideIndex: 0,
            targetSlideId: "slide-2",
            out var plan).Should().BeTrue();

        plan.TargetSlideNumericId.Should().Be(257);
    }

    private static Presentation BuildPresentation()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", NumericId = 256 });
        presentation.Slides.Add(new Slide { Id = "slide-2", NumericId = 257, Title = "Target" });
        return presentation;
    }
}
