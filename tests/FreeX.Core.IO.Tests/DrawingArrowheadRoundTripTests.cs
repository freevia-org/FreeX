using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Verifies that arrowhead data (head/tail, type, w, len) survives a full save→reload cycle
/// through both the XLSX adapter and the NativeJson adapter.
/// </summary>
public sealed class DrawingArrowheadRoundTripTests
{
    // ── XLSX round-trip ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(DrawingArrowheadType.Triangle, DrawingArrowheadSize.Small,  DrawingArrowheadSize.Medium, "triangle", "sm", "med")]
    [InlineData(DrawingArrowheadType.Arrow,    DrawingArrowheadSize.Medium, DrawingArrowheadSize.Large,  "arrow",    "med", "lg")]
    [InlineData(DrawingArrowheadType.Stealth,  DrawingArrowheadSize.Large,  DrawingArrowheadSize.Small,  "stealth",  "lg", "sm")]
    [InlineData(DrawingArrowheadType.Diamond,  DrawingArrowheadSize.Medium, DrawingArrowheadSize.Medium, "diamond",  "med", "med")]
    [InlineData(DrawingArrowheadType.Oval,     DrawingArrowheadSize.Small,  DrawingArrowheadSize.Large,  "oval",     "sm", "lg")]
    public void XlsxAdapter_RoundTrips_HeadArrowhead(
        DrawingArrowheadType type,
        DrawingArrowheadSize w,
        DrawingArrowheadSize len,
        string expectedTypeAttr,
        string expectedWAttr,
        string expectedLenAttr)
    {
        var workbook = CreateLineWorkbook(head: new DrawingArrowhead(type, w, len), tail: null);
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();

        adapter.Save(workbook, stream);
        stream.Position = 0;

        // Verify raw XML
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var xml = LoadDrawingXml(archive);
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var headEnd = xml.Descendants(a + "headEnd").Should().ContainSingle().Subject;
            headEnd.Attribute("type")!.Value.Should().Be(expectedTypeAttr);
            headEnd.Attribute("w")!.Value.Should().Be(expectedWAttr);
            headEnd.Attribute("len")!.Value.Should().Be(expectedLenAttr);
            // No tailEnd element expected
            xml.Descendants(a + "tailEnd").Should().BeEmpty();
        }

