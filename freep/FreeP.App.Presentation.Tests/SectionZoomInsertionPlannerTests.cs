using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SectionZoomInsertionPlannerTests
{
    [Fact]
    public void Builds_native_section_zoom_for_a_named_section()
    {
        var presentation = BuildPresentation();

        var options = SectionZoomInsertionPlanner.BuildTargetOptions(presentation, currentSlideIndex: 0);
        options.Should().ContainSingle(option => option.Id == "{SECTION-TARGET}");

        SectionZoomInsertionPlanner.TryBuildPlan(
            presentation,
            "{SECTION-TARGET}",
            out var plan).Should().BeTrue();

        plan.TargetDisplayName.Should().Be("Target section");
        plan.TargetSlideCount.Should().Be(2);
    }

    [Fact]
    public void Editing_session_inserts_native_section_zoom_and_undoes_it()
    {
        var presentation = BuildPresentation();
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        var shape = session.InsertSectionZoom("{SECTION-TARGET}");

        shape.Kind.Should().Be(SlideShapeKind.Zoom);
        shape.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Zoom);
        shape.PreservedObject.ZoomTargetSectionId.Should().Be("{SECTION-TARGET}");
        shape.PreservedObject.RawXml.Should().Contain("sectionzoom");
        ZoomNavigationService.TryGetTargetSlideIndex(presentation, shape.PreservedObject, out var targetIndex)
            .Should().BeTrue();
        targetIndex.Should().Be(1);

        session.Undo();
        presentation.Slides[0].Shapes.Should().NotContain(shape);
        session.Redo();
        presentation.Slides[0].Shapes.Should().ContainSingle(item => item.Kind == SlideShapeKind.Zoom);
    }

    [Fact]
    public void Rejects_empty_or_unknown_section()
    {
        var presentation = BuildPresentation();

        SectionZoomInsertionPlanner.TryBuildPlan(presentation, "{MISSING}", out _).Should().BeFalse();
        SectionZoomInsertionPlanner.TryBuildPlan(presentation, "{SECTION-EMPTY}", out _).Should().BeFalse();
    }

    private static Presentation BuildPresentation()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target 1" });
        presentation.Slides.Add(new Slide { Id = "slide-3", Title = "Target 2" });

        var target = new PresentationSection { Id = "{SECTION-TARGET}", Name = "Target section" };
        target.SlideIds.Add("slide-2");
        target.SlideIds.Add("slide-3");
        presentation.Sections.Add(target);
        presentation.Sections.Add(new PresentationSection { Id = "{SECTION-EMPTY}", Name = "Empty" });
        return presentation;
    }
}
