using System.IO;
using System.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;
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
    public void Retargeting_clears_stale_auto_preview_and_undo_restores_it()
    {
        var presentation = BuildPresentation();
        presentation.Slides.Add(new Slide { Id = "slide-3", NumericId = 258, Title = "Slide 3" });
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = session.InsertSlideZoom("slide-2");
        var oldPreview = new byte[] { 7, 8, 9 };

        session.ResetZoomCoverImage(shape.Id, oldPreview, "image/png").Should().BeTrue();
        shape.Picture!.Bytes.Should().BeEquivalentTo(oldPreview);
        shape.PreservedObject!.Parts.Should().ContainSingle();

        session.SetSlideZoomTarget(shape.Id, "slide-3").Should().BeTrue();
        shape.PreservedObject.Parts.Should().BeEmpty();
        shape.PreservedObject.SlideRels.Values.Should().NotContain(relation =>
            relation.RelType.EndsWith("/image", StringComparison.OrdinalIgnoreCase));
        shape.PreservedObject.RawXml.Should().NotContain("embed=");
        shape.Picture.Should().BeNull();

        session.Undo();
        shape.PreservedObject.ZoomTargetSlideNumericId.Should().Be(257);
        shape.PreservedObject.Parts.Values.Should().ContainSingle().Which.Should().BeEquivalentTo(oldPreview);
        shape.Picture!.Bytes.Should().BeEquivalentTo(oldPreview);
        shape.PreservedObject.RawXml.Should().Contain("embed=");
    }

    [Fact]
    public void Retargeting_preserves_user_authored_cover_image()
    {
        var presentation = BuildPresentation();
        presentation.Slides.Add(new Slide { Id = "slide-3", NumericId = 258, Title = "Slide 3" });
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = session.InsertSlideZoom("slide-2");
        var cover = new byte[] { 3, 2, 1 };

        session.SetZoomCoverImage(shape.Id, cover, "image/png").Should().BeTrue();
        session.SetSlideZoomTarget(shape.Id, "slide-3").Should().BeTrue();

        shape.PreservedObject!.ZoomProperties!.ImageType.Should().Be("cover");
        shape.PreservedObject.Parts.Values.Should().ContainSingle().Which.Should().BeEquivalentTo(cover);
        shape.Picture!.Bytes.Should().BeEquivalentTo(cover);
        shape.PreservedObject.RawXml.Should().Contain("embed=");
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

    // R162 F1: a Slide Zoom's target numeric id is baked speculatively when the zoom is
    // authored (see EffectiveNumericId above). If a slide is later duplicated/inserted before
    // the target -- an ordinary, unrelated edit -- the baked numeric id can be stolen by the
    // new slide, and the saved package's sldId silently points at the wrong slide. This must
    // be corrected by the time the file is actually written, so the assertion below goes all
    // the way through PptxPackageWriter + PptxPackageReader rather than only inspecting the
    // in-memory plan.
    [Fact]
    public void Duplicating_a_slide_before_the_zoom_target_still_saves_the_right_target()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Unrelated" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Host" });
        presentation.Slides.Add(new Slide { Id = "slide-3", Title = "Target" });

        // Baked while only these three (shape-less) slides exist, so the prediction (258) is
        // correct for the CURRENT state -- exactly like SlideZoomInsertionPlannerTests above.
        var shape = SlideZoomInsertionPlanner.CreateShape(presentation, 1, "slide-3");
        presentation.Slides[1].Shapes.Add(shape);
        shape.PreservedObject!.ZoomTargetSlideNumericId.Should().Be(258);

        // Completely ordinary, unrelated edit made AFTER the zoom was authored: duplicate the
        // first (shape-less) slide and land the copy before the zoom's target. SlideCloner
        // always gives the duplicate a null NumericId, so it competes for the same 256-upward
        // allocation the zoom's numeric id was already predicted from, shifting "slide-3"'s
        // real save-time id to 259 -- one past what the zoom baked in.
        presentation.Slides.Insert(0, SlideCloner.CloneSlide(presentation.Slides[0]));

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var reopened = PptxPackageReader.Read(stream);

        var targetSlide = reopened.Slides.Single(slide => slide.Title == "Target");
        var zoomShape = reopened.Slides
            .SelectMany(slide => slide.Shapes)
            .Single(candidate => candidate.Kind == SlideShapeKind.Zoom);

        targetSlide.NumericId.Should().Be(259);
        zoomShape.PreservedObject!.ZoomTargetSlideNumericId.Should().Be(targetSlide.NumericId);
        zoomShape.PreservedObject.RawXml.Should().Contain($"sldId=\"{targetSlide.NumericId}\"");
    }

    // Sibling/adjacent case (rule 10): retargeting a Slide Zoom (SetZoomTargetCommand, via
    // EditingSession.SetSlideZoomTarget) must keep working correctly under the same save-time
    // reconciliation -- the fix must not revert a legitimate retarget back to the zoom's
    // original target just because a slide was later duplicated/inserted too.
    [Fact]
    public void Retargeted_zoom_still_saves_the_new_target_after_a_later_duplicate()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "host", Title = "Host" });
        presentation.Slides.Add(new Slide { Id = "unrelated", Title = "Unrelated" });
        presentation.Slides.Add(new Slide { Id = "old-target", Title = "OldTarget" });
        presentation.Slides.Add(new Slide { Id = "new-target", Title = "NewTarget" });

        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = session.InsertSlideZoom("old-target");
        session.SetSlideZoomTarget(shape.Id, "new-target").Should().BeTrue();
        shape.PreservedObject!.ZoomTargetSlideNumericId.Should().Be(259);

        // Another ordinary, unrelated edit made AFTER the retarget: duplicate the "Unrelated"
        // slide and land the copy ahead of everything, shifting "NewTarget"'s real save-time id
        // from the 259 the retarget baked in to 260.
        presentation.Slides.Insert(0, SlideCloner.CloneSlide(presentation.Slides[1]));

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;
        var reopened = PptxPackageReader.Read(stream);

        var targetSlide = reopened.Slides.Single(slide => slide.Title == "NewTarget");
        var oldTargetSlide = reopened.Slides.Single(slide => slide.Title == "OldTarget");
        var zoomShape = reopened.Slides
            .SelectMany(slide => slide.Shapes)
            .Single(candidate => candidate.Kind == SlideShapeKind.Zoom);

        targetSlide.NumericId.Should().Be(260);
        zoomShape.PreservedObject!.ZoomTargetSlideNumericId.Should().Be(targetSlide.NumericId);
        zoomShape.PreservedObject.ZoomTargetSlideNumericId.Should().NotBe(oldTargetSlide.NumericId);
    }

    private static Presentation BuildPresentation()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", NumericId = 256 });
        presentation.Slides.Add(new Slide { Id = "slide-2", NumericId = 257, Title = "Target" });
        return presentation;
    }
}
