using FreeP.App.Compositor;
using FreeP.Core.Model;
using System.Xml.Linq;

namespace FreeP.App.Compositor.Tests;

public sealed class SummaryZoomInsertionPlannerTests
{
    [Fact]
    public void Builds_native_multi_target_summary_zoom_with_fixed_layout()
    {
        var presentation = BuildPresentation();

        SummaryZoomInsertionPlanner.TryBuildPlan(
            presentation,
            new[] { "{SECTION-ONE}", "{SECTION-TWO}", "{SECTION-THREE}" },
            out var plan).Should().BeTrue();
        plan.Targets.Should().HaveCount(3);
        plan.Targets.Select(target => target.SectionId)
            .Should().ContainInOrder("{SECTION-ONE}", "{SECTION-TWO}", "{SECTION-THREE}");

        var shape = SummaryZoomInsertionPlanner.CreateShape(
            presentation,
            plan.Targets.Select(target => target.SectionId));
        shape.PreservedObject!.SummaryZoomTargets.Should().HaveCount(3);
        shape.PreservedObject.RawXml.Should().Contain("summaryzoom");
        shape.PreservedObject.RawXml.Should().Contain("summaryZmObj");
        shape.PreservedObject.RawXml.Should().Contain("fixedLayout");
        XElement.Parse(shape.PreservedObject.RawXml).Descendants()
            .Count(element => element.Name.LocalName == "zmPr")
            .Should().Be(3);
    }

    [Fact]
    public void Summary_zoom_requires_two_valid_sections_and_undoes()
    {
        var presentation = BuildPresentation();
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        SummaryZoomInsertionPlanner.TryBuildPlan(
            presentation,
            new[] { "{SECTION-ONE}" },
            out _).Should().BeFalse();
        SummaryZoomInsertionPlanner.TryBuildPlan(
            presentation,
            new[] { "{SECTION-ONE}", "{MISSING}" },
            out _).Should().BeFalse();

        var shape = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}" });
        presentation.Slides[0].Shapes.Should().Contain(shape);
        session.Undo();
        presentation.Slides[0].Shapes.Should().NotContain(shape);
        session.Redo();
        presentation.Slides[0].Shapes.Should().ContainSingle(item => item.Kind == SlideShapeKind.Zoom);
    }

    [Fact]
    public void Summary_zoom_navigation_uses_the_clicked_tile()
    {
        var presentation = BuildPresentation();
        var shape = SummaryZoomInsertionPlanner.CreateShape(
            presentation,
            new[] { "{SECTION-ONE}", "{SECTION-TWO}", "{SECTION-THREE}" });

        ZoomNavigationService.TryGetTargetSlideIndex(
            presentation, shape.PreservedObject, 0.75, 0.25, out var targetIndex).Should().BeTrue();
        targetIndex.Should().Be(1);

        ZoomNavigationService.TryGetTargetSlideIndex(
            presentation, shape.PreservedObject, 0.25, 0.75, out targetIndex).Should().BeTrue();
        targetIndex.Should().Be(2);
    }

    [Fact]
    public void Zoom_properties_are_undoable_and_update_every_summary_tile()
    {
        var presentation = BuildPresentation();
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var shape = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}", "{SECTION-THREE}" });
        var properties = new ZoomObjectProperties(
            ReturnToParent: false,
            ImageType: "cover",
            TransitionDuration: "120000",
            ShowBackground: false);

        session.SetZoomObjectProperties(shape.Id, properties).Should().BeTrue();
        shape.PreservedObject!.ZoomProperties.Should().Be(properties);
        var xml = XElement.Parse(shape.PreservedObject.RawXml);
        xml.Descendants().Where(element => element.Name.LocalName == "zmPr")
            .Should().AllSatisfy(element =>
            {
                element.Attribute("returnToParent")!.Value.Should().Be("0");
                element.Attribute("imageType")!.Value.Should().Be("cover");
                element.Attribute("transitionDur")!.Value.Should().Be("120000");
                element.Attribute("showBg")!.Value.Should().Be("0");
            });

        session.Undo();
        shape.PreservedObject.ZoomProperties.Should()
            .Be(new ZoomObjectProperties(true, "preview", null, true));
        shape.PreservedObject.RawXml.Should().Contain("imageType=\"preview\"");

        session.Redo();
        shape.PreservedObject.ZoomProperties.Should().Be(properties);
        shape.PreservedObject.RawXml.Should().Contain("imageType=\"cover\"");
    }

    private static Presentation BuildPresentation()
    {
        var presentation = new Presentation();
        for (var index = 1; index <= 4; index++)
            presentation.Slides.Add(new Slide { Id = $"slide-{index}", Title = $"Slide {index}" });

        AddSection(presentation, "{SECTION-ONE}", "One", "slide-1");
        AddSection(presentation, "{SECTION-TWO}", "Two", "slide-2");
        AddSection(presentation, "{SECTION-THREE}", "Three", "slide-3");
        AddSection(presentation, "{SECTION-EMPTY}", "Empty");
        return presentation;
    }

    private static void AddSection(
        Presentation presentation,
        string id,
        string name,
        params string[] slideIds)
    {
        var section = new PresentationSection { Id = id, Name = name };
        section.SlideIds.AddRange(slideIds);
        presentation.Sections.Add(section);
    }
}
