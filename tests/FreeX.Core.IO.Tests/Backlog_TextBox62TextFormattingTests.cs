using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// backlog textbox-6-2 (deferred since round 62): a loaded text box's rich-text formatting (font
/// size, bold, italic, color, alignment) was lost because <see cref="TextBoxModel"/> had no fields
/// to carry it -- so Duplicate Sheet stripped it, and a real xlsx load could never populate it.
/// These tests cover the read side (<see cref="XlsxWorksheetDrawingPartReader"/> populating the new
/// <c>TextBoxModel</c> fields from the txBody) and the xlsx write side (round-tripping those fields
/// through <see cref="XlsxFileAdapter"/> save/reload). Duplicate Sheet coverage lives alongside the
/// sibling K22 tests in RDrawObjectsRegressionTests.cs (FreeX.Core.Model.Tests).
/// </summary>
public sealed class Backlog_TextBox62TextFormattingTests
{
    [Fact]
    public void XlsxDrawingPartReader_ParsesTextBoxFormattingFromTxBody()
    {
        // Before the fix: XlsxTextBoxPackagePart/TextBoxModel had no fields for any of this --
        // the reader discarded the rPr/pPr/bodyPr formatting entirely for a true text box (the
        // shape branch already read it, but ReadSpElement's isTxBox branch returned early before
        // ever calling ReadShapeTextFormatting).
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
                  <xdr:txBody>
                    <a:bodyPr anchor="b" wrap="square"/>
                    <a:lstStyle/>
                    <a:p>
                      <a:pPr algn="r"/>
                      <a:r>
                        <a:rPr sz="1800" b="1">
                          <a:solidFill><a:srgbClr val="112233"/></a:solidFill>
                          <a:latin typeface="Georgia"/>
                        </a:rPr>
                        <a:t>Warning</a:t>
                      </a:r>
                    </a:p>
                  </xdr:txBody>
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

        textBox.Text.Should().Be("Warning");
        textBox.TextFontFamily.Should().Be("Georgia");
        textBox.TextFontSizePoints.Should().Be(18);
        textBox.TextBold.Should().BeTrue();
        textBox.TextItalic.Should().BeFalse();
        textBox.TextColor.Should().Be(new CellColor(0x11, 0x22, 0x33));
        textBox.TextHAlign.Should().Be(DrawingShapeTextHAlign.Right);
        textBox.TextVAnchor.Should().Be(DrawingShapeTextVAnchor.Bottom);
    }

    [Fact]
    public void XlsxDrawingPartReader_PlainTextBoxWithNoFormatting_ReadsDefaultFormattingFields()
    {
        // No-regression sibling: an ordinary text box with no rPr/pPr/bodyPr formatting must still
        // load cleanly with the new fields at their "nothing authored" defaults, not throw or
        // misclassify the object.
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

        textBox.Text.Should().Be("Plain");
        textBox.TextFontFamily.Should().BeNull();
        textBox.TextFontSizePoints.Should().Be(0);
        textBox.TextBold.Should().BeFalse();
        textBox.TextItalic.Should().BeFalse();
        textBox.TextColor.Should().BeNull();
        textBox.TextThemeColor.Should().BeNull();
        textBox.TextHAlign.Should().Be(DrawingShapeTextHAlign.Left);
    }

