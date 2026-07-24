using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip tests for <see cref="CustomGeometry"/> / <c>a:custGeom</c>:
/// - A shape with a <see cref="Shape.CustomGeometry"/> set writes an <c>a:custGeom</c> element (not <c>a:prstGeom</c>).
/// - Segments (MoveTo, LineTo, Close) survive DocxWriter → DocxReader.
/// - A shape without <see cref="Shape.CustomGeometry"/> still writes <c>a:prstGeom</c> as before.
/// </summary>
public class ShapeCustomGeometryRoundTripTests
{
    private static readonly XNamespace W   = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace A   = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";

    private static TextDocument DocumentWith(Shape shape)
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromShape(shape));
        doc.Blocks.Add(para);
        return doc;
    }

    private static TextDocument RoundTrip(TextDocument doc)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XDocument WriteDocXml(TextDocument doc)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static Shape ReadBackShape(Shape shape) =>
        RoundTrip(DocumentWith(shape))
            .Paragraphs.First()
            .Runs.Single(r => r.Shape is not null)
            .Shape!;

    // ── Preset shape still emits prstGeom ──────────────────────────────────────

    [Fact]
    public void PresetShape_WritesSpPr_WithPrstGeom()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        var xdoc  = WriteDocXml(DocumentWith(shape));

        var spPr = xdoc.Descendants(Wps + "spPr").FirstOrDefault();
        spPr.Should().NotBeNull();
        spPr!.Element(A + "prstGeom").Should().NotBeNull("preset shape must use a:prstGeom");
        spPr.Element(A + "custGeom").Should().BeNull("preset shape must not emit a:custGeom");
    }

    // ── Custom geometry writes custGeom ────────────────────────────────────────

    [Fact]
    public void CustomGeometry_WritesSpPr_WithCustGeom()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.CustomGeometry = CustomGeometry.RectanglePoly();
        var xdoc = WriteDocXml(DocumentWith(shape));

        var spPr = xdoc.Descendants(Wps + "spPr").FirstOrDefault();
        spPr.Should().NotBeNull();
        spPr!.Element(A + "custGeom").Should().NotBeNull("freeform shape must use a:custGeom");
        spPr.Element(A + "prstGeom").Should().BeNull("freeform shape must not emit a:prstGeom");
    }

    [Fact]
    public void CustomGeometry_CustGeom_ContainsPathLst()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.CustomGeometry = CustomGeometry.RectanglePoly();
        var xdoc = WriteDocXml(DocumentWith(shape));

        var custGeom = xdoc.Descendants(A + "custGeom").FirstOrDefault();
        custGeom.Should().NotBeNull();
        custGeom!.Element(A + "pathLst").Should().NotBeNull("a:custGeom must contain a:pathLst");
        custGeom.Descendants(A + "path").Should().NotBeEmpty("a:pathLst must contain at least one a:path");
    }

    // ── Segment round-trip ─────────────────────────────────────────────────────

    [Fact]
    public void CustomGeometry_Rectangle_RoundTripsSegmentCount()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.CustomGeometry = CustomGeometry.RectanglePoly();
        var segCount = shape.CustomGeometry.Segments.Count;

        var read = ReadBackShape(shape);
        read.HasCustomGeometry.Should().BeTrue();
        read.CustomGeometry!.Segments.Count.Should().Be(segCount,
            "all segments must survive the round-trip");
    }

    [Fact]
    public void CustomGeometry_Rectangle_FirstSegment_IsMoveTo()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.CustomGeometry = CustomGeometry.RectanglePoly();

        var read = ReadBackShape(shape);
        read.CustomGeometry!.Segments.First().Kind.Should().Be(CustomSegmentKind.MoveTo,
            "first segment must be a MoveTo");
    }

    [Fact]
    public void CustomGeometry_Rectangle_LastSegment_IsClose()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.CustomGeometry = CustomGeometry.RectanglePoly();

        var read = ReadBackShape(shape);
        read.CustomGeometry!.Segments.Last().Kind.Should().Be(CustomSegmentKind.Close,
            "last segment must be a Close");
    }

    [Fact]
    public void CustomGeometry_MoveToPoint_CoordinatesPreserved()
    {
        var cg = new CustomGeometry();
        cg.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo,  new CustomPoint(1000, 2000)));
        cg.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo,  new CustomPoint(20600, 2000)));
        cg.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo,  new CustomPoint(20600, 19600)));
        cg.Segments.Add(new CustomSegment(CustomSegmentKind.Close));

        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.CustomGeometry = cg;

        var read = ReadBackShape(shape);
        var moveTo = read.CustomGeometry!.Segments.First();
        moveTo.Kind.Should().Be(CustomSegmentKind.MoveTo);
        moveTo.Point.Should().NotBeNull();
        moveTo.Point!.X.Should().Be(1000);
        moveTo.Point.Y.Should().Be(2000);
    }

    // ── Ellipse poly round-trip ────────────────────────────────────────────────

    [Fact]
    public void CustomGeometry_CubicBezier_WritesOrderedControlAndEndpointPoints()
    {
        var cubic = new CustomSegment(
            CustomSegmentKind.CubicBezierTo,
            new CustomPoint(21_000, 10_800),
            new CustomPoint(7_200, 0),
            new CustomPoint(14_400, 21_600));
        var geometry = new CustomGeometry();
        geometry.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, new CustomPoint(0, 10_800)));
        geometry.Segments.Add(cubic);

        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.CustomGeometry = geometry;

        var cubicXml = WriteDocXml(DocumentWith(shape)).Descendants(A + "cubicBezTo").Single();
        var points = cubicXml.Elements(A + "pt").ToList();

        points.Should().HaveCount(3);
        points.Select(point => (long)point.Attribute("x")!).Should().Equal(7_200, 14_400, 21_000);
        points.Select(point => (long)point.Attribute("y")!).Should().Equal(0, 21_600, 10_800);
    }

    [Fact]
    public void CustomGeometry_CubicBezier_RoundTripsEndpointAndControls()
    {
        var geometry = new CustomGeometry();
        geometry.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, new CustomPoint(0, 10_800)));
        geometry.Segments.Add(new CustomSegment(
            CustomSegmentKind.CubicBezierTo,
            new CustomPoint(21_000, 10_800),
            new CustomPoint(7_200, 0),
            new CustomPoint(14_400, 21_600)));

        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.CustomGeometry = geometry;

        var cubic = ReadBackShape(shape).CustomGeometry!.Segments.Single(segment =>
            segment.Kind == CustomSegmentKind.CubicBezierTo);

        cubic.Point.Should().Be(new CustomPoint(21_000, 10_800));
        cubic.ControlPoint1.Should().Be(new CustomPoint(7_200, 0));
        cubic.ControlPoint2.Should().Be(new CustomPoint(14_400, 21_600));
    }

    [Fact]
    public void EllipsePoly_RoundTrips_WithExpectedSegmentKinds()
    {
        var shape = Shape.Preset(ShapeKind.Ellipse, widthPt: 80, heightPt: 80);
        shape.CustomGeometry = CustomGeometry.EllipsePoly();

        var read = ReadBackShape(shape);
        read.HasCustomGeometry.Should().BeTrue();

        var kinds = read.CustomGeometry!.Segments.Select(s => s.Kind).ToList();
        kinds.Should().Contain(CustomSegmentKind.MoveTo);
        kinds.Should().Contain(CustomSegmentKind.LineTo);
        kinds.Should().Contain(CustomSegmentKind.Close);
    }

    // ── HasCustomGeometry property ─────────────────────────────────────────────

    [Fact]
    public void HasCustomGeometry_IsTrue_WhenSegmentsPresent()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.CustomGeometry = CustomGeometry.RectanglePoly();
        shape.HasCustomGeometry.Should().BeTrue();
    }

    [Fact]
    public void HasCustomGeometry_IsFalse_WhenNoGeometry()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.HasCustomGeometry.Should().BeFalse();
    }

    // ── custGeom has required child elements ───────────────────────────────────

    [Fact]
    public void CustGeom_Contains_RequiredChildElements()
    {
        var shape = Shape.Preset(ShapeKind.Rectangle, widthPt: 100, heightPt: 60);
        shape.CustomGeometry = CustomGeometry.RectanglePoly();
        var xdoc = WriteDocXml(DocumentWith(shape));

        var cg = xdoc.Descendants(A + "custGeom").First();
        cg.Element(A + "avLst").Should().NotBeNull("a:avLst required by DrawingML schema");
        cg.Element(A + "gdLst").Should().NotBeNull("a:gdLst required by DrawingML schema");
        cg.Element(A + "ahLst").Should().NotBeNull("a:ahLst required by DrawingML schema");
        cg.Element(A + "cxnLst").Should().NotBeNull("a:cxnLst required by DrawingML schema");
        cg.Element(A + "rect").Should().NotBeNull("a:rect required by DrawingML schema");
        cg.Element(A + "pathLst").Should().NotBeNull("a:pathLst required");
    }
}
