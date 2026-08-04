using System.IO.Compression;
using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class ZoomGeometryRoundTripTests
{
    private static readonly XNamespace PresentationNamespace =
        "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace DrawingNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void Slide_and_section_zoom_transforms_survive_save_and_reopen()
    {
        var presentation = BuildPresentation();
        var slideZoom = SlideZoomInsertionPlanner.CreateShape(presentation, 0, "slide-2");
        var sectionZoom = SectionZoomInsertionPlanner.CreateShape(presentation, "{TARGET}");
        presentation.Slides[0].Shapes.Add(slideZoom);
        presentation.Slides[0].Shapes.Add(sectionZoom);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new MoveShapeCommand(0, slideZoom.Id, 100000, 200000));
        bus.Execute(new ResizeShapeCommand(0, sectionZoom.Id, 900000, 800000, 3000000, 2000000));
        bus.Execute(new RotateShapeCommand(0, sectionZoom.Id, 22.5));

        var package = Write(presentation);
        var reopened = PptxPackageReader.Read(new MemoryStream(package));
        var reopenedZooms = reopened.Slides[0].Shapes
            .Where(shape => shape.Kind == SlideShapeKind.Zoom)
            .ToArray();

        var reopenedSlide = reopenedZooms.Single(shape =>
            shape.PreservedObject!.ZoomTargetSlideNumericId == slideZoom.PreservedObject!.ZoomTargetSlideNumericId);
        reopenedSlide.OffsetXEmu.Should().Be(slideZoom.OffsetXEmu);
        reopenedSlide.OffsetYEmu.Should().Be(slideZoom.OffsetYEmu);
        reopenedSlide.ExtentCxEmu.Should().Be(slideZoom.ExtentCxEmu);
        reopenedSlide.ExtentCyEmu.Should().Be(slideZoom.ExtentCyEmu);
        reopenedSlide.RotationDeg.Should().Be(slideZoom.RotationDeg);

        reopenedZooms.Should().HaveCount(2);
        reopenedZooms.Should().Contain(shape =>
            shape.PreservedObject!.ZoomTargetSectionId == sectionZoom.PreservedObject!.ZoomTargetSectionId
            && shape.OffsetXEmu == sectionZoom.OffsetXEmu
            && shape.OffsetYEmu == sectionZoom.OffsetYEmu
            && shape.ExtentCxEmu == sectionZoom.ExtentCxEmu
            && shape.ExtentCyEmu == sectionZoom.ExtentCyEmu
            && Math.Abs(shape.RotationDeg - sectionZoom.RotationDeg) < 0.0001);
    }

    [Fact]
    public void Summary_zoom_transform_updates_native_and_fallback_geometry()
    {
        var presentation = BuildPresentation();
        var summaryZoom = SummaryZoomInsertionPlanner.CreateShape(
            presentation,
            new[] { "{ONE}", "{TARGET}" });
        presentation.Slides[0].Shapes.Add(summaryZoom);
        var bus = new PresentationCommandBus(presentation);
        bus.Execute(new ResizeShapeCommand(0, summaryZoom.Id, 700000, 600000, 5000000, 3200000));
        bus.Execute(new RotateShapeCommand(0, summaryZoom.Id, 17));

        var package = Write(presentation);
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var slideStream = archive.GetEntry("ppt/slides/slide1.xml")!.Open();
        var slideXml = XDocument.Load(slideStream);
        var transforms = slideXml.Descendants(PresentationNamespace + "xfrm")
            .Concat(slideXml.Descendants(DrawingNamespace + "xfrm"))
            .Where(xfrm => xfrm.Element(DrawingNamespace + "off") is not null
                           && xfrm.Element(DrawingNamespace + "ext") is not null)
            .ToArray();

        transforms.Should().Contain(xfrm => IsTransform(xfrm, summaryZoom));
        transforms.Should().Contain(xfrm =>
            xfrm.Element(DrawingNamespace + "off")!.Attribute("x")!.Value == summaryZoom.OffsetXEmu.ToString()
            && xfrm.Element(DrawingNamespace + "off")!.Attribute("y")!.Value == summaryZoom.OffsetYEmu.ToString()
            && xfrm.Element(DrawingNamespace + "ext")!.Attribute("cx")!.Value == summaryZoom.ExtentCxEmu.ToString()
            && xfrm.Element(DrawingNamespace + "ext")!.Attribute("cy")!.Value == summaryZoom.ExtentCyEmu.ToString());
        transforms.Should().HaveCountGreaterThanOrEqualTo(2);

        var reopened = PptxPackageReader.Read(new MemoryStream(package));
        var reopenedSummary = reopened.Slides[0].Shapes.Single(shape => shape.Kind == SlideShapeKind.Zoom);
        reopenedSummary.OffsetXEmu.Should().Be(summaryZoom.OffsetXEmu);
        reopenedSummary.OffsetYEmu.Should().Be(summaryZoom.OffsetYEmu);
        reopenedSummary.ExtentCxEmu.Should().Be(summaryZoom.ExtentCxEmu);
        reopenedSummary.ExtentCyEmu.Should().Be(summaryZoom.ExtentCyEmu);
        reopenedSummary.RotationDeg.Should().Be(summaryZoom.RotationDeg);
    }

    [Fact]
    public void Retargeted_section_zoom_round_trips_native_section_id()
    {
        var presentation = BuildPresentation();
        var shape = SectionZoomInsertionPlanner.CreateShape(presentation, "{ONE}");
        presentation.Slides[0].Shapes.Add(shape);
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new SetZoomTargetCommand(
            0,
            shape.Id,
            ZoomTargetKind.Section,
            slideNumericId: null,
            sectionId: "{TARGET}",
            "Zoom to Target"));

        shape.PreservedObject!.ZoomTargetSectionId.Should().Be("{TARGET}");
        shape.PreservedObject.RawXml.Should().Contain("sectionId=\"{TARGET}\"");
        var reopened = PptxPackageReader.Read(new MemoryStream(Write(presentation)));
        reopened.Slides[0].Shapes
            .Where(candidate => candidate.Kind == SlideShapeKind.Zoom)
            .Single().PreservedObject!.ZoomTargetSectionId.Should().Be("{TARGET}");
    }

    private static bool IsTransform(XElement xfrm, SlideShape shape)
    {
        return xfrm.Element(DrawingNamespace + "off")!.Attribute("x")!.Value == shape.OffsetXEmu.ToString()
            && xfrm.Element(DrawingNamespace + "off")!.Attribute("y")!.Value == shape.OffsetYEmu.ToString()
            && xfrm.Element(DrawingNamespace + "ext")!.Attribute("cx")!.Value == shape.ExtentCxEmu.ToString()
            && xfrm.Element(DrawingNamespace + "ext")!.Attribute("cy")!.Value == shape.ExtentCyEmu.ToString()
            && xfrm.Attribute("rot")!.Value == ((long)Math.Round(shape.RotationDeg * 60000)).ToString();
    }

    private static byte[] Write(Presentation presentation)
    {
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    private static Presentation BuildPresentation()
    {
        var presentation = new Presentation();
        for (var index = 1; index <= 3; index++)
            presentation.Slides.Add(new Slide { Id = $"slide-{index}", Title = $"Slide {index}" });

        var one = new PresentationSection { Id = "{ONE}", Name = "One" };
        one.SlideIds.Add("slide-1");
        var target = new PresentationSection { Id = "{TARGET}", Name = "Target" };
        target.SlideIds.Add("slide-2");
        presentation.Sections.Add(one);
        presentation.Sections.Add(target);
        return presentation;
    }
}
