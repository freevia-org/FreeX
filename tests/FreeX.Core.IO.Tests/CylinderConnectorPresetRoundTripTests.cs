using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip tests for Cylinder ("can" OOXML preset) and connector preset mapping.
/// Verifies that DrawingShapeKind.Cylinder saves as prst="can" and reloads back as
/// DrawingShapeKind.Cylinder through both XlsxFileAdapter and NativeJsonAdapter.
/// </summary>
public sealed class CylinderConnectorPresetRoundTripTests
{
    // ── Cylinder — OOXML "can" preset ─────────────────────────────────────

    [Fact]
    public void XlsxAdapter_WritesCylinderAsCan_Preset()
    {
        var workbook = CreateWorkbookWithCylinder();
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = LoadDrawingXml(archive);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var prstGeom = drawingXml.Descendants(a + "prstGeom").Should().ContainSingle().Subject;
        prstGeom.Attribute("prst")!.Value.Should().Be("can",
            "Cylinder must be saved as OOXML preset 'can'");
    }

    [Fact]
    public void XlsxAdapter_RoundTripsCylinder_KindSurvivesReload()
    {
        var workbook = CreateWorkbookWithCylinder();
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        var loaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).DrawingShapes
            .Should().ContainSingle().Subject;
        loaded.Kind.Should().Be(DrawingShapeKind.Cylinder,
            "reloading a 'can' prstGeom must map back to DrawingShapeKind.Cylinder");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTripsCylinder_KindSurvivesReload()
    {
        var workbook = CreateWorkbookWithCylinder();
        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        stream.Position = 0;

        var loaded = new NativeJsonAdapter().Load(stream).GetSheetAt(0).DrawingShapes
            .Should().ContainSingle().Subject;
        loaded.Kind.Should().Be(DrawingShapeKind.Cylinder);
    }

    // ── Cylinder — OOXML fragment parse ────────────────────────────────────

    [Fact]
    public void XlsxDrawingPartReader_Reads_CanPresetAsCylinder()
    {
        var drawingXml = XDocument.Parse("""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:oneCellAnchor>
                <xdr:from>
                  <xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff>
                  <xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff>
                </xdr:from>
                <xdr:ext cx="914400" cy="914400"/>
                <xdr:sp>
                  <xdr:nvSpPr>
                    <xdr:cNvPr id="2" name="Cylinder 1"/>
                    <xdr:cNvSpPr/>
                  </xdr:nvSpPr>
                  <xdr:spPr>
                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm>
                    <a:prstGeom prst="can"><a:avLst/></a:prstGeom>
                    <a:solidFill><a:srgbClr val="ED7D31"/></a:solidFill>
                    <a:ln><a:solidFill><a:srgbClr val="C55A11"/></a:solidFill></a:ln>
                  </xdr:spPr>
                </xdr:sp>
                <xdr:clientData/>
              </xdr:oneCellAnchor>
            </xdr:wsDr>
            """);

        var shape = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml)
            .Shapes
            .Should()
            .ContainSingle("the 'can' prstGeom must be read as a shape")
            .Subject;

        shape.Kind.Should().Be(DrawingShapeKind.Cylinder,
            "OOXML prst='can' must map to DrawingShapeKind.Cylinder");
    }

    // ── CurvedConnector — OOXML fragment parse ──────────────────────────────

    [Theory]
    [InlineData("curvedConnector2")]
    [InlineData("curvedConnector3")]
    [InlineData("curvedConnector4")]
    [InlineData("curvedConnector5")]
    public void XlsxDrawingPartReader_Reads_CurvedConnectorVariants_AsCurvedConnector(string prst)
    {
        var drawingXml = BuildConnectorXml(prst);

        var shape = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml)
            .Shapes
            .Should().ContainSingle()
            .Subject;

        shape.Kind.Should().Be(DrawingShapeKind.CurvedConnector,
            $"OOXML prst='{prst}' must map to DrawingShapeKind.CurvedConnector");
    }

    [Theory]
    [InlineData("bentConnector2")]
    [InlineData("bentConnector3")]
    [InlineData("bentConnector4")]
    [InlineData("bentConnector5")]
    public void XlsxDrawingPartReader_Reads_BentConnectorVariants_AsElbowConnector(string prst)
    {
        var drawingXml = BuildConnectorXml(prst);

        var shape = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml)
            .Shapes
            .Should().ContainSingle()
            .Subject;

        shape.Kind.Should().Be(DrawingShapeKind.ElbowConnector,
            $"OOXML prst='{prst}' must map to DrawingShapeKind.ElbowConnector");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Workbook CreateWorkbookWithCylinder()
    {
        var workbook = new Workbook("Cylinder");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Cylinder,
            Width = 96,
            Height = 120,
            FillColor = new CellColor(0xED, 0x7D, 0x31),
            OutlineColor = new CellColor(0xC5, 0x5A, 0x11)
        });
        return workbook;
    }

    private static XDocument BuildConnectorXml(string prst) => XDocument.Parse($"""
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
                <xdr:cNvPr id="2" name="Connector 1"/>
                <xdr:cNvCxnSpPr/>
              </xdr:nvCxnSpPr>
              <xdr:spPr>
                <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="457200"/></a:xfrm>
                <a:prstGeom prst="{prst}"><a:avLst/></a:prstGeom>
                <a:ln><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></a:ln>
              </xdr:spPr>
            </xdr:cxnSp>
            <xdr:clientData/>
          </xdr:oneCellAnchor>
        </xdr:wsDr>
        """);

    private static XDocument LoadDrawingXml(ZipArchive archive) =>
        XlsxPackageTestFixtures.LoadPackageXml(
            archive,
            "xl/drawings/drawing1.xml",
            "the XLSX package should contain xl/drawings/drawing1.xml");
}