        // Verify model round-trip
        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.HeadArrowhead.Should().NotBeNull();
        loaded.HeadArrowhead!.Type.Should().Be(type);
        loaded.HeadArrowhead.Width.Should().Be(w);
        loaded.HeadArrowhead.Length.Should().Be(len);
        loaded.TailArrowhead.Should().BeNull();
    }

    [Theory]
    [InlineData(DrawingArrowheadType.Triangle, DrawingArrowheadSize.Medium, DrawingArrowheadSize.Medium, "triangle", "med", "med")]
    [InlineData(DrawingArrowheadType.Arrow,    DrawingArrowheadSize.Large,  DrawingArrowheadSize.Small,  "arrow",    "lg", "sm")]
    public void XlsxAdapter_RoundTrips_TailArrowhead(
        DrawingArrowheadType type,
        DrawingArrowheadSize w,
        DrawingArrowheadSize len,
        string expectedTypeAttr,
        string expectedWAttr,
        string expectedLenAttr)
    {
        var workbook = CreateLineWorkbook(head: null, tail: new DrawingArrowhead(type, w, len));
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();

        adapter.Save(workbook, stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var xml = LoadDrawingXml(archive);
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            xml.Descendants(a + "headEnd").Should().BeEmpty();
            var tailEnd = xml.Descendants(a + "tailEnd").Should().ContainSingle().Subject;
            tailEnd.Attribute("type")!.Value.Should().Be(expectedTypeAttr);
            tailEnd.Attribute("w")!.Value.Should().Be(expectedWAttr);
            tailEnd.Attribute("len")!.Value.Should().Be(expectedLenAttr);
        }

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.HeadArrowhead.Should().BeNull();
        loaded.TailArrowhead.Should().NotBeNull();
        loaded.TailArrowhead!.Type.Should().Be(type);
        loaded.TailArrowhead.Width.Should().Be(w);
        loaded.TailArrowhead.Length.Should().Be(len);
    }

    [Fact]
    public void XlsxAdapter_RoundTrips_BothArrowheads()
    {
        var head = new DrawingArrowhead(DrawingArrowheadType.Triangle, DrawingArrowheadSize.Small, DrawingArrowheadSize.Large);
        var tail = new DrawingArrowhead(DrawingArrowheadType.Arrow, DrawingArrowheadSize.Large, DrawingArrowheadSize.Small);
        var workbook = CreateLineWorkbook(head, tail);

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.HeadArrowhead.Should().NotBeNull();
        loaded.HeadArrowhead!.Type.Should().Be(DrawingArrowheadType.Triangle);
        loaded.HeadArrowhead.Width.Should().Be(DrawingArrowheadSize.Small);
        loaded.HeadArrowhead.Length.Should().Be(DrawingArrowheadSize.Large);

        loaded.TailArrowhead.Should().NotBeNull();
        loaded.TailArrowhead!.Type.Should().Be(DrawingArrowheadType.Arrow);
        loaded.TailArrowhead.Width.Should().Be(DrawingArrowheadSize.Large);
        loaded.TailArrowhead.Length.Should().Be(DrawingArrowheadSize.Small);
    }

    [Fact]
    public void XlsxAdapter_NoArrowheads_WritesNoArrowheadElements()
    {
        var workbook = CreateLineWorkbook(head: null, tail: null);
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var xml = LoadDrawingXml(archive);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        xml.Descendants(a + "headEnd").Should().BeEmpty();
        xml.Descendants(a + "tailEnd").Should().BeEmpty();
    }

    // ── NativeJson round-trip ───────────────────────────────────────────────

    [Fact]
    public void NativeJsonAdapter_RoundTrips_BothArrowheads()
    {
        var head = new DrawingArrowhead(DrawingArrowheadType.Stealth, DrawingArrowheadSize.Large, DrawingArrowheadSize.Medium);
        var tail = new DrawingArrowhead(DrawingArrowheadType.Diamond, DrawingArrowheadSize.Small, DrawingArrowheadSize.Large);
        var workbook = CreateLineWorkbook(head, tail);

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.HeadArrowhead.Should().NotBeNull();
        loaded.HeadArrowhead!.Type.Should().Be(DrawingArrowheadType.Stealth);
        loaded.HeadArrowhead.Width.Should().Be(DrawingArrowheadSize.Large);
        loaded.HeadArrowhead.Length.Should().Be(DrawingArrowheadSize.Medium);

        loaded.TailArrowhead.Should().NotBeNull();
        loaded.TailArrowhead!.Type.Should().Be(DrawingArrowheadType.Diamond);
        loaded.TailArrowhead.Width.Should().Be(DrawingArrowheadSize.Small);
        loaded.TailArrowhead.Length.Should().Be(DrawingArrowheadSize.Large);
    }

    [Fact]
    public void NativeJsonAdapter_NoneArrowhead_DroppedOnReload()
    {
        // An explicit None arrowhead should not be preserved across a JSON round-trip
        // (it is equivalent to null and will not be stored).
        var workbook = CreateLineWorkbook(
            head: DrawingArrowhead.None,
            tail: null);

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.HeadArrowhead.Should().BeNull();
        loaded.TailArrowhead.Should().BeNull();
    }

    // ── OOXML parse-only (drawing XML fragment) ─────────────────────────────

    [Fact]
    public void XlsxDrawingPartReader_Reads_ConnectorHeadEnd()
    {
        var drawingXml = XDocument.Parse("""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:oneCellAnchor>
                <xdr:from>
                  <xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff>
                  <xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff>
                </xdr:from>
                <xdr:ext cx="914400" cy="457200"/>
                <xdr:cxnSp>
                  <xdr:nvCxnSpPr>
                    <xdr:cNvPr id="2" name="Straight Arrow Connector 1"/>
                    <xdr:cNvCxnSpPr/>
                  </xdr:nvCxnSpPr>
                  <xdr:spPr>
                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="457200"/></a:xfrm>
                    <a:prstGeom prst="line"><a:avLst/></a:prstGeom>
                    <a:ln>
                      <a:solidFill><a:srgbClr val="FF0000"/></a:solidFill>
                      <a:headEnd type="triangle" w="sm" len="med"/>
                      <a:tailEnd type="arrow" w="lg" len="sm"/>
                    </a:ln>
                  </xdr:spPr>
                </xdr:cxnSp>
                <xdr:clientData/>
              </xdr:oneCellAnchor>
            </xdr:wsDr>
            """);

        var shape = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml)
            .Shapes
            .Should()
            .ContainSingle()
            .Subject;

        shape.HeadArrowhead.Should().NotBeNull();
        shape.HeadArrowhead!.Type.Should().Be(DrawingArrowheadType.Triangle);
        shape.HeadArrowhead.Width.Should().Be(DrawingArrowheadSize.Small);
        shape.HeadArrowhead.Length.Should().Be(DrawingArrowheadSize.Medium);

        shape.TailArrowhead.Should().NotBeNull();
        shape.TailArrowhead!.Type.Should().Be(DrawingArrowheadType.Arrow);
        shape.TailArrowhead.Width.Should().Be(DrawingArrowheadSize.Large);
        shape.TailArrowhead.Length.Should().Be(DrawingArrowheadSize.Small);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Workbook CreateLineWorkbook(DrawingArrowhead? head, DrawingArrowhead? tail)
    {
        var workbook = new Workbook("Arrows");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Line,
            Width = 200,
            Height = 100,
            HasFill = false,
            OutlineColor = new CellColor(200, 0, 0),
            OutlineWidthPoints = 2.0,
            HeadArrowhead = head?.IsPresent == true ? head : null,
            TailArrowhead = tail?.IsPresent == true ? tail : null
        });
        return workbook;
    }

    private static XDocument LoadDrawingXml(ZipArchive archive)
    {
        var entry = archive.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        entry.Should().NotBeNull("a drawing XML entry must be present");
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }
}
