using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
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
                          <a:ln w="12700">
                            <a:solidFill><a:srgbClr val="8B0000"/></a:solidFill>
                          </a:ln>
                          <a:solidFill><a:srgbClr val="FF4500"/></a:solidFill>
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
        shape.ShapeTextHasNoFill.Should().BeFalse("solid-fill WordArt must retain its glyph fill");
    }

    [Fact]
    public void XlsxDrawingPartReader_Reads_WordArt_ExplicitNoFill_RetainsOutline()
    {
        var drawingXml = XDocument.Parse("""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:oneCellAnchor><xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:ext cx="2700000" cy="900000"/>
                <xdr:sp><xdr:nvSpPr><xdr:cNvPr id="5" name="Outline WordArt"/><xdr:cNvSpPr/></xdr:nvSpPr>
                  <xdr:spPr><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/></xdr:spPr>
                  <xdr:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr sz="3600"><a:ln w="12700"><a:solidFill><a:srgbClr val="C55A11"/></a:solidFill></a:ln><a:noFill/></a:rPr><a:t>Outline</a:t></a:r></a:p></xdr:txBody>
                  <xdr:clientData/></xdr:sp>
              </xdr:oneCellAnchor>
            </xdr:wsDr>
            """);

        var (_, shapes) = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml);
        var shape = shapes.Should().ContainSingle().Subject;

        shape.IsWordArt.Should().BeTrue("the run has an authored text outline");
        shape.ShapeTextHasNoFill.Should().BeTrue("a:rPr/a:noFill is an explicit outline-only request");
        shape.ShapeTextColor.Should().BeNull("noFill is not a missing authored fill");
        shape.ShapeTextOutlineColor.Should().Be(new CellColor(0xC5, 0x5A, 0x11));
        shape.ShapeTextOutlineWidthPoints.Should().BeApproximately(1.0, 0.01);
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
    public void XlsxAdapter_RoundTrips_WordArt_ExplicitNoFill_RetainingOutline()
    {
        var workbook = CreateWordArtWorkbook(
            isWordArt: true,
            warpPreset: null,
            textColor: new CellColor(0xFF, 0x45, 0x00),
            gradEndColor: null,
            outlineColor: new CellColor(0xC5, 0x5A, 0x11),
            outlineWidthPt: 1.0);
        workbook.GetSheetAt(0).DrawingShapes.Single().ShapeTextHasNoFill = true;

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var rPr = LoadDrawingXml(archive).Descendants(DrawingNs + "rPr").Should().ContainSingle().Subject;
            rPr.Element(DrawingNs + "noFill").Should().NotBeNull();
            rPr.Element(DrawingNs + "solidFill").Should().BeNull("explicit noFill must win over the model's fallback color");
            rPr.Element(DrawingNs + "ln").Should().NotBeNull("the visible WordArt outline must survive");
        }

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.IsWordArt.Should().BeTrue();
        loaded.ShapeTextHasNoFill.Should().BeTrue();
        loaded.ShapeTextOutlineColor.Should().Be(new CellColor(0xC5, 0x5A, 0x11));
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

    // ── WW1 — rPr child order: <a:ln> before fill group ─────────────────

    /// <summary>
    /// WW1: The writer must emit <c>&lt;a:ln&gt;</c> BEFORE the fill (gradFill/solidFill) on
    /// <c>&lt;a:rPr&gt;</c>, matching CT_TextCharacterProperties order.  Reversed order causes
    /// Excel to report the workbook as corrupt and drop the WordArt styling.
    /// </summary>
    [Fact]
    public void XlsxAdapter_WordArt_RprChildOrder_LnBeforeFill_Gradient()
    {
        var workbook = CreateWordArtWorkbook(
            isWordArt: true,
            warpPreset: "textWave1",
            textColor: new CellColor(0xFF, 0x00, 0x00),
            gradEndColor: new CellColor(0x00, 0x00, 0xFF),
            outlineColor: new CellColor(0x00, 0x00, 0x00),
            outlineWidthPt: 1.0);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        // Verify element order in the XML: <a:ln> index must be less than <a:gradFill> index.
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var xml = LoadDrawingXml(archive);
            var rPr = xml.Descendants(DrawingNs + "rPr").Should().ContainSingle().Subject;
            var children = rPr.Elements().Select(e => e.Name.LocalName).ToList();
            var idxLn = children.IndexOf("ln");
            var idxGradFill = children.IndexOf("gradFill");
            idxLn.Should().BeGreaterThanOrEqualTo(0, "<a:ln> must be present");
            idxGradFill.Should().BeGreaterThanOrEqualTo(0, "<a:gradFill> must be present");
            idxLn.Should().BeLessThan(idxGradFill,
                "CT_TextCharacterProperties requires <a:ln> before the fill group");
        }

        // OpenXmlValidator must report no schema errors (WW1 fix: wrong order triggered repair).
        stream.Position = 0;
        SchemaErrors(stream).Should().BeEmpty("ln-before-fill order must satisfy OOXML schema");
    }

    [Fact]
    public void XlsxAdapter_WordArt_RprChildOrder_LnBeforeFill_Solid()
    {
        // Solid fill (no gradient) also needs ln before solidFill.
        var workbook = CreateWordArtWorkbook(
            isWordArt: true,
            warpPreset: null,
            textColor: new CellColor(0xFF, 0x45, 0x00),
            gradEndColor: null,
            outlineColor: new CellColor(0x8B, 0x00, 0x00),
            outlineWidthPt: 1.5);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var xml = LoadDrawingXml(archive);
            var rPr = xml.Descendants(DrawingNs + "rPr").Should().ContainSingle().Subject;
            var children = rPr.Elements().Select(e => e.Name.LocalName).ToList();
            var idxLn = children.IndexOf("ln");
            var idxSolidFill = children.IndexOf("solidFill");
            idxLn.Should().BeGreaterThanOrEqualTo(0, "<a:ln> must be present");
            idxSolidFill.Should().BeGreaterThanOrEqualTo(0, "<a:solidFill> must be present");
            idxLn.Should().BeLessThan(idxSolidFill,
                "CT_TextCharacterProperties requires <a:ln> before solidFill");
        }

        stream.Position = 0;
        SchemaErrors(stream).Should().BeEmpty("ln-before-solidFill must satisfy OOXML schema");
    }

    // ── WW2 — gradient theme-color stops round-trip ───────────────────────

    /// <summary>
    /// WW2: A WordArt gradient text fill using schemeClr (theme) stops must survive a
    /// round-trip: start and end stops must be read as <see cref="WorkbookThemeColorReference"/>
    /// (not null/dropped) and re-emitted as <c>&lt;a:schemeClr&gt;</c> in the writer.
    /// </summary>
    [Fact]
    public void XlsxDrawingPartReader_Reads_WordArt_ThemeGradientStops()
    {
        var drawingXml = XDocument.Parse("""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:oneCellAnchor>
                <xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:ext cx="2700000" cy="900000"/>
                <xdr:sp>
                  <xdr:nvSpPr><xdr:cNvPr id="2" name="WordArt 1"/><xdr:cNvSpPr/></xdr:nvSpPr>
                  <xdr:spPr>
                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="2700000" cy="900000"/></a:xfrm>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/>
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
                              <a:gs pos="0"><a:schemeClr val="accent1"/></a:gs>
                              <a:gs pos="100000"><a:schemeClr val="accent2"/></a:gs>
                            </a:gsLst>
                            <a:lin ang="0" scaled="0"/>
                          </a:gradFill>
                        </a:rPr>
                        <a:t>ThemeGrad</a:t>
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
        shape.ShapeTextThemeColor.Should().NotBeNull("start stop is schemeClr accent1");
        shape.ShapeTextThemeColor.GetValueOrDefault().Slot.Should().Be(WorkbookThemeColorSlot.Accent1);
        shape.ShapeTextGradientEndThemeColor.Should().NotBeNull("end stop is schemeClr accent2");
        shape.ShapeTextGradientEndThemeColor.GetValueOrDefault().Slot.Should().Be(WorkbookThemeColorSlot.Accent2);
        // Explicit color must be null — theme ref takes precedence.
        shape.ShapeTextColor.Should().BeNull("theme-color start stop: explicit color should be null");
        shape.ShapeTextGradientEndColor.Should().BeNull("theme-color end stop: explicit color should be null");
    }

    [Fact]
    public void XlsxAdapter_WordArt_ThemeGradient_SchemeClrPreservedOnRoundTrip()
    {
        // Build a WordArt with theme-color gradient (accent1 → accent2) by setting
        // ShapeTextThemeColor and ShapeTextGradientEndThemeColor directly on the model.
        var workbook = new Workbook("ThemeGradTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 240,
            Height = 80,
            HasFill = false,
            ShapeText = "ThemeGrad",
            ShapeTextFontSizePoints = 36,
            ShapeTextBold = true,
            ShapeTextHAlign = DrawingShapeTextHAlign.Center,
            ShapeTextVAnchor = DrawingShapeTextVAnchor.Middle,
            IsWordArt = true,
            ShapeTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0),
            ShapeTextGradientEndThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, 0),
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        // The written XML must use schemeClr, not srgbClr, for both stops.
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var xml = LoadDrawingXml(archive);
            var rPr = xml.Descendants(DrawingNs + "rPr").Should().ContainSingle().Subject;
            var gradFill = rPr.Element(DrawingNs + "gradFill");
            gradFill.Should().NotBeNull("gradient fill emitted");
            var stops = gradFill!.Descendants(DrawingNs + "gs").ToList();
            stops.Should().HaveCount(2, "two gradient stops");
            stops[0].Element(DrawingNs + "schemeClr").Should().NotBeNull("start stop must be schemeClr (accent1)");
            stops[1].Element(DrawingNs + "schemeClr").Should().NotBeNull("end stop must be schemeClr (accent2)");
        }

        // Reload and verify model — theme colors must survive round-trip.
        stream.Position = 0;
        var loaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).DrawingShapes
            .Should().ContainSingle().Subject;
        loaded.IsWordArt.Should().BeTrue();
        loaded.ShapeTextThemeColor.Should().NotBeNull();
        loaded.ShapeTextThemeColor.GetValueOrDefault().Slot.Should().Be(WorkbookThemeColorSlot.Accent1);
        loaded.ShapeTextGradientEndThemeColor.Should().NotBeNull();
        loaded.ShapeTextGradientEndThemeColor.GetValueOrDefault().Slot.Should().Be(WorkbookThemeColorSlot.Accent2);
    }

    // ── WW5 — solid/single-stop WordArt stays solid across round-trips ────

    /// <summary>
    /// WW5: When a WordArt rPr carries a <c>&lt;gradFill&gt;</c> with only one distinct stop
    /// (or start==end), the reader must NOT synthesise a dummy end color equal to the start.
    /// On re-save, the writer must emit a solid fill, not a degenerate 2-stop gradient.
    /// </summary>
    [Fact]
    public void XlsxDrawingPartReader_SingleStopGradient_ReadAsSolid()
    {
        // A single-stop gradFill: only one <a:gs> present.
        var drawingXml = XDocument.Parse("""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:oneCellAnchor>
                <xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:ext cx="2700000" cy="900000"/>
                <xdr:sp>
                  <xdr:nvSpPr><xdr:cNvPr id="2" name="WordArt 1"/><xdr:cNvSpPr/></xdr:nvSpPr>
                  <xdr:spPr>
                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="2700000" cy="900000"/></a:xfrm>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/>
                  </xdr:spPr>
                  <xdr:txBody>
                    <a:bodyPr anchor="ctr" wrap="square"/>
                    <a:lstStyle/>
                    <a:p>
                      <a:pPr algn="ctr"/>
                      <a:r>
                        <a:rPr lang="en-US" dirty="0" sz="3600">
                          <a:gradFill>
                            <a:gsLst>
                              <a:gs pos="0"><a:srgbClr val="AA1122"/></a:gs>
                            </a:gsLst>
                            <a:lin ang="5400000" scaled="0"/>
                          </a:gradFill>
                        </a:rPr>
                        <a:t>SingleStop</a:t>
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

        shape.ShapeTextColor.Should().Be(new CellColor(0xAA, 0x11, 0x22));
        // Key assertion: no synthesised end color — must be null so the writer falls back to solid.
        shape.ShapeTextGradientEndColor.Should().BeNull("single-stop gradient must not synthesise an end stop");
        shape.ShapeTextGradientEndThemeColor.Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_WordArtSolid_StaysSolid_NoGradientEmitted()
    {
        // A solid-fill WordArt (no gradient end) must save as solidFill, not gradFill.
        var workbook = CreateWordArtWorkbook(
            isWordArt: true,
            warpPreset: "textWave1",
            textColor: new CellColor(0xFF, 0x45, 0x00),
            gradEndColor: null,          // no gradient
            outlineColor: null,
            outlineWidthPt: 0);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var xml = LoadDrawingXml(archive);
        var rPr = xml.Descendants(DrawingNs + "rPr").Should().ContainSingle().Subject;
        rPr.Element(DrawingNs + "gradFill").Should().BeNull("solid WordArt must not emit gradFill");
        rPr.Element(DrawingNs + "solidFill").Should().NotBeNull("solid WordArt must emit solidFill");
    }

    // ── WW6 — gradient direction angle round-trips ────────────────────────

    /// <summary>
    /// WW6: The authored <c>&lt;a:lin ang="..."&gt;</c> value must survive a read→write round-trip.
    /// A horizontal gradient (ang=0) must not be reoriented to vertical (ang=5400000).
    /// </summary>
    [Fact]
    public void XlsxDrawingPartReader_Reads_WordArt_GradientAngle()
    {
        var drawingXml = XDocument.Parse("""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:oneCellAnchor>
                <xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:ext cx="2700000" cy="900000"/>
                <xdr:sp>
                  <xdr:nvSpPr><xdr:cNvPr id="2" name="WordArt 1"/><xdr:cNvSpPr/></xdr:nvSpPr>
                  <xdr:spPr>
                    <a:xfrm><a:off x="0" y="0"/><a:ext cx="2700000" cy="900000"/></a:xfrm>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/>
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
                            <a:lin ang="0" scaled="0"/>
                          </a:gradFill>
                        </a:rPr>
                        <a:t>HorizGrad</a:t>
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

        shape.ShapeTextGradientAngle.Should().Be(0, "ang=0 (horizontal) must be captured");
    }

    [Fact]
    public void XlsxAdapter_WordArt_GradientAngle_RoundTrips()
    {
        // Build a WordArt with a horizontal gradient (ang=0) and verify the angle survives save/load.
        var workbook = new Workbook("GradAngleTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 240,
            Height = 80,
            HasFill = false,
            ShapeText = "HorizGrad",
            ShapeTextFontSizePoints = 36,
            ShapeTextHAlign = DrawingShapeTextHAlign.Center,
            ShapeTextVAnchor = DrawingShapeTextVAnchor.Middle,
            IsWordArt = true,
            ShapeTextColor = new CellColor(0xFF, 0x00, 0x00),
            ShapeTextGradientEndColor = new CellColor(0x00, 0x00, 0xFF),
            ShapeTextGradientAngle = 0,  // horizontal: left-to-right
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);

        // XML: ang attribute must be "0".
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var xml = LoadDrawingXml(archive);
            var linEl = xml.Descendants(DrawingNs + "lin").FirstOrDefault(
                e => e.Parent?.Name.LocalName == "gradFill" && e.Parent?.Parent?.Name.LocalName == "rPr");
            linEl.Should().NotBeNull("<a:lin> under rPr/gradFill must be present");
            linEl!.Attribute("ang")?.Value.Should().Be("0", "authored angle 0 must be written as-is");
        }

        // Reload: angle must come back as 0.
        stream.Position = 0;
        var loaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).DrawingShapes
            .Should().ContainSingle().Subject;
        loaded.ShapeTextGradientAngle.Should().Be(0, "gradient angle must survive round-trip");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static List<string> SchemaErrors(Stream stream)
    {
        stream.Position = 0;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        using var document = SpreadsheetDocument.Open(copy, isEditable: false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(e => e.ErrorType == ValidationErrorType.Schema)
            .Select(e => $"{e.Description} @ {e.Path?.XPath}")
            .ToList();
    }

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
