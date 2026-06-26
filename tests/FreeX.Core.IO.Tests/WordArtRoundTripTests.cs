using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip tests for WordArt fields: <see cref="DrawingShapeModel.IsWordArt"/>,
/// <see cref="DrawingShapeModel.WarpPreset"/>, <see cref="DrawingShapeModel.ShapeTextGradientEndColor"/>,
/// <see cref="DrawingShapeModel.ShapeTextOutlineColor"/>, and
/// <see cref="DrawingShapeModel.ShapeTextOutlineWidthPoints"/>.
///
/// Covers:
/// (a) OOXML XML fragment parsing via <see cref="XlsxWorksheetDrawingPartReader.ReadShapeParts"/>.
/// (b) Full XLSX save → reload via <see cref="XlsxFileAdapter"/>.
/// (c) NativeJson save → reload via <see cref="NativeJsonAdapter"/>.
/// </summary>
public sealed class WordArtRoundTripTests
{
    private static readonly XNamespace DrawingNs  = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    // ── OOXML parse: solid text fill + text outline + prstTxWarp ────────────

    [Fact]
    public void XlsxDrawingPartReader_Reads_WordArt_SolidFill_TextOutline_WarpPreset()
    {
        var drawingXml = XDocument.Parse("""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:oneCellAnchor>
                <xdr:from>
                  <xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff>
                  <xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff>
                </xdr:from>
                <xdr:ext cx="2700000" cy="900000"/>
                <xdr:sp>
                  <xdr:nvSpPr>
                    <xdr:cNvPr id="2" name="WordArt 1"/>
                    <xdr:cNvSpPr/>
                  </xdr:nvSpPr>
                  <xdr:spPr>
                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="2700000" cy="900000"/></a:xfrm>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                    <a:noFill/>
                  </xdr:spPr>
                  <xdr:txBody>
                    <a:bodyPr anchor="ctr" wrap="square">
                      <a:prstTxWarp prst="textWave1"/>
                    </a:bodyPr>
                    <a:lstStyle/>
                    <a:p>
                      <a:pPr algn="ctr"/>
                      <a:r>
                        <a:rPr lang="en-US" dirty="0" sz="3600" b="1">
                          <a:solidFill><a:srgbClr val="FF4500"/></a:solidFill>
                          <a:ln w="12700">
                            <a:solidFill><a:srgbClr val="8B0000"/></a:solidFill>
                          </a:ln>
                        </a:rPr>
                        <a:t>FreeX</a:t>
                      </a:r>
                    </a:p>
                  </xdr:txBody>
                  <xdr:clientData/>
                </xdr:sp>
              </xdr:oneCellAnchor>
            </xdr:wsDr>
            """);

        var (_, shapes) = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml);
        var shape = shapes.Should().ContainSingle().Subject;

