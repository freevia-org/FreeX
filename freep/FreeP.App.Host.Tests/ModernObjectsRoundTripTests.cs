using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FreeP.App.Compositor;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 25A: Round-trip tests for slide zoom, ink contentPart, 3D model, and unknown
/// graphicFrame preservation (no silent loss guarantee).
/// </summary>
public sealed class ModernObjectsRoundTripTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.ModernObjectsTests", Guid.NewGuid().ToString("N"));

    public ModernObjectsRoundTripTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Minimal 1×1 white PNG ─────────────────────────────────────────────────
    private static readonly byte[] MinPng =
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    // ── Zoom graphicFrame round-trip ──────────────────────────────────────────

    [Fact]
    public void ZoomGraphicFrame_RoundTrips_VerbatimXmlAndPreservedKind()
    {
        // Build a PPTX with a synthetic zoom graphicFrame injected into slide1
        const string zoomUri = "http://schemas.microsoft.com/office/powerpoint/2010/main";
        const string zoomXml = """
            <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <p:nvGraphicFramePr>
                <p:cNvPr id="10" name="Zoom 10"/>
                <p:cNvGraphicFramePr/>
                <p:nvPr/>
              </p:nvGraphicFramePr>
              <p:xfrm>
                <a:off x="457200" y="274638"/>
                <a:ext cx="2743200" cy="1828800"/>
              </p:xfrm>
              <a:graphic>
                <a:graphicData uri="http://schemas.microsoft.com/office/powerpoint/2010/main">
                  <p14:zoom xmlns:p14="http://schemas.microsoft.com/office/powerpoint/2010/main" type="slide"/>
                </a:graphicData>
              </a:graphic>
            </p:graphicFrame>
            """;

        var ms1 = BuildPptxWithShapeXml(zoomXml);

        // Read
        var pres1 = PptxPackageReader.Read(ms1);
        var slide1 = pres1.Slides[0];
        var zoom = slide1.Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.Zoom);

        zoom.Should().NotBeNull("zoom graphicFrame should not be silently dropped");
        zoom!.PreservedObject.Should().NotBeNull();
        zoom.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Zoom);
        zoom.PreservedObject.RawXml.Should().Contain(zoomUri);

        // Write → re-read (round-trip)
        var ms2 = WritePptxToMemory(pres1);
        var pres2 = PptxPackageReader.Read(ms2);
        var zoom2 = pres2.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.Zoom);

        zoom2.Should().NotBeNull("zoom must survive write/re-read round-trip");
        zoom2!.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Zoom);
        zoom2.PreservedObject.RawXml.Should().Contain(zoomUri);
    }

    [Fact]
    public void PreservedObject_NonVisualProperties_RoundTripEditedState()
    {
        const string zoomXml = """
            <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <p:nvGraphicFramePr>
                <p:cNvPr id="10" name="Original Zoom"/>
                <p:cNvGraphicFramePr/>
                <p:nvPr/>
              </p:nvGraphicFramePr>
              <p:xfrm>
                <a:off x="457200" y="274638"/>
                <a:ext cx="2743200" cy="1828800"/>
              </p:xfrm>
              <a:graphic>
                <a:graphicData uri="http://schemas.microsoft.com/office/powerpoint/2010/main">
                  <p14:zoom xmlns:p14="http://schemas.microsoft.com/office/powerpoint/2010/main" type="slide"/>
                </a:graphicData>
              </a:graphic>
            </p:graphicFrame>
            """;

        var presentation = PptxPackageReader.Read(BuildPptxWithShapeXml(zoomXml));
        var zoom = presentation.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom);
        zoom.Name = "Edited Zoom";
        zoom.IsHidden = true;
        zoom.AlternativeTextTitle = "Navigation target";
        zoom.AlternativeText = "Opens the quarterly overview.";
        zoom.IsDecorative = true;

        var roundTripped = PptxPackageReader.Read(WritePptxToMemory(presentation));
        var edited = roundTripped.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom);

        edited.Name.Should().Be("Edited Zoom");
        edited.IsHidden.Should().BeTrue();
        edited.AlternativeTextTitle.Should().Be("Navigation target");
        edited.AlternativeText.Should().Be("Opens the quarterly overview.");
        edited.IsDecorative.Should().BeTrue();
    }

    [Fact]
    public void SlideZoom_CapturesTargetSlideId_AndPreservesItOnWrite()
    {
        const string zoomXml = """
            <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                            xmlns:pslz="http://schemas.microsoft.com/office/powerpoint/2016/slidezoom">
              <p:nvGraphicFramePr>
                <p:cNvPr id="10" name="Slide Zoom 10"/>
                <p:cNvGraphicFramePr/>
                <p:nvPr/>
              </p:nvGraphicFramePr>
              <p:xfrm>
                <a:off x="457200" y="274638"/>
                <a:ext cx="2743200" cy="1828800"/>
              </p:xfrm>
              <a:graphic>
                <a:graphicData uri="http://schemas.microsoft.com/office/powerpoint/2016/slidezoom">
                  <pslz:sldZm>
                    <pslz:sldZmObj sldId="257"/>
                  </pslz:sldZm>
                </a:graphicData>
              </a:graphic>
            </p:graphicFrame>
            """;

        var pres1 = PptxPackageReader.Read(BuildPptxWithShapeXml(zoomXml));
        var zoom = pres1.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom);

        pres1.Slides[0].NumericId.Should().Be(256);
        zoom.PreservedObject!.ZoomTargetSlideNumericId.Should().Be(257);

        var pres2 = PptxPackageReader.Read(WritePptxToMemory(pres1));
        pres2.Slides[0].NumericId.Should().Be(256);
        pres2.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom)
            .PreservedObject!.ZoomTargetSlideNumericId.Should().Be(257);
    }

    [Fact]
    public void SectionZoom_CapturesTargetSectionId()
    {
        const string zoomXml = """
            <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                            xmlns:psez="http://schemas.microsoft.com/office/powerpoint/2016/sectionzoom">
              <p:nvGraphicFramePr>
                <p:cNvPr id="10" name="Section Zoom 10"/>
                <p:cNvGraphicFramePr/>
                <p:nvPr/>
              </p:nvGraphicFramePr>
              <p:xfrm>
                <a:off x="457200" y="274638"/>
                <a:ext cx="2743200" cy="1828800"/>
              </p:xfrm>
              <a:graphic>
                <a:graphicData uri="http://schemas.microsoft.com/office/powerpoint/2016/sectionzoom">
                  <psez:sectionZm>
                    <psez:sectionZmObj sectionId="{SECTION-ONE}"/>
                  </psez:sectionZm>
                </a:graphicData>
              </a:graphic>
            </p:graphicFrame>
            """;

        var presentation = PptxPackageReader.Read(BuildPptxWithShapeXml(zoomXml));
        var zoom = presentation.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom);

        zoom.PreservedObject!.ZoomTargetSectionId.Should().Be("{SECTION-ONE}");
    }

    [Fact]
    public void AuthoredSlideZoom_WritesNativeTargetAndReopens()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        var inserted = session.InsertSlideZoom("slide-2");
        SummaryZoomPreviewPlanner.AttachPreviewImage(
            presentation,
            inserted,
            targetSlideIndex: 1,
            _ => MinPng).Should().BeTrue();

        var roundTripped = PptxPackageReader.Read(WritePptxToMemory(presentation));
        var zoom = roundTripped.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom);

        zoom.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Zoom);
        zoom.PreservedObject.ZoomTargetSlideNumericId.Should().Be(257);
        zoom.PreservedObject.RawXml.Should().Contain("slidezoom");
        zoom.PreservedObject.RawXml.Should().Contain("sldId=\"257\"");
        zoom.PreservedObject.RawXml.Should().Contain("zmPr");
        zoom.PreservedObject.RawXml.Should().Contain("imageType=\"preview\"");
        zoom.PreservedObject.RawXml.Should().Contain("blipFill");
        zoom.PreservedObject.Parts.Keys.Should().ContainSingle(key =>
            key.StartsWith("ppt/media/freep-zoom-preview-", StringComparison.OrdinalIgnoreCase));
        zoom.PreservedObject.ZoomProperties.Should()
            .Be(new ZoomObjectProperties(true, "preview", null, true));
        zoom.PreservedObject.RawXml.Should().Contain("blipFill");
        zoom.PreservedObject.RawXml.Should().Contain("spPr");
    }

    [Fact]
    public void AuthoredSectionZoom_WritesNativeTargetAndReopens()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target 1" });
        presentation.Slides.Add(new Slide { Id = "slide-3", Title = "Target 2" });
        var section = new PresentationSection { Id = "{SECTION-TARGET}", Name = "Target section" };
        section.SlideIds.Add("slide-2");
        section.SlideIds.Add("slide-3");
        presentation.Sections.Add(section);
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));

        var inserted = session.InsertSectionZoom(section.Id);
        SummaryZoomPreviewPlanner.AttachPreviewImage(
            presentation,
            inserted,
            targetSlideIndex: 1,
            _ => MinPng).Should().BeTrue();

        var roundTripped = PptxPackageReader.Read(WritePptxToMemory(presentation));
        var zoom = roundTripped.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom);

        zoom.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Zoom);
        zoom.PreservedObject.ZoomTargetSectionId.Should().Be(section.Id);
        zoom.PreservedObject.RawXml.Should().Contain("sectionzoom");
        zoom.PreservedObject.RawXml.Should().Contain("sectionId=\"{SECTION-TARGET}\"");
        zoom.PreservedObject.RawXml.Should().Contain("zmPr");
        zoom.PreservedObject.RawXml.Should().Contain("imageType=\"preview\"");
        zoom.PreservedObject.RawXml.Should().Contain("blipFill");
        zoom.PreservedObject.Parts.Keys.Should().ContainSingle(key =>
            key.StartsWith("ppt/media/freep-zoom-preview-", StringComparison.OrdinalIgnoreCase));
        zoom.PreservedObject.ZoomProperties.Should()
            .Be(new ZoomObjectProperties(true, "preview", null, true));
        zoom.PreservedObject.RawXml.Should().Contain("blipFill");
        zoom.PreservedObject.RawXml.Should().Contain("spPr");
        roundTripped.Sections.Should().ContainSingle(item => item.Id == section.Id);
    }

    [Fact]
    public void AuthoredSlideZoom_CoverImage_WritesRelationshipAndReopens()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var inserted = session.InsertSlideZoom("slide-2");
        var image = new byte[] { 4, 3, 2, 1 };

        session.SetZoomCoverImage(inserted.Id, image, "image/png").Should().BeTrue();

        var roundTripped = PptxPackageReader.Read(WritePptxToMemory(presentation));
        var zoom = roundTripped.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom);

        zoom.PreservedObject!.ZoomProperties!.ImageType.Should().Be("cover");
        zoom.PreservedObject.RawXml.Should().Contain("imageType=\"cover\"");
        zoom.PreservedObject.RawXml.Should().Contain("blipFill");
        zoom.PreservedObject.Parts.Values.Should().ContainSingle().Which.Should().BeEquivalentTo(image);
        zoom.PreservedObject.SlideRels.Values.Should().ContainSingle(rel =>
            rel.RelType.EndsWith("/image", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AuthoredSlideZoom_CoverImage_CanRestorePreviewAndUndo()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });
        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var inserted = session.InsertSlideZoom("slide-2");
        var cover = new byte[] { 4, 3, 2, 1 };
        var preview = new byte[] { 9, 8, 7, 6 };

        session.SetZoomCoverImage(inserted.Id, cover, "image/png").Should().BeTrue();
        session.ResetZoomCoverImage(inserted.Id, preview, "image/png").Should().BeTrue();
        inserted.PreservedObject!.ZoomProperties!.ImageType.Should().Be("preview");
        inserted.Picture!.Bytes.Should().Equal(preview);

        session.Undo();
        inserted.PreservedObject.ZoomProperties!.ImageType.Should().Be("cover");
        inserted.Picture!.Bytes.Should().Equal(cover);

        session.Redo();
        inserted.PreservedObject.ZoomProperties!.ImageType.Should().Be("preview");
        inserted.Picture!.Bytes.Should().Equal(preview);

        var roundTripped = PptxPackageReader.Read(WritePptxToMemory(presentation));
        var zoom = roundTripped.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom);
        zoom.PreservedObject!.RawXml.Should().Contain("imageType=\"preview\"");
        zoom.PreservedObject.Parts.Values.Should().ContainSingle().Which.Should().Equal(preview);
    }

    [Fact]
    public void AuthoredSummaryZoom_WritesAllTargetsAndReopens()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target 1" });
        presentation.Slides.Add(new Slide { Id = "slide-3", Title = "Target 2" });
        presentation.Slides.Add(new Slide { Id = "slide-4", Title = "Target 3" });
        foreach (var (id, name, slideId) in new[]
                 {
                     ("{SECTION-ONE}", "One", "slide-2"),
                     ("{SECTION-TWO}", "Two", "slide-3"),
                     ("{SECTION-THREE}", "Three", "slide-4"),
                 })
        {
            var section = new PresentationSection { Id = id, Name = name };
            section.SlideIds.Add(slideId);
            presentation.Sections.Add(section);
        }

        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var inserted = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}", "{SECTION-THREE}" });
        SummaryZoomPreviewPlanner.AttachPreviewImages(
            presentation,
            inserted,
            _ => MinPng);

        var roundTripped = PptxPackageReader.Read(WritePptxToMemory(presentation));
        var zoom = roundTripped.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom);

        zoom.PreservedObject!.SummaryZoomTargets.Select(target => target.SectionId)
            .Should().ContainInOrder("{SECTION-ONE}", "{SECTION-TWO}", "{SECTION-THREE}");
        zoom.PreservedObject.RawXml.Should().Contain("summaryzoom");
        zoom.PreservedObject.RawXml.Should().Contain("fixedLayout");
        zoom.PreservedObject.RawXml.Should().Contain("summaryZmObj");
        zoom.PreservedObject.Parts.Should().HaveCount(3);
        zoom.PreservedObject.RawXml.Should().Contain("embed=");
        zoom.PreservedObject.ZoomProperties.Should()
            .Be(new ZoomObjectProperties(true, "preview", null, true));
        zoom.PreservedObject.WasAlternateContent.Should().BeTrue();
        zoom.PreservedObject.McRequiresToken.Should().Be("p14");
        zoom.PreservedObject.AlternateContentFallbackXml.Should().Contain("<p:sp");

        using var saved = WritePptxToMemory(presentation);
        using var zip = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var slideXmlEntry = zip.Entries.First(entry =>
            entry.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        using var slideReader = new StreamReader(slideXmlEntry.Open());
        var slideXml = slideReader.ReadToEnd();
        slideXml.Should().Contain("AlternateContent");
        slideXml.Should().Contain("Requires=\"p14\"");
        slideXml.Should().Contain("Summary Zoom");
        slideXml.Should().Contain("roundRect");
    }

    [Fact]
    public void ZoomPreviewCrop_IsUndoableAndRoundTripsAsDrawingMlSourceRect()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });

        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var zoom = session.InsertSlideZoom("slide-2");
        var cropped = new ZoomObjectProperties(
            ReturnToParent: true,
            ImageType: "preview",
            ShowBackground: true,
            CropLeft: 12500,
            CropTop: 25000,
            CropRight: 37500,
            CropBottom: 50000);

        session.SetZoomObjectProperties(zoom.Id, cropped).Should().BeTrue();
        var srcRect = XElement.Parse(zoom.PreservedObject!.RawXml)
            .Descendants().Single(element => element.Name.LocalName == "srcRect");
        srcRect.Attributes().Select(attribute => (attribute.Name.LocalName, attribute.Value))
            .Should().ContainInOrder(
                ("l", "12500"), ("t", "25000"), ("r", "37500"), ("b", "50000"));

        session.Undo();
        zoom.PreservedObject.RawXml.Should().NotContain("srcRect");
        session.Redo();
        zoom.PreservedObject.RawXml.Should().Contain("srcRect");

        var reopened = PptxPackageReader.Read(WritePptxToMemory(presentation));
        reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom)
            .PreservedObject!.ZoomProperties.Should().Be(cropped);
    }

    [Fact]
    public void ZoomTransition_ToggleIsUndoableAndRoundTripsNativeDuration()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });

        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var zoom = session.InsertSlideZoom("slide-2");
        ZoomObjectPropertiesPlanner.TryParseTransitionDuration(
                " 01250 ", enabled: true, out var normalized)
            .Should().BeTrue();

        var enabled = new ZoomObjectProperties(TransitionDuration: normalized);
        session.SetZoomObjectProperties(zoom.Id, enabled).Should().BeTrue();
        XElement.Parse(zoom.PreservedObject!.RawXml)
            .Descendants().Single(element => element.Name.LocalName == "zmPr")
            .Attribute("transitionDur")!.Value.Should().Be("1250");

        session.Undo();
        XElement.Parse(zoom.PreservedObject.RawXml)
            .Descendants().Single(element => element.Name.LocalName == "zmPr")
            .Attribute("transitionDur").Should().BeNull();
        session.Redo();

        var reopened = PptxPackageReader.Read(WritePptxToMemory(presentation));
        reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom)
            .PreservedObject!.ZoomProperties!.TransitionDuration.Should().Be("1250");

        ZoomObjectPropertiesPlanner.TryParseTransitionDuration(
                "1250", enabled: false, out var disabledDuration)
            .Should().BeTrue();
        session.SetZoomObjectProperties(
                zoom.Id,
                new ZoomObjectProperties(TransitionDuration: disabledDuration))
            .Should().BeTrue();
        XElement.Parse(zoom.PreservedObject.RawXml)
            .Descendants().Single(element => element.Name.LocalName == "zmPr")
            .Attribute("transitionDur").Should().BeNull();
    }

    [Fact]
    public void ZoomFrameBorder_IsUndoableAndRoundTripsNativeSolidColor()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });

        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var zoom = session.InsertSlideZoom("slide-2");
        var insertedZoomProperties = XElement.Parse(zoom.PreservedObject!.RawXml)
            .Descendants().Single(element => element.Name.LocalName == "zmPr");
        insertedZoomProperties.Elements().Select(element => element.Name.LocalName)
            .Should().Equal("blipFill", "spPr");
        insertedZoomProperties.Elements().Single(element => element.Name.LocalName == "spPr")
            .Parent.Should().BeSameAs(insertedZoomProperties);
        session.SetZoomObjectProperties(
                zoom.Id,
                new ZoomObjectProperties(
                    FrameBorderColor: "4472C4",
                    FrameBorderWidthEmu: 25400,
                    FrameBorderDash: OutlineDash.Dot))
            .Should()
            .BeTrue();

        var lineColor = XElement.Parse(zoom.PreservedObject!.RawXml)
            .Descendants().Single(element => element.Name.LocalName == "ln")
            .Descendants().Single(element => element.Name.LocalName == "srgbClr");
        lineColor.Attribute("val")!.Value.Should().Be("4472C4");
        XElement.Parse(zoom.PreservedObject.RawXml)
            .Descendants().Single(element => element.Name.LocalName == "ln")
            .Attribute("w")!.Value.Should().Be("25400");
        XElement.Parse(zoom.PreservedObject.RawXml)
            .Descendants().Single(element => element.Name.LocalName == "prstDash")
            .Attribute("val")!.Value.Should().Be("dot");
        zoom.PreservedObject.ZoomProperties!.FrameBorderColor.Should().Be("4472C4");
        zoom.PreservedObject.ZoomProperties.FrameBorderWidthEmu.Should().Be(25400);
        zoom.PreservedObject.ZoomProperties.FrameBorderDash.Should().Be(OutlineDash.Dot);

        session.Undo();
        zoom.PreservedObject.RawXml.Should().NotContain("4472C4");
        session.Redo();

        var reopened = PptxPackageReader.Read(WritePptxToMemory(presentation));
        reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom)
            .PreservedObject!.ZoomProperties!.FrameBorderDash.Should().Be(OutlineDash.Dot);
    }

    [Fact]
    public void ZoomFrameBorder_GradientIsUndoableAndRoundTripsNativeStops()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });

        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var zoom = session.InsertSlideZoom("slide-2");
        var gradient = new ZoomFrameBorderGradient("4472C4", "FFFFFF", 8_130_000);
        session.SetZoomObjectProperties(
                zoom.Id,
                new ZoomObjectProperties(
                    FrameBorderWidthEmu: 25400,
                    FrameBorderGradient: gradient))
            .Should().BeTrue();

        var line = XElement.Parse(zoom.PreservedObject!.RawXml)
            .Descendants().Single(element => element.Name.LocalName == "ln");
        line.Element(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main") + "gradFill")
            .Should().NotBeNull();
        line.Descendants().Where(element => element.Name.LocalName == "srgbClr")
            .Select(element => element.Attribute("val")!.Value)
            .Should().Equal("4472C4", "FFFFFF");
        zoom.PreservedObject.ZoomProperties!.FrameBorderGradient.Should().Be(gradient);

        session.Undo();
        zoom.PreservedObject.RawXml.Should().NotContain("4472C4");
        session.Redo();

        var reopened = PptxPackageReader.Read(WritePptxToMemory(presentation));
        reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom)
            .PreservedObject!.ZoomProperties!.FrameBorderGradient.Should().Be(gradient);
    }

    [Fact]
    public void ZoomFrameBorder_PatternIsUndoableAndRoundTripsNativeColors()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });

        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var zoom = session.InsertSlideZoom("slide-2");
        var pattern = new ZoomFrameBorderPattern("pct50", "F2F2F2", "FFFFFF");
        session.SetZoomObjectProperties(
                zoom.Id,
                new ZoomObjectProperties(
                    FrameBorderWidthEmu: 25400,
                    FrameBorderDash: OutlineDash.Dot,
                    FrameBorderPattern: pattern))
            .Should().BeTrue();

        var line = XElement.Parse(zoom.PreservedObject!.RawXml)
            .Descendants().Single(element => element.Name.LocalName == "ln");
        var drawing = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var patternXml = line.Element(drawing + "pattFill");
        patternXml.Should().NotBeNull();
        patternXml!.Attribute("prst")!.Value.Should().Be("pct50");
        patternXml.Descendants().Where(element => element.Name.LocalName == "srgbClr")
            .Select(element => element.Attribute("val")!.Value)
            .Should().Equal("F2F2F2", "FFFFFF");
        zoom.PreservedObject.ZoomProperties!.FrameBorderPattern.Should().Be(pattern);

        session.Undo();
        zoom.PreservedObject.RawXml.Should().NotContain("F2F2F2");
        session.Redo();

        var reopened = PptxPackageReader.Read(WritePptxToMemory(presentation));
        reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom)
            .PreservedObject!.ZoomProperties!.FrameBorderPattern.Should().Be(pattern);
    }

    [Fact]
    public void ZoomFrameGeometry_IsUndoableAndRoundTripsNativePreset()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });

        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var zoom = session.InsertSlideZoom("slide-2");
        session.SetZoomObjectProperties(
                zoom.Id,
                new ZoomObjectProperties(FrameGeometry: "ellipse"))
            .Should().BeTrue();

        XElement.Parse(zoom.PreservedObject!.RawXml)
            .Descendants().Single(element => element.Name.LocalName == "prstGeom")
            .Attribute("prst")!.Value.Should().Be("ellipse");
        zoom.PreservedObject.ZoomProperties!.FrameGeometry.Should().Be("ellipse");

        session.Undo();
        XElement.Parse(zoom.PreservedObject.RawXml)
            .Descendants().Single(element => element.Name.LocalName == "prstGeom")
            .Attribute("prst")!.Value.Should().Be("rect");
        session.Redo();

        var reopened = PptxPackageReader.Read(WritePptxToMemory(presentation));
        reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom)
            .PreservedObject!.ZoomProperties!.FrameGeometry.Should().Be("ellipse");
    }

    [Fact]
    public void ZoomFrameBorder_ClearPreservesUnsupportedNativeLineFill()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });

        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var zoom = session.InsertSlideZoom("slide-2");
        var raw = XElement.Parse(zoom.PreservedObject!.RawXml);
        var drawing = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var shapeProperties = raw.Descendants().Single(element => element.Name.LocalName == "spPr");
        shapeProperties.Add(new XElement(drawing + "ln",
            new XElement(drawing + "pattFill")));
        zoom.PreservedObject.RawXml = raw.ToString(SaveOptions.DisableFormatting);

        session.SetZoomObjectProperties(
                zoom.Id,
                new ZoomObjectProperties(FrameBorderColor: string.Empty))
            .Should()
            .BeTrue();

        zoom.PreservedObject.RawXml.Should().Contain("pattFill");
    }

    [Fact]
    public void ZoomFrameBorder_EditPreservesNativeThemeFill()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target" });
        var zoom = SlideZoomInsertionPlanner.CreateShape(presentation, 0, "slide-2");
        presentation.Slides[0].Shapes.Add(zoom);

        var drawing = XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main");
        var raw = XElement.Parse(zoom.PreservedObject!.RawXml);
        var shapeProperties = raw.Descendants()
            .Single(element => element.Name.LocalName == "spPr");
        var line = new XElement(drawing + "ln");
        shapeProperties.Add(line);
        line.Elements(drawing + "solidFill").Remove();
        line.Add(new XElement(drawing + "solidFill",
            new XElement(drawing + "schemeClr", new XAttribute("val", "accent1"))));
        zoom.PreservedObject.RawXml = raw.ToString(SaveOptions.DisableFormatting);

        var session = new EditingSession(
            presentation, new PresentationCommandBus(presentation));
        session.SetZoomObjectProperties(
                zoom.Id,
                new ZoomObjectProperties(
                    FrameBorderWidthEmu: 25400,
                    FrameBorderDash: OutlineDash.Dot))
            .Should().BeTrue();

        var edited = XElement.Parse(zoom.PreservedObject.RawXml);
        edited.Descendants(drawing + "schemeClr").Should().ContainSingle(element =>
            element.Attribute("val")!.Value == "accent1");
        edited.Descendants(drawing + "srgbClr").Should().BeEmpty();
        edited.Descendants(drawing + "prstDash").Single()
            .Attribute("val")!.Value.Should().Be("dot");
    }

    [Fact]
    public void SummaryZoomTileLayout_IsUndoableAndRoundTripsNativeFactors()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target 1" });
        presentation.Slides.Add(new Slide { Id = "slide-3", Title = "Target 2" });
        foreach (var (id, slideId) in new[]
                 { ("{SECTION-ONE}", "slide-2"), ("{SECTION-TWO}", "slide-3") })
        {
            var section = new PresentationSection { Id = id, Name = id };
            section.SlideIds.Add(slideId);
            presentation.Sections.Add(section);
        }

        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var zoom = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}" });
        var originalTile = zoom.PreservedObject!.SummaryZoomTargets.Single(target =>
            target.SectionId == "{SECTION-TWO}");
        session.SetSummaryZoomTileLayout(
            zoom.Id, "{SECTION-TWO}", -12500, 8750, 112500, 90000).Should().BeTrue();

        var tile = XElement.Parse(zoom.PreservedObject!.RawXml).Descendants()
            .Single(element => element.Name.LocalName == "summaryZmObj"
                && element.Attribute("sectionId")?.Value == "{SECTION-TWO}");
        tile.Attribute("offsetFactorX")!.Value.Should().Be("-12500");
        tile.Attribute("offsetFactorY")!.Value.Should().Be("8750");
        tile.Attribute("scaleFactorX")!.Value.Should().Be("112500");
        tile.Attribute("scaleFactorY")!.Value.Should().Be("90000");

        session.Undo();
        zoom.PreservedObject.SummaryZoomTargets.Single(target => target.SectionId == "{SECTION-TWO}")
            .Should().Be(originalTile);
        session.Redo();
        zoom.PreservedObject.SummaryZoomTargets.Single(target => target.SectionId == "{SECTION-TWO}")
            .ScaleFactorX.Should().Be(112500);

        var reopened = PptxPackageReader.Read(WritePptxToMemory(presentation));
        reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom)
            .PreservedObject!.SummaryZoomTargets.Single(target => target.SectionId == "{SECTION-TWO}")
            .Should().Be(new SummaryZoomTarget(
                "{SECTION-TWO}", "{SECTION-TWO}", string.Empty,
                -12500, 8750, 112500, 90000));
    }

    [Fact]
    public void AuthoredSummaryZoom_CoverImageTargetsSingleTileAndReopens()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target 1" });
        presentation.Slides.Add(new Slide { Id = "slide-3", Title = "Target 2" });
        foreach (var (id, name, slideId) in new[]
                 {
                     ("{SECTION-ONE}", "One", "slide-2"),
                     ("{SECTION-TWO}", "Two", "slide-3"),
                 })
        {
            var section = new PresentationSection { Id = id, Name = name };
            section.SlideIds.Add(slideId);
            presentation.Sections.Add(section);
        }

        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var inserted = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}" });
        var firstImage = new byte[] { 7, 8, 9 };
        var secondImage = new byte[] { 10, 11, 12 };
        session.SetSummaryZoomTileCoverImage(
            inserted.Id, "{SECTION-ONE}", firstImage, "image/png").Should().BeTrue();
        session.SetSummaryZoomTileCoverImage(
            inserted.Id, "{SECTION-TWO}", secondImage, "image/jpeg").Should().BeTrue();

        var roundTripped = PptxPackageReader.Read(WritePptxToMemory(presentation));
        var zoom = roundTripped.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom);
        var root = XElement.Parse(zoom.PreservedObject!.RawXml);
        var tiles = root.Descendants().Where(element => element.Name.LocalName == "summaryZmObj").ToArray();

        var firstTile = tiles.Single(tile => tile.Attribute("sectionId")?.Value == "{SECTION-ONE}");
        firstTile.Descendants().Any(element =>
            element.Name.LocalName == "zmPr"
            && element.Attribute("imageType")?.Value == "cover").Should().BeTrue();
        var secondTile = tiles.Single(tile => tile.Attribute("sectionId")?.Value == "{SECTION-TWO}");
        secondTile.Descendants().Any(element =>
            element.Name.LocalName == "zmPr"
            && element.Attribute("imageType")?.Value == "cover").Should().BeTrue();
        zoom.PreservedObject.Parts.Values.Should().Contain(bytes => bytes.SequenceEqual(firstImage));
        zoom.PreservedObject.Parts.Values.Should().Contain(bytes => bytes.SequenceEqual(secondImage));
        zoom.PreservedObject.SlideRels.Values.Should().HaveCount(2);
        zoom.PreservedObject.SlideRels.Values.Should().OnlyContain(rel =>
            rel.RelType.EndsWith("/image", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AuthoredSummaryZoom_CoverImage_CanRestoreOneTileWithoutChangingItsSibling()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Source" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Target 1" });
        presentation.Slides.Add(new Slide { Id = "slide-3", Title = "Target 2" });
        foreach (var (id, name, slideId) in new[]
                 {
                     ("{SECTION-ONE}", "One", "slide-2"),
                     ("{SECTION-TWO}", "Two", "slide-3"),
                 })
        {
            var section = new PresentationSection { Id = id, Name = name };
            section.SlideIds.Add(slideId);
            presentation.Sections.Add(section);
        }

        var session = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var inserted = session.InsertSummaryZoom(new[] { "{SECTION-ONE}", "{SECTION-TWO}" });
        var firstCover = new byte[] { 1, 2, 3 };
        var firstPreview = new byte[] { 4, 5, 6 };
        var secondCover = new byte[] { 7, 8, 9 };

        session.SetSummaryZoomTileCoverImage(
            inserted.Id, "{SECTION-ONE}", firstCover, "image/png").Should().BeTrue();
        session.SetSummaryZoomTileCoverImage(
            inserted.Id, "{SECTION-TWO}", secondCover, "image/jpeg").Should().BeTrue();
        session.ResetSummaryZoomTileCoverImage(
            inserted.Id, "{SECTION-ONE}", firstPreview, "image/png").Should().BeTrue();

        var root = XElement.Parse(inserted.PreservedObject!.RawXml);
        var tiles = root.Descendants().Where(element => element.Name.LocalName == "summaryZmObj").ToArray();
        var firstTile = tiles.Single(tile => tile.Attribute("sectionId")?.Value == "{SECTION-ONE}");
        var secondTile = tiles.Single(tile => tile.Attribute("sectionId")?.Value == "{SECTION-TWO}");
        firstTile.Descendants().Any(element =>
            element.Name.LocalName == "zmPr"
            && element.Attribute("imageType")?.Value == "preview").Should().BeTrue();
        secondTile.Descendants().Any(element =>
            element.Name.LocalName == "zmPr"
            && element.Attribute("imageType")?.Value == "cover").Should().BeTrue();
        inserted.Picture.Should().BeNull();
        inserted.PreservedObject.Parts.Values.Should().Contain(bytes => bytes.SequenceEqual(firstPreview));
        inserted.PreservedObject.Parts.Values.Should().Contain(bytes => bytes.SequenceEqual(secondCover));

        session.Undo();
        XElement.Parse(inserted.PreservedObject.RawXml).Descendants()
            .Single(element => element.Name.LocalName == "summaryZmObj"
                && element.Attribute("sectionId")?.Value == "{SECTION-ONE}")
            .Descendants().Any(element =>
                element.Name.LocalName == "zmPr"
                && element.Attribute("imageType")?.Value == "cover").Should().BeTrue();
    }

    // ── Ink contentPart round-trip ────────────────────────────────────────────

    [Fact]
    public void InkContentPart_RoundTrips_VerbatimXmlAndCapturesBytes()
    {
        const string inkXml = """
            <p:contentPart xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                           xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                           r:id="rIdInk1">
              <p:nvContentPartPr>
                <p:cNvPr id="20" name="Ink 20"/>
              </p:nvContentPartPr>
              <p:xfrm xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                <a:off x="914400" y="457200"/>
                <a:ext cx="1828800" cy="914400"/>
              </p:xfrm>
            </p:contentPart>
            """;
        var inkBytes = Encoding.UTF8.GetBytes("<inkml><trace>0 0 1 1</trace></inkml>");
        const string inkRelType = "http://schemas.microsoft.com/office/2016/05/19/relationships/ink";

        var ms1 = BuildPptxWithContentPart(inkXml, inkBytes, inkRelType,
            inkPartPath: "ppt/ink/ink1.xml", inkRelId: "rIdInk1");

        var pres1 = PptxPackageReader.Read(ms1);
        var inkShape = pres1.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.Ink);

        inkShape.Should().NotBeNull("ink contentPart should not be silently dropped");
        inkShape!.PreservedObject.Should().NotBeNull();
        inkShape.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Ink);
        inkShape.PreservedObject.Parts.Values.Should().Contain(b => b.Length > 0,
            "the ink part bytes should have been captured");

        // Round-trip
        var ms2 = WritePptxToMemory(pres1);
        var pres2 = PptxPackageReader.Read(ms2);
        var ink2 = pres2.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.Ink);

        ink2.Should().NotBeNull("ink must survive write/re-read round-trip");
        ink2!.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Ink);
        ink2.PreservedObject.Parts.Values.Should().Contain(b => b.Length > 0,
            "ink part bytes should survive round-trip");
    }

    // ── 3D model graphicFrame round-trip ──────────────────────────────────────

    [Fact]
    public void Model3dGraphicFrame_RoundTrips_VerbatimXmlAndGlbBytes()
    {
        const string model3dUri = "http://schemas.microsoft.com/office/drawing/2017/model3d";
        const string model3dRelType = "http://schemas.microsoft.com/office/2017/06/relationships/model3d";
        var glbBytes = new byte[] { 0x67, 0x6C, 0x54, 0x46, 0x02, 0x00 }; // glTF magic

        const string model3dXml = """
            <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <p:nvGraphicFramePr>
                <p:cNvPr id="30" name="3D Model 30"/>
                <p:cNvGraphicFramePr/>
                <p:nvPr/>
              </p:nvGraphicFramePr>
              <p:xfrm>
                <a:off x="914400" y="457200"/>
                <a:ext cx="2743200" cy="2743200"/>
              </p:xfrm>
              <a:graphic>
                <a:graphicData uri="http://schemas.microsoft.com/office/drawing/2017/model3d">
                  <am3d:model3d xmlns:am3d="http://schemas.microsoft.com/office/drawing/2017/model3d"
                                r:id="rIdGlb1"/>
                </a:graphicData>
              </a:graphic>
            </p:graphicFrame>
            """;

        var ms1 = BuildPptxWithShapeXml(model3dXml,
            extraParts: new() { ["ppt/media/model1.glb"] = (glbBytes, "model/gltf-binary") },
            extraRels: new() { ["rIdGlb1"] = (model3dRelType, "../media/model1.glb") });

        var pres1 = PptxPackageReader.Read(ms1);
        var m3d = pres1.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.Model3d);

        m3d.Should().NotBeNull("3D model graphicFrame should not be silently dropped");
        m3d!.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Model3d);
        m3d.PreservedObject.RawXml.Should().Contain(model3dUri);
        m3d.PreservedObject.Parts.Values.Should().Contain(b => b.SequenceEqual(glbBytes),
            "the GLB bytes should have been captured");

        // Canvas transforms edit the shared SlideShape geometry even when the
        // payload is a preserved modern graphic frame. Those edits must survive
        // the native XML write boundary, not just the in-memory model.
        m3d.OffsetXEmu = 1828800;
        m3d.OffsetYEmu = 914400;
        m3d.ExtentCxEmu = 3657600;
        m3d.ExtentCyEmu = 1828800;
        m3d.RotationDeg = 37.5;
        m3d.FlipH = true;
        m3d.FlipV = true;

        // Round-trip
        var ms2 = WritePptxToMemory(pres1);
        var pres2 = PptxPackageReader.Read(ms2);
        var m3d2 = pres2.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.Model3d);

        m3d2.Should().NotBeNull("3D model must survive write/re-read round-trip");
        m3d2!.PreservedObject!.Parts.Values.Should().Contain(b => b.SequenceEqual(glbBytes),
            "GLB bytes must survive round-trip");
        m3d2.OffsetXEmu.Should().Be(1828800);
        m3d2.OffsetYEmu.Should().Be(914400);
        m3d2.ExtentCxEmu.Should().Be(3657600);
        m3d2.ExtentCyEmu.Should().Be(1828800);
        m3d2.RotationDeg.Should().BeApproximately(37.5, 0.001);
        m3d2.FlipH.Should().BeTrue();
        m3d2.FlipV.Should().BeTrue();
    }

    // ── Unknown graphicFrame — no silent loss ─────────────────────────────────

    [Fact]
    public void UnknownGraphicFrameUri_IsPreserved_VerbatimAndNotDropped()
    {
        const string unknownXml = """
            <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <p:nvGraphicFramePr>
                <p:cNvPr id="40" name="Unknown 40"/>
                <p:cNvGraphicFramePr/>
                <p:nvPr/>
              </p:nvGraphicFramePr>
              <p:xfrm>
                <a:off x="0" y="0"/>
                <a:ext cx="1828800" cy="914400"/>
              </p:xfrm>
              <a:graphic>
                <a:graphicData uri="http://example.com/some/future/extension">
                  <ex:data xmlns:ex="http://example.com/some/future/extension" value="test-payload"/>
                </a:graphicData>
              </a:graphic>
            </p:graphicFrame>
            """;

        var ms1 = BuildPptxWithShapeXml(unknownXml);

        var pres1 = PptxPackageReader.Read(ms1);
        var unknown = pres1.Slides[0].Shapes
            .FirstOrDefault(s => s.Kind == SlideShapeKind.PreservedObject);

        unknown.Should().NotBeNull("unknown graphicFrame should not be silently dropped");
        unknown!.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Unknown);
        unknown.PreservedObject.RawXml.Should().Contain("test-payload",
            "the payload XML must be captured verbatim");

        // Round-trip
        var ms2 = WritePptxToMemory(pres1);
        var pres2 = PptxPackageReader.Read(ms2);
        var u2 = pres2.Slides[0].Shapes
            .FirstOrDefault(s => s.Kind == SlideShapeKind.PreservedObject);

        u2.Should().NotBeNull("unknown graphicFrame must survive round-trip");
        u2!.PreservedObject!.RawXml.Should().Contain("test-payload",
            "payload must survive write/re-read round-trip");
    }

    // ── EA1: preserved fallback image gets a content-type Default entry ──────────

    [Fact]
    public void EA1_PreservedFallbackImage_GetsContentTypeDefault()
    {
        // Bug EA1: the fallback image bytes written for a preserved object never had its file
        // extension registered in mediaExtensions → no Default entry in [Content_Types].xml → repair.
        const string unknownXml = """
            <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <p:nvGraphicFramePr>
                <p:cNvPr id="50" name="EA1Test"/>
                <p:cNvGraphicFramePr/><p:nvPr/>
              </p:nvGraphicFramePr>
              <p:xfrm><a:off x="0" y="0"/><a:ext cx="1" cy="1"/></p:xfrm>
              <a:graphic>
                <a:graphicData uri="http://example.com/test">
                  <ex:data xmlns:ex="http://example.com/test" value="ea1"/>
                </a:graphicData>
              </a:graphic>
            </p:graphicFrame>
            """;

        // Read a PPTX with this shape; the fallback image comes from the shape tree injection.
        var pres1 = PptxPackageReader.Read(BuildPptxWithShapeXml(unknownXml));

        // Manually set a PNG fallback on the shape to test EA1 (simulates what ExtractPreservedFallbackImage would set)
        var shape = pres1.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.PreservedObject);
        Assert.NotNull(shape);
        shape!.Picture = new ImagePart { Bytes = MinPng, ContentType = "image/png" };

        // Write and verify [Content_Types].xml has a Default for "png"
        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres1, ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var ctEntry = zip.GetEntry("[Content_Types].xml");
        Assert.NotNull(ctEntry);
        string ctXml;
        using (var sr = new StreamReader(ctEntry!.Open())) ctXml = sr.ReadToEnd();

        // EA1 fix: "png" Default must be present because fallback image extension is registered
        Assert.Contains("Extension=\"png\"", ctXml);
        Assert.Contains("image/png", ctXml);
    }

    // ── FA1 (was EA2): reindexed preserved part gets correct Override ────────────

    /// <summary>
    /// Builds a slide with one shape whose PreservedObject references
    /// "ppt/media/3dModel.glb" (a Model3d preserved object).
    /// </summary>
    private static Slide BuildPreservedModel3dSlide(uint shapeId, byte[] glbBytes)
    {
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id   = shapeId,
            Kind = SlideShapeKind.Model3d,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
            PreservedObject = new PreservedObjectInfo
            {
                ObjectKind          = PreservedObjectKind.Model3d,
                RawXml              = "<p:graphicFrame xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"/>",
                WasAlternateContent = false,
            },
        };
        shape.PreservedObject.Parts["ppt/media/3dModel.glb"]            = glbBytes;
        shape.PreservedObject.PartContentTypes["ppt/media/3dModel.glb"] = "model/gltf-binary";
        shape.PreservedObject.SlideRels["rId1"] =
            ("http://schemas.microsoft.com/office/2017/06/relationships/model3d", "ppt/media/3dModel.glb");
        slide.Shapes.Add(shape);
        return slide;
    }

    /// <summary>
    /// Strengthened FA1 regression test. The ORIGINAL EA2 test only asserted that the
    /// content-types XML contained the substring "preserved_2_" and "model/gltf-binary" — a
    /// weak check that passes even if the Override is emitted at a path the writer never
    /// actually wrote to the zip (which is exactly the FA1 bug: the pre-scan's reindex
    /// numbering can disagree with WriteSlidePreservedObjects' real per-slide-reset numbering).
    ///
    /// This test instead parses [Content_Types].xml properly and asserts, for EVERY Override
    /// whose PartName looks like a preserved-object media part, that a zip entry ACTUALLY EXISTS
    /// at that exact path — i.e. the Override always describes a real, written part.
    /// It also asserts the converse: every preserved-object part written to the zip has a
    /// matching Override.
    /// </summary>
    [Fact]
    public void FA1_ReindexedPreservedPart_OverrideMatchesActualWrittenZipEntry()
    {
        var pres = new Presentation();
        var glbBytes = new byte[] { 0x67, 0x6C, 0x54, 0x46 }; // glTF magic

        // Two slides, each with a shape whose preserved part resolves to the SAME OPC path.
        pres.Slides.Add(BuildPreservedModel3dSlide(1, glbBytes));
        pres.Slides.Add(BuildPreservedModel3dSlide(2, glbBytes));

        AssertContentTypesOverridesMatchWrittenPreservedParts(pres);
    }

    /// <summary>
    /// FA1: same as above but with a THIRD slide added. The old buggy pre-scan used a
    /// "remap once, then skip forever" global guard keyed only on the original path, so once the
    /// SECOND slide's occurrence claimed the sole remap dictionary entry, a THIRD (or later)
    /// slide reusing the same original path was silently ignored by the pre-scan — even though
    /// the real per-slide writer (with its fresh per-slide writtenPaths/partCounter) would still
    /// reindex or write that third occurrence. A 2-slide case can pass by coincidence; 3+ slides
    /// exposes the bug for real.
    /// </summary>
    [Fact]
    public void FA1_ThreeSlidesShareOnePath_EveryWrittenPartHasMatchingOverride()
    {
        var pres = new Presentation();
        var glbBytes = new byte[] { 0x67, 0x6C, 0x54, 0x46 };

        pres.Slides.Add(BuildPreservedModel3dSlide(1, glbBytes));
        pres.Slides.Add(BuildPreservedModel3dSlide(2, glbBytes));
        pres.Slides.Add(BuildPreservedModel3dSlide(3, glbBytes));

        AssertContentTypesOverridesMatchWrittenPreservedParts(pres);
    }

    /// <summary>
    /// FA1: two shapes on the SAME slide both reference the same original path, followed by a
    /// second slide that reuses the same original path again. Exercises the per-shape AND
    /// per-slide reset boundaries together.
    /// </summary>
    [Fact]
    public void FA1_TwoShapesOneSlideThenAnotherSlide_EveryWrittenPartHasMatchingOverride()
    {
        var pres = new Presentation();
        var glbBytes = new byte[] { 0x67, 0x6C, 0x54, 0x46 };

        var slide1 = new Slide();
        slide1.Shapes.Add(BuildPreservedModel3dSlide(1, glbBytes).Shapes[0]);
        var shape1b = BuildPreservedModel3dSlide(101, glbBytes).Shapes[0];
        slide1.Shapes.Add(shape1b);
        pres.Slides.Add(slide1);
        pres.Slides.Add(BuildPreservedModel3dSlide(2, glbBytes));

        AssertContentTypesOverridesMatchWrittenPreservedParts(pres);
    }

    /// <summary>
    /// Writes <paramref name="pres"/> and asserts: (1) every [Content_Types].xml Override whose
    /// PartName is a preserved-media path (ppt/media/preserved_*.* or ppt/media/3dModel.glb, the
    /// paths used in these tests) corresponds to an actual zip entry, and (2) every actual
    /// preserved-media zip entry has a corresponding Override with the expected content type.
    /// </summary>
    private static void AssertContentTypesOverridesMatchWrittenPreservedParts(Presentation pres)
    {
        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var ctEntry = zip.GetEntry("[Content_Types].xml");
        Assert.NotNull(ctEntry);
        XDocument ctDoc;
        using (var s = ctEntry!.Open()) ctDoc = XDocument.Load(s);

        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        bool LooksLikePreservedMediaPath(string partName) =>
            partName.Contains("/media/preserved_", StringComparison.OrdinalIgnoreCase) ||
            partName.Contains("/media/3dModel.glb", StringComparison.OrdinalIgnoreCase);

        var overridesForPreservedParts = ctDoc.Root!
            .Elements(ct + "Override")
            .Select(e => (PartName: e.Attribute("PartName")!.Value, ContentType: e.Attribute("ContentType")!.Value))
            .Where(o => LooksLikePreservedMediaPath(o.PartName))
            .ToList();

        overridesForPreservedParts.Should().NotBeEmpty(
            "at least one preserved-object media Override should have been emitted");

        // (1) Every Override for a preserved-media path must point at a REAL zip entry.
        foreach (var (partName, contentType) in overridesForPreservedParts)
        {
            var zipPath = partName.TrimStart('/');
            var entry = zip.GetEntry(zipPath);
            entry.Should().NotBeNull(
                $"[Content_Types].xml declares an Override at '{partName}' (content type " +
                $"'{contentType}') but the writer never actually wrote a zip entry there — " +
                "this is the FA1 bug: PowerPoint would show a repair prompt for the part that " +
                "WAS written (with no matching Override) while this phantom Override describes " +
                "nothing real.");
        }

        // (2) Every actually-written preserved-media zip entry must have a matching Override.
        var writtenPreservedEntries = zip.Entries
            .Where(e => LooksLikePreservedMediaPath(e.FullName))
            .Select(e => e.FullName)
            .ToList();

        writtenPreservedEntries.Should().NotBeEmpty("the writer should have written at least one preserved media part");

        var overriddenPaths = new HashSet<string>(
            overridesForPreservedParts.Select(o => o.PartName.TrimStart('/')),
            StringComparer.OrdinalIgnoreCase);

        foreach (var writtenPath in writtenPreservedEntries)
        {
            overriddenPaths.Should().Contain(writtenPath,
                $"the writer wrote a preserved-object part at '{writtenPath}' but " +
                "[Content_Types].xml has no Override for it — PowerPoint would prompt to repair.");
        }
    }

    // ── EA4: preserved-object rId patch-map collision (packed-uint key) ──────────

    /// <summary>
    /// Bug EA4: the old <c>PrvHashRelId(shapeId, oldRelId)</c> packed only the LOW 8 BITS of
    /// shapeId together with a 21-bit hash of oldRelId into a single uint key shared with the
    /// mediaById dictionary. Two preserved shapes on the SAME slide whose cNvPr ids share a low
    /// byte (5 and 261 — 261 &amp; 0xFF == 5) and which both reference the same OLD rId string
    /// (here "rId2", each to a DIFFERENT media part) collided on that packed key: the second
    /// shape's write silently overwrote the first's, so BuildPreservedObjectEl rewrote one
    /// shape's rId to the OTHER shape's media (cross-wired) — a dangling/cross-wired reference
    /// that makes PowerPoint prompt to repair. The fix keys the patch map directly on the real
    /// (uint shapeId, string oldRelId) tuple, so no collision is possible.
    ///
    /// This test builds two Model3d preserved shapes (ids 5 and 261) each with RawXml containing
    /// an r:id="rId2" attribute and a SlideRels["rId2"] entry pointing at its OWN distinct glb
    /// part, writes the presentation, and asserts that after write, shape 5's rewritten r:id
    /// resolves (via the slide .rels) to shape 5's own glb bytes and shape 261's resolves to its
    /// own glb bytes — not cross-wired.
    /// </summary>
    [Fact]
    public void EA4_TwoPreservedShapesSharingLowByteIdAndOldRid_DoNotCrossWireRelIds()
    {
        const string model3dRelType = "http://schemas.microsoft.com/office/2017/06/relationships/model3d";

        var glbBytesShape5   = new byte[] { 0x67, 0x6C, 0x54, 0x46, 0x01 }; // glTF magic + marker byte
        var glbBytesShape261 = new byte[] { 0x67, 0x6C, 0x54, 0x46, 0x02 }; // glTF magic + different marker

        static SlideShape BuildShape(uint shapeId, string oldRelId, string glbPath, byte[] glbBytes)
        {
            var info = new PreservedObjectInfo
            {
                ObjectKind          = PreservedObjectKind.Model3d,
                RawXml              =
                    $"<p:graphicFrame xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" " +
                    $"xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                    $"xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    $"<p:nvGraphicFramePr><p:cNvPr id=\"{shapeId}\" name=\"Model{shapeId}\"/>" +
                    $"<p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr>" +
                    $"<p:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"914400\" cy=\"914400\"/></p:xfrm>" +
                    $"<a:graphic><a:graphicData uri=\"http://schemas.microsoft.com/office/drawing/2017/model3d\">" +
                    $"<am3d:model3d xmlns:am3d=\"http://schemas.microsoft.com/office/drawing/2017/model3d\" " +
                    $"r:id=\"{oldRelId}\"/></a:graphicData></a:graphic></p:graphicFrame>",
                WasAlternateContent = false,
            };
            info.Parts[glbPath]            = glbBytes;
            info.PartContentTypes[glbPath] = "model/gltf-binary";
            info.SlideRels[oldRelId]       = (model3dRelType, glbPath);

            return new SlideShape
            {
                Id              = shapeId,
                Kind            = SlideShapeKind.Model3d,
                ExtentCxEmu     = 914400,
                ExtentCyEmu     = 914400,
                PreservedObject = info,
            };
        }

        var slide = new Slide();
        var shape5   = BuildShape(5,   "rId2", "ppt/media/model5.glb",   glbBytesShape5);
        var shape261 = BuildShape(261, "rId2", "ppt/media/model261.glb", glbBytesShape261);
        slide.Shapes.Add(shape5);
        slide.Shapes.Add(shape261);

        var pres = new Presentation();
        pres.Slides.Add(slide);

        using var ms = WritePptxToMemory(pres);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);

        // Load slide1.xml and slide1.xml.rels
        var slideEntry = zip.GetEntry("ppt/slides/slide1.xml");
        Assert.NotNull(slideEntry);
        XDocument slideDoc;
        using (var s = slideEntry!.Open()) slideDoc = XDocument.Load(s);

        var relsEntry = zip.GetEntry("ppt/slides/_rels/slide1.xml.rels");
        Assert.NotNull(relsEntry);
        XDocument relsDoc;
        using (var s = relsEntry!.Open()) relsDoc = XDocument.Load(s);

        XNamespace pkgRelsNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relTargetById = relsDoc.Root!.Elements(pkgRelsNs + "Relationship")
            .ToDictionary(
                e => e.Attribute("Id")!.Value,
                e => e.Attribute("Target")!.Value);

        // Find the two am3d:model3d elements (one per cNvPr id) and read their (rewritten) r:id.
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";

        string GetRewrittenRelIdFor(uint shapeId)
        {
            var gf = slideDoc.Descendants(p + "graphicFrame")
                .FirstOrDefault(g => g.Descendants(p + "cNvPr")
                    .Any(c => c.Attribute("id")?.Value == shapeId.ToString()));
            Assert.NotNull(gf);
            var idAttr = gf!.Descendants()
                .SelectMany(e => e.Attributes())
                .FirstOrDefault(a => a.Name.NamespaceName == r.NamespaceName);
            Assert.NotNull(idAttr);
            return idAttr!.Value;
        }

        var relId5   = GetRewrittenRelIdFor(5);
        var relId261 = GetRewrittenRelIdFor(261);

        // The two shapes must have been allocated DIFFERENT fresh rIds (if the collision bug were
        // present, the underlying map corruption could manifest in various ways, but the key
        // observable symptom is that each rewritten rId must resolve to the shape's OWN media).
        Assert.True(relTargetById.ContainsKey(relId5), $"rewritten rId '{relId5}' for shape 5 must exist in slide rels");
        Assert.True(relTargetById.ContainsKey(relId261), $"rewritten rId '{relId261}' for shape 261 must exist in slide rels");

        var target5   = relTargetById[relId5];
        var target261 = relTargetById[relId261];

        target5.Should().Contain("model5.glb", "shape 5's rewritten rId must resolve to shape 5's OWN media, not shape 261's");
        target261.Should().Contain("model261.glb", "shape 261's rewritten rId must resolve to shape 261's OWN media, not shape 5's");

        // Cross-wiring guard: neither shape's target should point at the OTHER shape's media.
        target5.Should().NotContain("model261.glb", "shape 5 must not be cross-wired to shape 261's media");
        target261.Should().NotContain("model5.glb", "shape 261 must not be cross-wired to shape 5's media");

        // Also verify the actual bytes at the resolved zip paths are the shape's own bytes.
        string ResolveZipPath(string relTarget) =>
            relTarget.TrimStart('.', '/') is var t && t.StartsWith("media/") ? $"ppt/{t}" : t;

        var entry5   = zip.GetEntry(ResolveZipPath(target5));
        var entry261 = zip.GetEntry(ResolveZipPath(target261));
        Assert.NotNull(entry5);
        Assert.NotNull(entry261);

        byte[] ReadAll(ZipArchiveEntry e)
        {
            using var es = e.Open();
            using var msOut = new MemoryStream();
            es.CopyTo(msOut);
            return msOut.ToArray();
        }

        ReadAll(entry5!).Should().BeEquivalentTo(glbBytesShape5, "shape 5's media bytes must be its own, not shape 261's");
        ReadAll(entry261!).Should().BeEquivalentTo(glbBytesShape261, "shape 261's media bytes must be its own, not shape 5's");
    }

    // ── EA3: mc:AlternateContent Requires token round-trips verbatim ──────────────

    [Fact]
    public void EA3_McRequiresToken_RoundTrips_Verbatim()
    {
        // Bug EA3: the preserved-object mc:AlternateContent re-wrap hardcoded Requires="p14",
        // even when the original used "p15", "p159", or another prefix. This test verifies the
        // token is captured on read and re-emitted verbatim on write.
        const string shapeXml = """
            <mc:AlternateContent
                xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                xmlns:p159="http://schemas.microsoft.com/office/powerpoint/2015/09/main">
              <mc:Choice Requires="p159" xmlns:p159="http://schemas.microsoft.com/office/powerpoint/2015/09/main">
                <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:nvGraphicFramePr>
                    <p:cNvPr id="60" name="EA3Test"/><p:cNvGraphicFramePr/><p:nvPr/>
                  </p:nvGraphicFramePr>
                  <p:xfrm><a:off x="0" y="0"/><a:ext cx="1" cy="1"/></p:xfrm>
                  <a:graphic>
                    <a:graphicData uri="http://example.com/test">
                      <ex:data xmlns:ex="http://example.com/test" value="ea3"/>
                    </a:graphicData>
                  </a:graphic>
                </p:graphicFrame>
              </mc:Choice>
              <mc:Fallback>
                <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:nvGraphicFramePr>
                    <p:cNvPr id="60" name="EA3Test"/><p:cNvGraphicFramePr/><p:nvPr/>
                  </p:nvGraphicFramePr>
                  <p:xfrm><a:off x="0" y="0"/><a:ext cx="1" cy="1"/></p:xfrm>
                  <a:graphic>
                    <a:graphicData uri="http://example.com/test"/>
                  </a:graphic>
                </p:graphicFrame>
              </mc:Fallback>
            </mc:AlternateContent>
            """;

        var ms1 = BuildPptxWithShapeXml(shapeXml);
        var pres1 = PptxPackageReader.Read(ms1);

        var shape = pres1.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.PreservedObject);
        Assert.NotNull(shape);

        // EA3: the token must be captured on read
        Assert.True(shape!.PreservedObject!.WasAlternateContent, "WasAlternateContent must be set");
        Assert.Equal("p159", shape.PreservedObject.McRequiresToken); // EA3 fix: captured

        // Write and re-read
        var ms2 = WritePptxToMemory(pres1);
        ms2.Position = 0;

        // Verify the re-emitted XML has Requires="p159", not "p14"
        using var zip = new ZipArchive(ms2, ZipArchiveMode.Read, leaveOpen: true);
        var slideEntry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
        Assert.NotNull(slideEntry);
        string slideXml;
        using (var sr = new StreamReader(slideEntry!.Open())) slideXml = sr.ReadToEnd();

        // EA3 fix: must contain the ORIGINAL Requires token "p159", not the hardcoded "p14"
        Assert.Contains("Requires=\"p159\"", slideXml);
        // EA3 fix: the prefix "p14" must NOT appear as the Requires token
        Assert.DoesNotContain("Requires=\"p14\"", slideXml);
    }

    // ── FA2: multi-token mc:Choice Requires must not throw / must produce valid xmlns ──────

    /// <summary>
    /// Bug FA2: mc:AlternateContent permits Requires to be a SPACE-SEPARATED list of prefixes
    /// (e.g. Requires="p14 p15"). The old writer did
    /// `new XAttribute(XNamespace.Xmlns + requiresToken, requiresNsUri)` using the RAW (possibly
    /// multi-token) string as the xmlns LOCAL NAME — an xmlns local-name containing a space is
    /// not a legal XML name, so XName/XAttribute construction throws XmlException, and the
    /// ENTIRE save fails for any preserved object whose original wrapper used a multi-token
    /// Requires. This test asserts the save no longer throws, and that Requires plus BOTH
    /// per-token xmlns declarations are preserved on round-trip.
    /// </summary>
    [Fact]
    public void FA2_MultiTokenRequires_WritesWithoutThrowing_AndRoundTripsBothXmlns()
    {
        const string p14Uri  = "http://schemas.microsoft.com/office/powerpoint/2010/main";
        const string p15Uri  = "http://schemas.microsoft.com/office/powerpoint/2012/main";
        const string shapeXml = """
            <mc:AlternateContent
                xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                xmlns:p14="http://schemas.microsoft.com/office/powerpoint/2010/main"
                xmlns:p15="http://schemas.microsoft.com/office/powerpoint/2012/main">
              <mc:Choice Requires="p14 p15"
                         xmlns:p14="http://schemas.microsoft.com/office/powerpoint/2010/main"
                         xmlns:p15="http://schemas.microsoft.com/office/powerpoint/2012/main">
                <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:nvGraphicFramePr>
                    <p:cNvPr id="70" name="FA2Test"/><p:cNvGraphicFramePr/><p:nvPr/>
                  </p:nvGraphicFramePr>
                  <p:xfrm><a:off x="0" y="0"/><a:ext cx="1" cy="1"/></p:xfrm>
                  <a:graphic>
                    <a:graphicData uri="http://example.com/test">
                      <ex:data xmlns:ex="http://example.com/test" value="fa2"/>
                    </a:graphicData>
                  </a:graphic>
                </p:graphicFrame>
              </mc:Choice>
              <mc:Fallback>
                <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:nvGraphicFramePr>
                    <p:cNvPr id="70" name="FA2Test"/><p:cNvGraphicFramePr/><p:nvPr/>
                  </p:nvGraphicFramePr>
                  <p:xfrm><a:off x="0" y="0"/><a:ext cx="1" cy="1"/></p:xfrm>
                  <a:graphic>
                    <a:graphicData uri="http://example.com/test"/>
                  </a:graphic>
                </p:graphicFrame>
              </mc:Fallback>
            </mc:AlternateContent>
            """;

        var ms1 = BuildPptxWithShapeXml(shapeXml);
        var pres1 = PptxPackageReader.Read(ms1);

        var shape = pres1.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.PreservedObject);
        shape.Should().NotBeNull();
        shape!.PreservedObject!.WasAlternateContent.Should().BeTrue();
        shape.PreservedObject.McRequiresToken.Should().Be("p14 p15",
            "the raw multi-token Requires value must be captured verbatim");

        // FA2: both tokens' namespace URIs must have been resolved individually on read.
        shape.PreservedObject.McRequiresNsUris.Should().ContainKey("p14")
            .WhoseValue.Should().Be(p14Uri);
        shape.PreservedObject.McRequiresNsUris.Should().ContainKey("p15")
            .WhoseValue.Should().Be(p15Uri);

        // FA2 core assertion: writing must NOT throw (the old code threw XmlException here
        // because it tried to declare an xmlns named "p14 p15", which is not a legal XML name).
        MemoryStream ms2 = null!;
        var writeAction = () => ms2 = WritePptxToMemory(pres1);
        writeAction.Should().NotThrow("a multi-token Requires must not crash the save");

        ms2.Position = 0;
        using var zip = new ZipArchive(ms2, ZipArchiveMode.Read, leaveOpen: true);
        var slideEntry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
        slideEntry.Should().NotBeNull();
        string slideXml;
        using (var sr = new StreamReader(slideEntry!.Open())) slideXml = sr.ReadToEnd();

        // The XML must be well-formed (XDocument.Parse throws on malformed XML / illegal names).
        XDocument slideDoc = null!;
        var parseAction = () => slideDoc = XDocument.Parse(slideXml);
        parseAction.Should().NotThrow("the re-emitted slide XML must be well-formed");

        // Requires attribute preserved verbatim, and BOTH xmlns declarations present.
        slideXml.Should().Contain("Requires=\"p14 p15\"");
        slideXml.Should().Contain(p14Uri);
        slideXml.Should().Contain(p15Uri);

        // Both xmlns prefixes must actually be declared as xmlns attributes (not just appear as
        // a substring somewhere) — find the mc:Choice element and check its in-scope namespaces.
        XNamespace mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        var choiceEl = slideDoc!.Descendants(mc + "Choice").FirstOrDefault();
        choiceEl.Should().NotBeNull("mc:Choice must be re-emitted");
        choiceEl!.GetNamespaceOfPrefix("p14")?.NamespaceName.Should().Be(p14Uri);
        choiceEl.GetNamespaceOfPrefix("p15")?.NamespaceName.Should().Be(p15Uri);
    }

    /// <summary>
    /// FA2: when a Requires token's namespace URI is UNKNOWN (not resolvable from the source
    /// document and not one of the well-known MS prefixes), the writer must NOT force the p14
    /// URI onto it — that would be a wrong/misleading binding. This test uses a fabricated
    /// unknown prefix ("zzUnknown") with no xmlns declared anywhere in scope, so
    /// McRequiresNsUris will be empty; the writer should fall back to preserving the element
    /// verbatim (un-wrapped) rather than emit a broken/incorrect AlternateContent.
    /// </summary>
    [Fact]
    public void FA2_UnknownRequiresToken_DoesNotForcePrefixToP14Uri()
    {
        const string shapeXml = """
            <mc:AlternateContent
                xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006">
              <mc:Choice Requires="zzUnknown">
                <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:nvGraphicFramePr>
                    <p:cNvPr id="71" name="FA2UnknownTest"/><p:cNvGraphicFramePr/><p:nvPr/>
                  </p:nvGraphicFramePr>
                  <p:xfrm><a:off x="0" y="0"/><a:ext cx="1" cy="1"/></p:xfrm>
                  <a:graphic>
                    <a:graphicData uri="http://example.com/test">
                      <ex:data xmlns:ex="http://example.com/test" value="fa2unknown"/>
                    </a:graphicData>
                  </a:graphic>
                </p:graphicFrame>
              </mc:Choice>
              <mc:Fallback>
                <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <p:nvGraphicFramePr>
                    <p:cNvPr id="71" name="FA2UnknownTest"/><p:cNvGraphicFramePr/><p:nvPr/>
                  </p:nvGraphicFramePr>
                  <p:xfrm><a:off x="0" y="0"/><a:ext cx="1" cy="1"/></p:xfrm>
                  <a:graphic>
                    <a:graphicData uri="http://example.com/test"/>
                  </a:graphic>
                </p:graphicFrame>
              </mc:Fallback>
            </mc:AlternateContent>
            """;

        var ms1 = BuildPptxWithShapeXml(shapeXml);
        var pres1 = PptxPackageReader.Read(ms1);

        var shape = pres1.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.PreservedObject);
        shape.Should().NotBeNull();
        shape!.PreservedObject!.McRequiresToken.Should().Be("zzUnknown");
        shape.PreservedObject.McRequiresNsUris.Should().NotContainKey("zzUnknown",
            "an unresolvable prefix must not get a guessed URI on read");

        Action writeAction = () =>
        {
            using var ms2 = WritePptxToMemory(pres1);
            ms2.Position = 0;
            using var zip = new ZipArchive(ms2, ZipArchiveMode.Read, leaveOpen: true);
            var slideEntry = zip.Entries.First(e =>
                e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
            string slideXml;
            using (var sr = new StreamReader(slideEntry.Open())) slideXml = sr.ReadToEnd();

            // Must not crash and must not fabricate the p14 URI for the "zzUnknown" prefix.
            var p14Uri = "http://schemas.microsoft.com/office/powerpoint/2010/main";
            if (slideXml.Contains("zzUnknown"))
                slideXml.Should().NotContain($"xmlns:zzUnknown=\"{p14Uri}\"");
        };
        writeAction.Should().NotThrow("an unknown Requires token must not crash the save");
    }

    // ── EA4: preserved-object rId patch-map collision ─────────────────────────

    /// <summary>
    /// Bug EA4: the writer packed (shapeId, oldRelId) into a single uint key using only the LOW 8
    /// BITS of shapeId plus a 21-bit hash of oldRelId. Two preserved shapes on the SAME SLIDE whose
    /// cNvPr ids share a low byte (5 and 261 -&gt; 261 &amp; 0xFF == 5) and which both reference the
    /// same OLD rId string internally ("rId2", each to a DIFFERENT media part) collide on that
    /// packed key — the second shape's write overwrites the first's, so one shape's rId gets
    /// patched to the wrong (or a stale) new rId, cross-wiring its media reference. This test builds
    /// exactly that scenario and asserts each shape resolves to its OWN media after round-trip.
    /// </summary>
    [Fact]
    public void EA4_TwoPreservedShapesSharingLowByteAndOldRid_DoNotCrossWireMedia()
    {
        const string unknownUri = "http://example.com/ea4/test";
        const string relType = "http://example.com/ea4/rel";

        // Shape A: cNvPr id = 5, references local "rId2" -> media part A.
        string ShapeXml(uint id, string label) => $"""
            <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <p:nvGraphicFramePr>
                <p:cNvPr id="{id}" name="EA4-{label}"/>
                <p:cNvGraphicFramePr/>
                <p:nvPr/>
              </p:nvGraphicFramePr>
              <p:xfrm>
                <a:off x="0" y="0"/>
                <a:ext cx="914400" cy="914400"/>
              </p:xfrm>
              <a:graphic>
                <a:graphicData uri="{unknownUri}">
                  <ex:data xmlns:ex="{unknownUri}" r:id="rId2"/>
                </a:graphicData>
              </a:graphic>
            </p:graphicFrame>
            """;

        var mediaA = new byte[] { 0xAA, 0x01 };
        var mediaB = new byte[] { 0xBB, 0x02, 0x02 };

        var ms1 = BuildPptxWithTwoShapesSharingOldRid(
            ShapeXml(5, "A"), ShapeXml(261, "B"),
            relType,
            mediaAPath: "ppt/media/ea4MediaA.bin", mediaABytes: mediaA,
            mediaBPath: "ppt/media/ea4MediaB.bin", mediaBBytes: mediaB);

        var pres1 = PptxPackageReader.Read(ms1);
        var slide1 = pres1.Slides[0];
        var shapeA1 = slide1.Shapes.First(s => s.Id == 5);
        var shapeB1 = slide1.Shapes.First(s => s.Id == 261);
        shapeA1.PreservedObject.Should().NotBeNull();
        shapeB1.PreservedObject.Should().NotBeNull();

        // Write, then re-read the output package to verify no cross-wiring.
        var ms2 = WritePptxToMemory(pres1);
        ms2.Position = 0;

        using var zip = new ZipArchive(ms2, ZipArchiveMode.Read, leaveOpen: true);
        var slideEntry = zip.Entries.First(e =>
            e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
        string slideXml;
        using (var sr = new StreamReader(slideEntry.Open())) slideXml = sr.ReadToEnd();
        var slideDoc = XDocument.Parse(slideXml);

        var slideRelsEntry = zip.Entries.First(e =>
            e.FullName.StartsWith("ppt/slides/_rels/slide") && e.FullName.EndsWith(".rels"));
        string slideRelsXml;
        using (var sr = new StreamReader(slideRelsEntry.Open())) slideRelsXml = sr.ReadToEnd();
        var relsDoc = XDocument.Parse(slideRelsXml);

        XNamespace rNs  = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace relsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        string ResolveMediaBytesForShape(uint shapeId)
        {
            var cNvPr = slideDoc.Descendants().First(e =>
                e.Name.LocalName == "cNvPr" && e.Attribute("id")?.Value == shapeId.ToString());
            var exData = cNvPr.Parent!.Parent!.Descendants().First(e => e.Name.LocalName == "data");
            var newRid = exData.Attribute(rNs + "id")!.Value;

            var relEl = relsDoc.Root!.Elements(relsNs + "Relationship")
                .First(e => e.Attribute("Id")!.Value == newRid);
            var target = relEl.Attribute("Target")!.Value;
            var targetPath = target.TrimStart('.', '/');
            targetPath = "ppt/" + targetPath[(targetPath.IndexOf("media/", StringComparison.Ordinal))..];

            var entry = zip.GetEntry(targetPath);
            entry.Should().NotBeNull($"resolved media part '{targetPath}' for shape {shapeId} must exist in the output zip");
            using var es = entry!.Open();
            using var msOut = new MemoryStream();
            es.CopyTo(msOut);
            return Convert.ToBase64String(msOut.ToArray());
        }

        var bytesForA = ResolveMediaBytesForShape(5);
        var bytesForB = ResolveMediaBytesForShape(261);

        bytesForA.Should().Be(Convert.ToBase64String(mediaA),
            "shape 5 (cNvPr id 5) must resolve to its OWN media part A, not shape 261's media");
        bytesForB.Should().Be(Convert.ToBase64String(mediaB),
            "shape 261 (cNvPr id 261, 261 & 0xFF == 5) must resolve to its OWN media part B, " +
            "not shape 5's media — this is the EA4 cross-wiring bug when it regresses");
    }

    /// <summary>
    /// Builds a minimal PPTX whose slide has TWO preserved (PreservedObject) shapes, cNvPr ids 5
    /// and 261 (261 &amp; 0xFF == 5, the EA4 collision precondition), each with
    /// <see cref="PreservedObjectInfo.SlideRels"/> keyed on the literal OLD id string "rId2" but
    /// pointing at its OWN distinct media part.
    ///
    /// Real OPC .rels files are flat per-part and cannot contain two relationships both literally
    /// named "rId2" resolving to different targets, so this scenario cannot be expressed as a single
    /// hand-rolled slide.xml.rels — it arises in practice from PreservedObjectInfo.SlideRels being
    /// captured PER-SHAPE (each shape's own "rId2" usage is captured independently by
    /// CaptureReferencedParts) whenever two DIFFERENT shapes on a slide each happen to reference an
    /// rId with the same string value. To reproduce that deterministically and directly test the
    /// writer's patch-map keying (the actual site of the EA4 bug), this helper constructs the two
    /// SlideShape/PreservedObjectInfo objects directly in-memory (bypassing the XML reader) with the
    /// colliding old-rid string "rId2" set explicitly on both, then runs them through the real
    /// writer.
    /// </summary>
    private static MemoryStream BuildPptxWithTwoShapesSharingOldRid(
        string shapeAXml, string shapeBXml,
        string relType,
        string mediaAPath, byte[] mediaABytes,
        string mediaBPath, byte[] mediaBBytes)
    {
        // Build directly via the in-memory model + writer, then re-read, so both shapes'
        // PreservedObject.SlideRels literally key on the OLD id string "rId2" for both shapes —
        // reproducing the exact writer-side collision precondition without fighting OPC's
        // one-rId-one-target invariant in a hand-rolled .rels file.
        var pres = new Presentation();
        var slide = new Slide();

        SlideShape MakeShape(uint id, string rawXml, string mediaPath, byte[] mediaBytes)
        {
            var info = new PreservedObjectInfo
            {
                ObjectKind = PreservedObjectKind.Unknown,
                RawXml     = rawXml,
            };
            info.Parts[mediaPath] = mediaBytes;
            info.PartContentTypes[mediaPath] = "application/octet-stream";
            // Both shapes capture the SAME old rId string "rId2" pointing at their OWN distinct
            // media part — this is the exact EA4 collision precondition.
            info.SlideRels["rId2"] = (relType, mediaPath);

            return new SlideShape
            {
                Id              = id,
                Kind            = SlideShapeKind.PreservedObject,
                ExtentCxEmu     = 914400,
                ExtentCyEmu     = 914400,
                PreservedObject = info,
            };
        }

        slide.Shapes.Add(MakeShape(5, shapeAXml, mediaAPath, mediaABytes));
        slide.Shapes.Add(MakeShape(261, shapeBXml, mediaBPath, mediaBBytes));
        pres.Slides.Add(slide);

        return WritePptxToMemory(pres);
    }

    // ── EA5: preserved-part capture is transitive (no dangling .rels targets) ────

    /// <summary>
    /// Bug EA5: the reader captured a preserved part's bytes plus its OWN .rels bytes, but never
    /// followed the relationship targets DECLARED INSIDE that .rels to capture the further parts
    /// they reference. A 3D-model part (e.g. "ppt/model3d/model1.glb") commonly has its own .rels
    /// pointing at a secondary part such as an embedded texture — the old code left that texture
    /// uncaptured, yet the writer still re-emitted the model's .rels VERBATIM (still referencing
    /// the texture's original path) — so the OUTPUT package had a relationship pointing at a part
    /// that was never written, which PowerPoint reports as needing repair.
    ///
    /// This test builds a preserved Model3d graphicFrame referencing "ppt/model3d/model1.glb",
    /// gives that part its OWN .rels with a relationship to a secondary texture part
    /// ("ppt/model3d/textures/texture1.png"), reads it, writes it back out, and asserts:
    ///  1. the texture part exists in the output zip,
    ///  2. its content type is declared (either a Default extension entry or an Override),
    ///  3. the model part's re-emitted .rels still resolves to a target that actually exists in
    ///     the output zip (no dangling reference).
    /// </summary>
    [Fact]
    public void EA5_TransitivePartCapture_TextureReferencedByModelRels_IsCapturedAndWritten_NoDanglingRefs()
    {
        const string model3dRelType = "http://schemas.microsoft.com/office/2017/06/relationships/model3d";
        const string textureRelType = "http://schemas.microsoft.com/office/2017/06/relationships/model3dTexture";

        const string modelPartPath   = "ppt/model3d/model1.glb";
        const string texturePartPath = "ppt/model3d/textures/texture1.png";

        var glbBytes     = new byte[] { 0x67, 0x6C, 0x54, 0x46, 0x02, 0x00 }; // glTF magic
        var textureBytes = MinPng;

        const string model3dXml = """
            <p:graphicFrame xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                            xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <p:nvGraphicFramePr>
                <p:cNvPr id="80" name="EA5 3D Model"/>
                <p:cNvGraphicFramePr/>
                <p:nvPr/>
              </p:nvGraphicFramePr>
              <p:xfrm>
                <a:off x="914400" y="457200"/>
                <a:ext cx="2743200" cy="2743200"/>
              </p:xfrm>
              <a:graphic>
                <a:graphicData uri="http://schemas.microsoft.com/office/drawing/2017/model3d">
                  <am3d:model3d xmlns:am3d="http://schemas.microsoft.com/office/drawing/2017/model3d"
                                r:id="rIdModel1"/>
                </a:graphicData>
              </a:graphic>
            </p:graphicFrame>
            """;

        var ms1 = BuildPptxWithShapeXml(model3dXml,
            extraParts: new()
            {
                [modelPartPath] = (glbBytes, "model/gltf-binary"),
            },
            extraRels: new()
            {
                ["rIdModel1"] = (model3dRelType, "../model3d/model1.glb"),
            });

        // Inject the model part's OWN .rels (pointing at the secondary texture part) and the
        // texture bytes themselves — BuildPptxWithShapeXml only wires up the SLIDE's rels, so we
        // add the model-part-level .rels and the texture part directly here.
        using (var zip = new ZipArchive(ms1, ZipArchiveMode.Update, leaveOpen: true))
        {
            var textureEntry = zip.CreateEntry(texturePartPath, CompressionLevel.Optimal);
            using (var s = textureEntry.Open()) s.Write(textureBytes, 0, textureBytes.Length);

            const string modelRelsPath = "ppt/model3d/_rels/model1.glb.rels";
            var modelRelsDoc = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships") + "Relationships",
                    new XElement(XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships") + "Relationship",
                        new XAttribute("Id", "rIdTexture1"),
                        new XAttribute("Type", textureRelType),
                        new XAttribute("Target", "textures/texture1.png"))));
            var modelRelsEntry = zip.CreateEntry(modelRelsPath, CompressionLevel.Optimal);
            using (var sw = new StreamWriter(modelRelsEntry.Open())) modelRelsDoc.Save(sw);
        }
        ms1.Position = 0;

        // Read
        var pres1 = PptxPackageReader.Read(ms1);
        var m3d = pres1.Slides[0].Shapes.FirstOrDefault(s => s.Kind == SlideShapeKind.Model3d);
        m3d.Should().NotBeNull("the 3D model graphicFrame should not be silently dropped");
        m3d!.PreservedObject.Should().NotBeNull();

        // EA5 core assertion (read side): the texture part referenced by the model's OWN .rels
        // must have been captured transitively, not just the model part itself.
        m3d.PreservedObject!.Parts.Should().ContainKey(texturePartPath,
            "EA5: the texture part referenced by the model part's own .rels must be captured transitively");
        m3d.PreservedObject.Parts[texturePartPath].Should().BeEquivalentTo(textureBytes,
            "the captured texture bytes must match the original texture part");
        m3d.PreservedObject.PartContentTypes.Should().ContainKey(texturePartPath,
            "a content type must be recorded for the transitively captured texture part");

        // Write back out
        var ms2 = WritePptxToMemory(pres1);
        using var zip2 = new ZipArchive(ms2, ZipArchiveMode.Read, leaveOpen: true);

        // (1) The texture part must exist somewhere in the output zip (path may have been
        // reindexed if it collided with an existing part, but it must exist SOMEWHERE).
        var writtenModelEntry = zip2.Entries.FirstOrDefault(e =>
            e.FullName.Equals(modelPartPath, StringComparison.OrdinalIgnoreCase) ||
            e.FullName.Contains("preserved_", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".glb"));
        writtenModelEntry.Should().NotBeNull("the model part itself must be written to the output zip");

        // Find the model part's own .rels in the output (at whatever path the model part landed).
        var modelRelsOutEntry = zip2.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".rels") && e.FullName.Contains("model", StringComparison.OrdinalIgnoreCase));
        modelRelsOutEntry.Should().NotBeNull("the model part's .rels must be re-emitted");

        XDocument modelRelsOutDoc;
        using (var s = modelRelsOutEntry!.Open()) modelRelsOutDoc = XDocument.Load(s);
        XNamespace relsNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var textureRel = modelRelsOutDoc.Root!.Elements(relsNs + "Relationship")
            .FirstOrDefault(e => e.Attribute("Type")?.Value == textureRelType);
        textureRel.Should().NotBeNull("the model's re-emitted .rels must still declare the texture relationship");

        // (2)+(3) Resolve the texture relationship's target relative to the model part's directory
        // and assert it exists in the output zip (no dangling reference) — the whole point of EA5.
        var modelDirInOutput = writtenModelEntry!.FullName.Contains('/')
            ? writtenModelEntry.FullName[..writtenModelEntry.FullName.LastIndexOf('/')]
            : string.Empty;
        var textureTarget = textureRel!.Attribute("Target")!.Value;

        string ResolveRelative(string baseDir, string target)
        {
            var combined = string.IsNullOrEmpty(baseDir) ? target : $"{baseDir}/{target}";
            var segments = combined.Split('/');
            var stack = new List<string>();
            foreach (var seg in segments)
            {
                if (seg is "" or ".") continue;
                if (seg == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }
                stack.Add(seg);
            }
            return string.Join("/", stack);
        }

        var resolvedTexturePath = ResolveRelative(modelDirInOutput, textureTarget);
        var textureOutEntry = zip2.GetEntry(resolvedTexturePath);
        textureOutEntry.Should().NotBeNull(
            $"EA5: the model's .rels declares a relationship to '{textureTarget}' (resolved to " +
            $"'{resolvedTexturePath}') but no such part exists in the output zip — this is exactly " +
            "the dangling-reference bug that makes PowerPoint prompt to repair.");

        byte[] textureOutBytes;
        using (var es = textureOutEntry!.Open())
        using (var msOut = new MemoryStream())
        {
            es.CopyTo(msOut);
            textureOutBytes = msOut.ToArray();
        }
        textureOutBytes.Should().BeEquivalentTo(textureBytes, "the texture bytes must survive the round-trip");

        // (2) Content type must be declared for the texture part's actual written path (Default
        // extension entry OR an Override) — otherwise PowerPoint still can't load it correctly.
        var ctEntry = zip2.GetEntry("[Content_Types].xml");
        ctEntry.Should().NotBeNull();
        XDocument ctDoc;
        using (var s = ctEntry!.Open()) ctDoc = XDocument.Load(s);
        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";

        var textureExt = resolvedTexturePath.Contains('.')
            ? resolvedTexturePath[(resolvedTexturePath.LastIndexOf('.') + 1)..]
            : string.Empty;
        var hasDefaultForExt = ctDoc.Root!.Elements(ct + "Default")
            .Any(e => string.Equals(e.Attribute("Extension")?.Value, textureExt, StringComparison.OrdinalIgnoreCase));
        var hasOverrideForPath = ctDoc.Root!.Elements(ct + "Override")
            .Any(e => (e.Attribute("PartName")?.Value ?? "").TrimStart('/')
                .Equals(resolvedTexturePath, StringComparison.OrdinalIgnoreCase));

        (hasDefaultForExt || hasOverrideForPath).Should().BeTrue(
            $"the texture part at '{resolvedTexturePath}' must have a declared content type " +
            "(Default extension entry or Override) in [Content_Types].xml");
    }

    // ── SlideCloner preserves modern object ───────────────────────────────────

    [Fact]
    public void SlideCloner_ClonesPreservedObject_CorrectlySharedBytes()
    {
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id  = 7,
            Kind = SlideShapeKind.Zoom,
            ExtentCxEmu = 1000000,
            ExtentCyEmu = 500000,
            PreservedObject = new PreservedObjectInfo
            {
                ObjectKind          = PreservedObjectKind.Zoom,
                RawXml              = "<p:graphicFrame/>",
                WasAlternateContent = true,
            },
            Picture = new ImagePart { Bytes = MinPng, ContentType = "image/png" },
        };
        shape.PreservedObject.Parts["ppt/media/img.png"]            = MinPng;
        shape.PreservedObject.PartContentTypes["ppt/media/img.png"] = "image/png";
        shape.PreservedObject.SlideRels["rId1"] = ("reltype", "ppt/media/img.png");
        slide.Shapes.Add(shape);

        var clone  = SlideCloner.CloneSlide(slide);
        var cs     = clone.Shapes[0];

        cs.Kind.Should().Be(SlideShapeKind.Zoom);
        cs.PreservedObject.Should().NotBeNull();
        cs.PreservedObject!.ObjectKind.Should().Be(PreservedObjectKind.Zoom);
        cs.PreservedObject.WasAlternateContent.Should().BeTrue();
        cs.PreservedObject.Parts.Should().ContainKey("ppt/media/img.png");
        cs.PreservedObject.SlideRels["rId1"].TargetPath.Should().Be("ppt/media/img.png");
        cs.Picture.Should().NotBeNull();
    }

    // ── Compositor renders fallback picture for modern objects ────────────────

    [Fact]
    public void Compositor_PreservedObject_WithPreviewImage_ProducesPictureOp()
    {
        var pres  = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = 8,
            Kind        = SlideShapeKind.Model3d,
            OffsetXEmu  = 457200,
            OffsetYEmu  = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800,
            PreservedObject = new PreservedObjectInfo { ObjectKind = PreservedObjectKind.Model3d },
            Picture     = new ImagePart { Bytes = MinPng, ContentType = "image/png" },
        });
        pres.Slides.Add(slide);

        var ops = SlideCompositor.Compose(pres, slide);

        ops.OfType<DrawOp.Picture>().Should().HaveCount(1,
            "a preserved object with a preview image should emit one DrawOp.Picture");
        ops.OfType<DrawOp.Picture>().First().Bytes.Should().BeEquivalentTo(MinPng);
    }

    [Fact]
    public void Compositor_PreservedObject_WithoutPreviewImage_ProducesShapeOp()
    {
        var pres  = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id          = 9,
            Kind        = SlideShapeKind.Zoom,
            OffsetXEmu  = 457200,
            OffsetYEmu  = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800,
            PreservedObject = new PreservedObjectInfo { ObjectKind = PreservedObjectKind.Zoom },
            // No Picture — should emit grey placeholder rectangle
        });
        pres.Slides.Add(slide);

        var ops = SlideCompositor.Compose(pres, slide);

        ops.OfType<DrawOp.Shape>().Should().HaveCount(1,
            "a preserved object without a preview image should emit one DrawOp.Shape placeholder");
    }

    // ── Fixture builders ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal PPTX stream with a single slide containing <paramref name="shapeXml"/>
    /// injected into the spTree. Optionally adds extra OPC parts and slide rels.
    /// </summary>
    private static MemoryStream BuildPptxWithShapeXml(
        string shapeXml,
        Dictionary<string, (byte[] bytes, string contentType)>? extraParts = null,
        Dictionary<string, (string relType, string target)>? extraRels = null)
    {
        // Create a base PPTX via the writer
        var basePres = new Presentation();
        basePres.Slides.Add(new Slide());
        var ms = new MemoryStream();
        PptxPackageWriter.Write(basePres, ms);
        ms.Position = 0;

        // Open for update and inject content
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            // Add extra parts
            if (extraParts is not null)
            {
                foreach (var kv in extraParts)
                {
                    var entry = zip.CreateEntry(kv.Key, CompressionLevel.Optimal);
                    using var s = entry.Open();
                    s.Write(kv.Value.bytes);
                }
            }

            // Patch slide rels to add extra rels
            if (extraRels is not null)
            {
                const string relsPath = "ppt/slides/_rels/slide1.xml.rels";
                var relsEntry = zip.GetEntry(relsPath);
                string relsXml;
                using (var sr = new StreamReader(relsEntry!.Open())) relsXml = sr.ReadToEnd();
                var relsDoc = XDocument.Parse(relsXml);
                var pkgRelsNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
                foreach (var kv in extraRels)
                {
                    relsDoc.Root!.Add(new XElement(pkgRelsNs + "Relationship",
                        new XAttribute("Id", kv.Key),
                        new XAttribute("Type", kv.Value.relType),
                        new XAttribute("Target", kv.Value.target)));
                }
                relsEntry.Delete();
                var newRels = zip.CreateEntry(relsPath, CompressionLevel.Optimal);
                using (var sw = new StreamWriter(newRels.Open())) relsDoc.Save(sw);
            }

            // Inject shape into spTree in slide1.xml
            const string slidePath = "ppt/slides/slide1.xml";
            var slideEntry = zip.GetEntry(slidePath)!;
            string slideXml;
            using (var sr = new StreamReader(slideEntry.Open())) slideXml = sr.ReadToEnd();
            var slideDoc = XDocument.Parse(slideXml);
            var spTree = slideDoc.Descendants().First(e => e.Name.LocalName == "spTree");
            spTree.Add(XElement.Parse(shapeXml));
            slideEntry.Delete();
            var newSlide = zip.CreateEntry(slidePath, CompressionLevel.Optimal);
            using (var sw = new StreamWriter(newSlide.Open())) slideDoc.Save(sw);
        }

        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Builds a minimal PPTX with a contentPart element and a matching OPC ink part.
    /// </summary>
    private static MemoryStream BuildPptxWithContentPart(
        string contentPartXml,
        byte[] inkPartBytes,
        string inkRelType,
        string inkPartPath,
        string inkRelId)
    {
        var basePres = new Presentation();
        basePres.Slides.Add(new Slide());
        var ms = new MemoryStream();
        PptxPackageWriter.Write(basePres, ms);
        ms.Position = 0;

        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            // Write the ink part
            var inkEntry = zip.CreateEntry(inkPartPath, CompressionLevel.Optimal);
            using (var s = inkEntry.Open()) s.Write(inkPartBytes);

            // Add rel entry (relative target from ppt/slides/ → ../../ppt/ink/)
            const string relsPath = "ppt/slides/_rels/slide1.xml.rels";
            var relsEntry = zip.GetEntry(relsPath)!;
            string relsXml;
            using (var sr = new StreamReader(relsEntry.Open())) relsXml = sr.ReadToEnd();
            var relsDoc = XDocument.Parse(relsXml);
            var pkgRelsNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");
            // Relative path from ppt/slides/ to ppt/ink/
            var relTarget = "../" + string.Join("/", inkPartPath.Split('/')[1..]);
            relsDoc.Root!.Add(new XElement(pkgRelsNs + "Relationship",
                new XAttribute("Id", inkRelId),
                new XAttribute("Type", inkRelType),
                new XAttribute("Target", relTarget)));
            relsEntry.Delete();
            var newRels = zip.CreateEntry(relsPath, CompressionLevel.Optimal);
            using (var sw = new StreamWriter(newRels.Open())) relsDoc.Save(sw);

            // Inject contentPart into spTree
            const string slidePath = "ppt/slides/slide1.xml";
            var slideEntry = zip.GetEntry(slidePath)!;
            string slideXml;
            using (var sr = new StreamReader(slideEntry.Open())) slideXml = sr.ReadToEnd();
            var slideDoc = XDocument.Parse(slideXml);
            var spTree = slideDoc.Descendants().First(e => e.Name.LocalName == "spTree");
            spTree.Add(XElement.Parse(contentPartXml));
            slideEntry.Delete();
            var newSlide = zip.CreateEntry(slidePath, CompressionLevel.Optimal);
            using (var sw = new StreamWriter(newSlide.Open())) slideDoc.Save(sw);
        }

        ms.Position = 0;
        return ms;
    }

    private static MemoryStream WritePptxToMemory(Presentation pres)
    {
        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        return ms;
    }
}