    [Fact]
    public void XlsxAdapter_RoundTripsTextBoxFontProperties()
    {
        // Write-side coverage: XlsxWorksheetDrawingObjectWriter.ToOneCellTextBoxAnchor previously
        // emitted a bare <a:r><a:t> run with no rPr/pPr/bodyPr formatting at all, so even a freshly
        // authored (never-loaded) formatted text box lost its formatting on save. Verify the
        // formatting survives a full save -> reload round trip.
        var workbook = new Workbook("TextBoxFormatting");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var textColor = new CellColor(0x11, 0x22, 0x33);
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Text = "Warning",
            TextFontFamily = "Georgia",
            TextFontSizePoints = 18,
            TextBold = true,
            TextItalic = true,
            TextColor = textColor,
            TextHAlign = DrawingShapeTextHAlign.Right,
            TextVAnchor = DrawingShapeTextVAnchor.Bottom
        });

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);

        // Verify the on-disk XML actually carries the formatting (not just the in-memory reload).
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(
                archive, "xl/drawings/drawing1.xml", "the XLSX package should contain xl/drawings/drawing1.xml");
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

            var txBody = drawingXml.Descendants(xdr + "txBody").Should().ContainSingle().Subject;
            txBody.Element(a + "bodyPr")!.Attribute("anchor")!.Value.Should().Be("b");
            txBody.Descendants(a + "pPr").Should().ContainSingle().Which.Attribute("algn")!.Value.Should().Be("r");

            var rPr = txBody.Descendants(a + "rPr").Should().ContainSingle().Subject;
            rPr.Attribute("sz")!.Value.Should().Be("1800");
            rPr.Attribute("b")!.Value.Should().Be("1");
            rPr.Attribute("i")!.Value.Should().Be("1");
            rPr.Element(a + "latin")!.Attribute("typeface")!.Value.Should().Be("Georgia");
        }

        stream.Position = 0;
        var reloaded = adapter.Load(stream).GetSheetAt(0).TextBoxes.Should().ContainSingle().Subject;
        reloaded.Text.Should().Be("Warning");
        reloaded.TextFontFamily.Should().Be("Georgia");
        reloaded.TextFontSizePoints.Should().Be(18);
        reloaded.TextBold.Should().BeTrue();
        reloaded.TextItalic.Should().BeTrue();
        reloaded.TextColor.Should().Be(textColor);
        reloaded.TextHAlign.Should().Be(DrawingShapeTextHAlign.Right);
        reloaded.TextVAnchor.Should().Be(DrawingShapeTextVAnchor.Bottom);
    }

    [Fact]
    public void XlsxAdapter_RoundTripsPlainUnformattedTextBox_NoRegression()
    {
        // No-regression sibling: a plain text box with no explicit formatting must still save and
        // reload with its text intact and the new fields at their harmless defaults -- the writer's
        // now-unconditional rPr/pPr/bodyPr emission must not corrupt or lose the text itself.
        var workbook = new Workbook("PlainTextBox");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Text = "Plain note"
        });

        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var reloaded = adapter.Load(stream).GetSheetAt(0).TextBoxes.Should().ContainSingle().Subject;
        reloaded.Text.Should().Be("Plain note");
        reloaded.TextFontFamily.Should().BeNull();
        reloaded.TextFontSizePoints.Should().Be(0);
        reloaded.TextBold.Should().BeFalse();
        reloaded.TextItalic.Should().BeFalse();
        reloaded.TextColor.Should().BeNull();
        reloaded.TextHAlign.Should().Be(DrawingShapeTextHAlign.Left);
        reloaded.TextVAnchor.Should().Be(DrawingShapeTextVAnchor.Top,
            "the writer now explicitly emits bodyPr@anchor=\"t\" for TextBoxModel's default (Top), " +
            "matching a plain Excel-authored text box's own default anchor");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTripsTextBoxFontProperties()
    {
        // Residual gap flagged by the round-67 textbox-rich-text fixer: TextBoxModel gained the 8
        // text-format fields (font family/size, bold, italic, color, alignment) and the XLSX
        // reader/writer round-trip them (see the XlsxAdapter tests above), but the native .fxl
        // JSON adapter's TextBoxDto/mapper never carried them -- so a .fxl save/reload silently
        // lost a text box's formatting even though XLSX preserved it. Before the fix, every
        // assertion below came back at its default (font null, size 0, bold/italic false, etc.).
        var workbook = new Workbook("TextBoxFormatting");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var textColor = new CellColor(0x11, 0x22, 0x33);
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Text = "Warning",
            TextFontFamily = "Georgia",
            TextFontSizePoints = 18,
            TextBold = true,
            TextItalic = true,
            TextColor = textColor,
            TextHAlign = DrawingShapeTextHAlign.Right,
            TextVAnchor = DrawingShapeTextVAnchor.Bottom
        });

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var reloaded = adapter.Load(stream).GetSheetAt(0).TextBoxes.Should().ContainSingle().Subject;
        reloaded.Text.Should().Be("Warning");
        reloaded.TextFontFamily.Should().Be("Georgia");
        reloaded.TextFontSizePoints.Should().Be(18);
        reloaded.TextBold.Should().BeTrue();
        reloaded.TextItalic.Should().BeTrue();
        reloaded.TextColor.Should().Be(textColor);
        reloaded.TextHAlign.Should().Be(DrawingShapeTextHAlign.Right);
        reloaded.TextVAnchor.Should().Be(DrawingShapeTextVAnchor.Bottom);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTripsPlainUnformattedTextBox_NoRegression()
    {
        // No-regression sibling: a plain text box with no explicit formatting must still round-trip
        // through the native JSON adapter with its text intact and the new fields at their harmless
        // defaults.
        var workbook = new Workbook("PlainTextBox");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Text = "Plain note"
        });

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var reloaded = adapter.Load(stream).GetSheetAt(0).TextBoxes.Should().ContainSingle().Subject;
        reloaded.Text.Should().Be("Plain note");
        reloaded.TextFontFamily.Should().BeNull();
        reloaded.TextFontSizePoints.Should().Be(0);
        reloaded.TextBold.Should().BeFalse();
        reloaded.TextItalic.Should().BeFalse();
        reloaded.TextColor.Should().BeNull();
        reloaded.TextThemeColor.Should().BeNull();
        reloaded.TextHAlign.Should().Be(DrawingShapeTextHAlign.Left);
        reloaded.TextVAnchor.Should().Be(DrawingShapeTextVAnchor.Top);
    }
}
