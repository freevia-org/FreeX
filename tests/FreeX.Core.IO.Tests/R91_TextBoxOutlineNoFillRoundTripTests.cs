using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R91-commands-insert-object-5-1 (xlsx side): an authored text box's explicit
/// <c>&lt;a:ln&gt;&lt;a:noFill/&gt;</c> (no border) was computed on read (<c>outlineHasNoFill</c> in
/// <c>XlsxWorksheetDrawingParts.ReadSpElement</c>) but never threaded into
/// <c>XlsxTextBoxPackagePart</c>/<see cref="TextBoxModel"/>, so a borderless imported text box
/// regained the fallback gray border on render, and the writer permanently baked that border back
/// in on re-save (it never passed <c>outlineHasNoFill</c> to <c>ToShapePropertiesForDrawingObject</c>
/// at all). These tests cover the read side and a full save -&gt; reload round trip.
/// </summary>
public sealed class R91_TextBoxOutlineNoFillRoundTripTests
{
    [Fact]
    public void XlsxDrawingPartReader_ParsesTextBoxExplicitNoLine()
    {
        var drawingXml = XDocument.Parse("""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:oneCellAnchor>
                <xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff>
                          <xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:ext cx="914400" cy="457200"/>
                <xdr:sp>
                  <xdr:nvSpPr>
                    <xdr:cNvPr id="2" name="TextBox 1"/>
                    <xdr:cNvSpPr txBox="1"/>
                  </xdr:nvSpPr>
                  <xdr:spPr>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                    <a:ln><a:noFill/></a:ln>
                  </xdr:spPr>
                  <xdr:txBody><a:bodyPr/><a:p><a:r><a:t>Note</a:t></a:r></a:p></xdr:txBody>
                </xdr:sp>
                <xdr:clientData/>
              </xdr:oneCellAnchor>
            </xdr:wsDr>
            """);

        var textBox = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml)
            .TextBoxes
            .Should()
            .ContainSingle()
            .Subject;

        textBox.OutlineHasNoFill.Should().BeTrue("the source <a:ln><a:noFill/> explicitly suppresses the border");
    }

    /// <summary>No-regression sibling: a text box with no <c>&lt;a:ln&gt;</c> override at all must
    /// still read as "has a line" (the safe/back-compat default), not silently lose its border.</summary>
    [Fact]
    public void XlsxDrawingPartReader_PlainTextBoxWithNoLnElement_ReadsOutlineHasNoFillFalse()
    {
        var drawingXml = XDocument.Parse("""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:oneCellAnchor>
                <xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff>
                          <xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:ext cx="914400" cy="457200"/>
                <xdr:sp>
                  <xdr:nvSpPr>
                    <xdr:cNvPr id="2" name="TextBox 1"/>
                    <xdr:cNvSpPr txBox="1"/>
                  </xdr:nvSpPr>
                  <xdr:spPr>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                  </xdr:spPr>
                  <xdr:txBody><a:bodyPr/><a:p><a:r><a:t>Plain</a:t></a:r></a:p></xdr:txBody>
                </xdr:sp>
                <xdr:clientData/>
              </xdr:oneCellAnchor>
            </xdr:wsDr>
            """);

        var textBox = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml)
            .TextBoxes
            .Should()
            .ContainSingle()
            .Subject;

        textBox.OutlineHasNoFill.Should().BeFalse();
    }

    [Fact]
    public void XlsxAdapter_RoundTripsTextBoxExplicitNoLine()
    {
        var workbook = new Workbook("TextBoxNoLine");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Text = "Note",
            OutlineHasNoFill = true
        });

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(
                archive, "xl/drawings/drawing1.xml", "the XLSX package should contain xl/drawings/drawing1.xml");
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

            var spPr = drawingXml.Descendants(xdr + "sp").Should().ContainSingle().Subject
                .Element(xdr + "spPr")!;
            var ln = spPr.Element(a + "ln");
            ln.Should().NotBeNull("an explicitly line-suppressed text box must round-trip as <a:ln><a:noFill/>");
            ln!.Element(a + "noFill").Should().NotBeNull();
        }

        stream.Position = 0;
        var reloaded = adapter.Load(stream).GetSheetAt(0).TextBoxes.Should().ContainSingle().Subject;
        reloaded.OutlineHasNoFill.Should().BeTrue();
    }

    /// <summary>No-regression sibling: a text box with an authored outline color (the ordinary,
    /// far more common case) must still round-trip that border, not have it stripped by the new
    /// no-line plumbing.</summary>
    [Fact]
    public void XlsxAdapter_RoundTripsTextBoxWithAuthoredOutlineColor()
    {
        var workbook = new Workbook("TextBoxWithLine");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var outlineColor = new CellColor(0x40, 0x50, 0x60);
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Text = "Bordered",
            OutlineColor = outlineColor
        });

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        var reloaded = adapter.Load(stream).GetSheetAt(0).TextBoxes.Should().ContainSingle().Subject;
        reloaded.OutlineHasNoFill.Should().BeFalse();
        reloaded.OutlineColor.Should().Be(outlineColor);
    }
}
