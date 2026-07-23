using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R78-io-shape-geometry-5-2: connector kinds (Line/ElbowConnector/CurvedConnector) must be written
/// as <c>&lt;xdr:cxnSp&gt;</c> (with <c>&lt;xdr:nvCxnSpPr&gt;</c>/<c>&lt;xdr:cNvCxnSpPr&gt;</c>), not the
/// generic <c>&lt;xdr:sp&gt;</c> -- otherwise Excel treats the saved object as a plain autoshape rather
/// than a connector (no connection-site glue, not listed as "Connector" in the Selection Pane).
/// </summary>
public sealed class R78_ConnectorShapeCxnSpWriterTests
{
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Theory]
    [InlineData(DrawingShapeKind.Line, "line")]
    [InlineData(DrawingShapeKind.ElbowConnector, "bentConnector2")]
    [InlineData(DrawingShapeKind.CurvedConnector, "curvedConnector2")]
    public void XlsxAdapter_WritesConnectorKind_AsCxnSp_NotGenericSp(DrawingShapeKind kind, string expectedPreset)
    {
        var workbook = CreateWorkbookWithShape(kind);
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = LoadDrawingXml(archive);

        // The connector must be packaged as <xdr:cxnSp> with the CT_Connector non-visual properties,
        // and must NOT also appear as a generic <xdr:sp>.
        var cxnSp = drawingXml.Descendants(SpreadsheetDrawingNs + "cxnSp").Should().ContainSingle(
            "connector kinds must be written as <xdr:cxnSp>, not <xdr:sp>").Subject;
        drawingXml.Descendants(SpreadsheetDrawingNs + "sp").Should().BeEmpty(
            "a connector shape must not also/instead be emitted as a generic <xdr:sp>");

        cxnSp.Element(SpreadsheetDrawingNs + "nvCxnSpPr").Should().NotBeNull();
        cxnSp.Element(SpreadsheetDrawingNs + "nvCxnSpPr")!
            .Element(SpreadsheetDrawingNs + "cNvCxnSpPr").Should().NotBeNull();
        cxnSp.Element(SpreadsheetDrawingNs + "nvCxnSpPr")!
            .Element(SpreadsheetDrawingNs + "cNvPr").Should().NotBeNull();

        var prstGeom = cxnSp.Descendants(DrawingNs + "prstGeom").Should().ContainSingle().Subject;
        prstGeom.Attribute("prst")!.Value.Should().Be(expectedPreset);
    }

    [Fact]
    public void XlsxAdapter_RoundTripsConnectorKind_ThroughCxnSp()
    {
        var workbook = CreateWorkbookWithShape(DrawingShapeKind.ElbowConnector);
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.Kind.Should().Be(DrawingShapeKind.ElbowConnector);
    }

    /// <summary>No-regression sibling: a non-connector preset shape must still be written as
    /// <c>&lt;xdr:sp&gt;</c>, not <c>&lt;xdr:cxnSp&gt;</c>.</summary>
    [Fact]
    public void XlsxAdapter_WritesRegularShape_AsGenericSp_NotCxnSp()
    {
        var workbook = CreateWorkbookWithShape(DrawingShapeKind.RoundedRectangle);
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = LoadDrawingXml(archive);

        drawingXml.Descendants(SpreadsheetDrawingNs + "sp").Should().ContainSingle(
            "a non-connector preset shape must still be written as <xdr:sp>");
        drawingXml.Descendants(SpreadsheetDrawingNs + "cxnSp").Should().BeEmpty(
            "a non-connector preset shape must not be written as <xdr:cxnSp>");
    }

    private static Workbook CreateWorkbookWithShape(DrawingShapeKind kind)
    {
        var workbook = new Workbook("Connectors");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = kind,
            Width = 200,
            Height = 100,
            HasFill = false,
            OutlineColor = new CellColor(50, 50, 50)
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