        shape.IsWordArt.Should().BeTrue("prstTxWarp + text outline = WordArt");
        shape.WarpPreset.Should().Be("textWave1");
        shape.ShapeText.Should().Be("FreeX");
        shape.ShapeTextFontSizePoints.Should().Be(36.0);
        shape.ShapeTextBold.Should().BeTrue();
        shape.ShapeTextColor.Should().Be(new CellColor(0xFF, 0x45, 0x00));
        shape.ShapeTextOutlineColor.Should().Be(new CellColor(0x8B, 0x00, 0x00));
        shape.ShapeTextOutlineWidthPoints.Should().BeApproximately(1.0, 0.01,
            "12700 EMU = 1 pt");
        shape.ShapeTextGradientEndColor.Should().BeNull("only solidFill on run — no gradient");
    }

    [Fact]
    public void XlsxDrawingPartReader_Reads_WordArt_GradientTextFill()
    {
        var drawingXml = XDocument.Parse("""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:oneCellAnchor>
                <xdr:from>
                  <xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff>
                  <xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff>
                </xdr:from>
                <xdr:ext cx="2700000" cy="900000"/>
                <xdr:sp>
                  <xdr:nvSpPr>
                    <xdr:cNvPr id="3" name="WordArt 2"/>
                    <xdr:cNvSpPr/>
                  </xdr:nvSpPr>
                  <xdr:spPr>
                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="2700000" cy="900000"/></a:xfrm>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                    <a:noFill/>
                  </xdr:spPr>
                  <xdr:txBody>
                    <a:bodyPr anchor="ctr" wrap="square"/>
                    <a:lstStyle/>
                    <a:p>
                      <a:pPr algn="ctr"/>
                      <a:r>
                        <a:rPr lang="en-US" dirty="0" sz="4800" b="1">
                          <a:gradFill>
                            <a:gsLst>
                              <a:gs pos="0"><a:srgbClr val="FF0000"/></a:gs>
                              <a:gs pos="100000"><a:srgbClr val="0000FF"/></a:gs>
                            </a:gsLst>
                            <a:lin ang="5400000" scaled="0"/>
                          </a:gradFill>
                        </a:rPr>
                        <a:t>Gradient</a:t>
                      </a:r>
                    </a:p>
                  </xdr:txBody>
                  <xdr:clientData/>
                </xdr:sp>
              </xdr:oneCellAnchor>
            </xdr:wsDr>
            """);

        var (_, shapes) = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml);
        var shape = shapes.Should().ContainSingle().Subject;

        shape.IsWordArt.Should().BeTrue("gradFill on run = WordArt");
        shape.WarpPreset.Should().BeNull("no prstTxWarp element");
        shape.ShapeText.Should().Be("Gradient");
        shape.ShapeTextFontSizePoints.Should().Be(48.0);
        // Start color is picked up as the main text color from the first gradient stop.
        shape.ShapeTextColor.Should().Be(new CellColor(0xFF, 0x00, 0x00));
        // End color from the second stop.
        shape.ShapeTextGradientEndColor.Should().Be(new CellColor(0x00, 0x00, 0xFF));
        shape.ShapeTextOutlineColor.Should().BeNull("no text outline");
    }

    [Fact]
    public void XlsxDrawingPartReader_NormalShape_IsWordArtFalse()
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
                <xdr:sp>
                  <xdr:nvSpPr>
                    <xdr:cNvPr id="4" name="Rectangle 1"/>
                    <xdr:cNvSpPr/>
                  </xdr:nvSpPr>
                  <xdr:spPr>
                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="457200"/></a:xfrm>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                    <a:solidFill><a:srgbClr val="5B9BD5"/></a:solidFill>
                  </xdr:spPr>
                  <xdr:txBody>
                    <a:bodyPr anchor="ctr" wrap="square"/>
                    <a:lstStyle/>
                    <a:p>
                      <a:pPr algn="ctr"/>
                      <a:r>
                        <a:rPr lang="en-US" dirty="0" sz="1100">
                          <a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill>
                        </a:rPr>
                        <a:t>Normal</a:t>
                      </a:r>
                    </a:p>
                  </xdr:txBody>
                  <xdr:clientData/>
                </xdr:sp>
              </xdr:oneCellAnchor>
            </xdr:wsDr>
            """);

        var (_, shapes) = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml);
        var shape = shapes.Should().ContainSingle().Subject;

        shape.IsWordArt.Should().BeFalse("no gradient, no text outline, no prstTxWarp");
        shape.WarpPreset.Should().BeNull();
        shape.ShapeTextGradientEndColor.Should().BeNull();
        shape.ShapeTextOutlineColor.Should().BeNull();
        shape.ShapeTextOutlineWidthPoints.Should().Be(0);
    }

    // ── XLSX adapter round-trip ────────────────────────────────────────────

    [Fact]
    public void XlsxAdapter_RoundTrips_WordArt_TextOutline_WarpPreset()
    {
        var workbook = CreateWordArtWorkbook(
            isWordArt: true,
            warpPreset: "textWave1",
            textColor: new CellColor(0xFF, 0x45, 0x00),
            gradEndColor: null,
            outlineColor: new CellColor(0x8B, 0x00, 0x00),
            outlineWidthPt: 1.5);

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);

        // Verify XML contains prstTxWarp and <a:ln> on rPr.
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var xml = LoadDrawingXml(archive);
            var bodyPr = xml.Descendants(DrawingNs + "bodyPr").Should().ContainSingle().Subject;
            bodyPr.Element(DrawingNs + "prstTxWarp")!.Attribute("prst")!.Value
                .Should().Be("textWave1");

            var rPr = xml.Descendants(DrawingNs + "rPr").Should().ContainSingle().Subject;
            rPr.Element(DrawingNs + "ln").Should().NotBeNull("text outline should be written");
        }

        // Reload and verify model fields survive.
        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.IsWordArt.Should().BeTrue();
        loaded.WarpPreset.Should().Be("textWave1");
        loaded.ShapeTextOutlineColor.Should().Be(new CellColor(0x8B, 0x00, 0x00));
        loaded.ShapeTextOutlineWidthPoints.Should().BeApproximately(1.5, 0.05);
        loaded.ShapeTextGradientEndColor.Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_RoundTrips_WordArt_GradientTextFill()
    {
        var workbook = CreateWordArtWorkbook(
            isWordArt: true,
            warpPreset: null,
            textColor: new CellColor(0xFF, 0x00, 0x00),
            gradEndColor: new CellColor(0x00, 0x00, 0xFF),
            outlineColor: null,
            outlineWidthPt: 0);

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);

        // Verify <a:gradFill> is on rPr (not solidFill).
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var xml = LoadDrawingXml(archive);
            var rPr = xml.Descendants(DrawingNs + "rPr").Should().ContainSingle().Subject;
            rPr.Element(DrawingNs + "gradFill").Should().NotBeNull("gradient fill should be emitted");
            rPr.Element(DrawingNs + "solidFill").Should().BeNull("gradient replaces solid fill");
        }

        // Reload and verify model.
        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.IsWordArt.Should().BeTrue();
        loaded.ShapeTextColor.Should().Be(new CellColor(0xFF, 0x00, 0x00));
        loaded.ShapeTextGradientEndColor.Should().Be(new CellColor(0x00, 0x00, 0xFF));
        loaded.WarpPreset.Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_NonWordArt_NoWordArtElementsWritten()
    {
        var workbook = CreateWordArtWorkbook(
            isWordArt: false,
            warpPreset: null,
            textColor: new CellColor(0xFF, 0xFF, 0xFF),
            gradEndColor: null,
            outlineColor: null,
            outlineWidthPt: 0);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var xml = LoadDrawingXml(archive);
        xml.Descendants(DrawingNs + "prstTxWarp").Should().BeEmpty("no warp for non-WordArt");
        var rPr = xml.Descendants(DrawingNs + "rPr").FirstOrDefault();
        if (rPr is not null)
        {
            rPr.Element(DrawingNs + "gradFill").Should().BeNull("no gradient for non-WordArt");
            rPr.Element(DrawingNs + "ln").Should().BeNull("no text outline for non-WordArt");
        }
    }

    // ── NativeJson round-trip ─────────────────────────────────────────────

    [Fact]
    public void NativeJsonAdapter_RoundTrips_WordArt_AllFields()
    {
        var workbook = CreateWordArtWorkbook(
            isWordArt: true,
            warpPreset: "textCurve",
            textColor: new CellColor(0x22, 0x44, 0x66),
            gradEndColor: new CellColor(0xAA, 0xBB, 0xCC),
            outlineColor: new CellColor(0x11, 0x22, 0x33),
            outlineWidthPt: 2.0);

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.IsWordArt.Should().BeTrue();
        loaded.WarpPreset.Should().Be("textCurve");
        loaded.ShapeTextColor.Should().Be(new CellColor(0x22, 0x44, 0x66));
        loaded.ShapeTextGradientEndColor.Should().Be(new CellColor(0xAA, 0xBB, 0xCC));
        loaded.ShapeTextOutlineColor.Should().Be(new CellColor(0x11, 0x22, 0x33));
        loaded.ShapeTextOutlineWidthPoints.Should().Be(2.0);
    }

    [Fact]
    public void NativeJsonAdapter_NonWordArt_WordArtFieldsAreDefault()
    {
        var workbook = CreateWordArtWorkbook(
            isWordArt: false,
            warpPreset: null,
            textColor: null,
            gradEndColor: null,
            outlineColor: null,
            outlineWidthPt: 0);

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.IsWordArt.Should().BeFalse();
        loaded.WarpPreset.Should().BeNull();
        loaded.ShapeTextGradientEndColor.Should().BeNull();
        loaded.ShapeTextOutlineColor.Should().BeNull();
        loaded.ShapeTextOutlineWidthPoints.Should().Be(0);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static Workbook CreateWordArtWorkbook(
        bool isWordArt,
        string? warpPreset,
        CellColor? textColor,
        CellColor? gradEndColor,
        CellColor? outlineColor,
        double outlineWidthPt)
    {
        var workbook = new Workbook("WordArtTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 240,
            Height = 80,
            HasFill = false,
            ShapeText = "FreeX",
            ShapeTextFontSizePoints = 36,
            ShapeTextBold = true,
            ShapeTextHAlign = DrawingShapeTextHAlign.Center,
            ShapeTextVAnchor = DrawingShapeTextVAnchor.Middle,
            ShapeTextColor = textColor,
            IsWordArt = isWordArt,
            WarpPreset = warpPreset,
            ShapeTextGradientEndColor = gradEndColor,
            ShapeTextOutlineColor = outlineColor,
            ShapeTextOutlineWidthPoints = outlineWidthPt,
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
