using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class ZoomNavigationServiceTests
{
    [Fact]
    public void Resolves_slide_zoom_numeric_id_to_zero_based_slide_index()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "rId2", NumericId = 256 });
        presentation.Slides.Add(new Slide { Id = "rId3", NumericId = 257 });
        var zoom = new PreservedObjectInfo
        {
            ObjectKind = PreservedObjectKind.Zoom,
            ZoomTargetSlideNumericId = 257,
        };

        ZoomNavigationService.TryGetTargetSlideIndex(presentation, zoom, out var index)
            .Should().BeTrue();
        index.Should().Be(1);
    }

    [Fact]
    public void Uses_powerpoint_default_return_to_parent_when_zoom_attribute_is_omitted()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { NumericId = 256 });
        var zoom = new PreservedObjectInfo
        {
            ObjectKind = PreservedObjectKind.Zoom,
            ZoomTargetSlideNumericId = 256,
        };

        ZoomNavigationService.TryGetTargetSlideIndex(
            presentation,
            zoom,
            null,
            null,
            out var index,
            out var returnToParent).Should().BeTrue();

        index.Should().Be(0);
        returnToParent.Should().BeTrue();
    }

    [Fact]
    public void Parses_positive_zoom_transition_duration_in_milliseconds()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { NumericId = 256 });
        var zoom = new PreservedObjectInfo
        {
            ObjectKind = PreservedObjectKind.Zoom,
            ZoomTargetSlideNumericId = 256,
            ZoomProperties = new ZoomObjectProperties(TransitionDuration: "1200"),
        };

        ZoomNavigationService.TryGetTargetSlideIndex(
            presentation,
            zoom,
            null,
            null,
            out _,
            out _,
            out var transitionDurationMs).Should().BeTrue();

        transitionDurationMs.Should().Be(1200);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("not-a-duration")]
    public void Ignores_invalid_zoom_transition_duration(string? value)
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { NumericId = 256 });
        var zoom = new PreservedObjectInfo
        {
            ObjectKind = PreservedObjectKind.Zoom,
            ZoomTargetSlideNumericId = 256,
            ZoomProperties = new ZoomObjectProperties(TransitionDuration: value),
        };

        ZoomNavigationService.TryGetTargetSlideIndex(
            presentation,
            zoom,
            null,
            null,
            out _,
            out _,
            out var transitionDurationMs).Should().BeTrue();

        transitionDurationMs.Should().BeNull();
    }

    [Fact]
    public void Preserves_explicit_return_to_parent_false()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { NumericId = 256 });
        var zoom = new PreservedObjectInfo
        {
            ObjectKind = PreservedObjectKind.Zoom,
            ZoomTargetSlideNumericId = 256,
            ZoomProperties = new ZoomObjectProperties(ReturnToParent: false),
        };

        ZoomNavigationService.TryGetTargetSlideIndex(
            presentation,
            zoom,
            null,
            null,
            out _,
            out var returnToParent).Should().BeTrue();

        returnToParent.Should().BeFalse();
    }

    [Fact]
    public void Resolves_target_from_preserved_raw_xml_when_reader_metadata_is_absent()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { NumericId = 256 });
        var zoom = new PreservedObjectInfo
        {
            ObjectKind = PreservedObjectKind.Zoom,
            RawXml = "<p:graphicFrame xmlns:p=\"urn:p\"><p14:sldZmObj xmlns:p14=\"urn:zoom\" sldId=\"256\"/></p:graphicFrame>",
        };

        ZoomNavigationService.TryGetTargetSlideIndex(presentation, zoom, out var index)
            .Should().BeTrue();
        index.Should().Be(0);
    }

    [Fact]
    public void Rejects_missing_or_unmatched_zoom_target()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { NumericId = 256 });

        ZoomNavigationService.TryGetTargetSlideIndex(
            presentation,
            new PreservedObjectInfo { ObjectKind = PreservedObjectKind.Zoom, ZoomTargetSlideNumericId = 999 },
            out _).Should().BeFalse();
        ZoomNavigationService.TryGetTargetSlideIndex(
            presentation,
            new PreservedObjectInfo { ObjectKind = PreservedObjectKind.Model3d, ZoomTargetSlideNumericId = 256 },
            out _).Should().BeFalse();
    }

    [Fact]
    public void Resolves_section_zoom_to_the_first_live_slide_in_that_section()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "rId2", NumericId = 256 });
        presentation.Slides.Add(new Slide { Id = "rId3", NumericId = 257 });
        presentation.Sections.Add(new PresentationSection
        {
            Id = "{SECTION-ONE}",
            Name = "Section One",
        });
        presentation.Sections[0].SlideIds.Add("rId3");
        var zoom = new PreservedObjectInfo
        {
            ObjectKind = PreservedObjectKind.Zoom,
            ZoomTargetSectionId = "{section-one}",
        };

        ZoomNavigationService.TryGetTargetSlideIndex(presentation, zoom, out var index)
            .Should().BeTrue();
        index.Should().Be(1);
    }

    [Fact]
    public void Resolves_section_zoom_target_from_preserved_raw_xml()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "rId2", NumericId = 256 });
        presentation.Sections.Add(new PresentationSection { Id = "{SECTION-ONE}" });
        presentation.Sections[0].SlideIds.Add("rId2");
        var zoom = new PreservedObjectInfo
        {
            ObjectKind = PreservedObjectKind.Zoom,
            RawXml = "<p:graphicFrame xmlns:p=\"urn:p\"><psez:sectionZmObj xmlns:psez=\"urn:section-zoom\" sectionId=\"{section-one}\"/></p:graphicFrame>",
        };

        ZoomNavigationService.TryGetTargetSlideIndex(presentation, zoom, out var index)
            .Should().BeTrue();
        index.Should().Be(0);
    }
}
