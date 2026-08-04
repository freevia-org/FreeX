using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for <c>w:docDefaults/w:rPrDefault/w:rPr</c> — the document-default run
/// properties (body font family + size). Without this fix, the document default font (e.g. Calibri 11pt)
/// stored only in docDefaults was silently dropped on save; Word then fell back to Times New Roman.
/// </summary>
public class DocDefaultsRoundTripTests
{
    private const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string W14ns = "http://schemas.microsoft.com/office/word/2010/wordml";
    private static readonly XNamespace W = Wns;
    private static readonly XNamespace W14 = W14ns;

    private static void AddPart(ZipArchive zip, string path, string xml)
    {
        var e = zip.CreateEntry(path);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(xml);
    }

    /// <summary>
    /// Reads a docx whose styles.xml has the given rPrDefault and pPrDefault XML fragments inside
    /// w:docDefaults. Pass null for either to omit it.
    /// </summary>
    private static TextDocument Read(string? rPrDefaultXml, string? pPrDefaultXml = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddPart(zip, "word/document.xml",
                $"<w:document xmlns:w=\"{Wns}\"><w:body><w:p><w:r><w:t>body</w:t></w:r></w:p></w:body></w:document>");

            var rPrPart = rPrDefaultXml is null ? "" : $"<w:rPrDefault>{rPrDefaultXml}</w:rPrDefault>";
            var pPrPart = pPrDefaultXml is null ? "" : $"<w:pPrDefault>{pPrDefaultXml}</w:pPrDefault>";
            AddPart(zip, "word/styles.xml",
                $"<w:styles xmlns:w=\"{Wns}\" xmlns:w14=\"{W14ns}\"><w:docDefaults>{rPrPart}{pPrPart}</w:docDefaults></w:styles>");
        }
        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    /// <summary>
    /// Writes a document and extracts its styles.xml as an XDocument so tests can assert on the raw XML.
    /// </summary>
    private static XDocument WriteStylesXml(TextDocument document)
    {
        using var ms = new MemoryStream();
        DocxWriter.Write(document, ms);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/styles.xml")!.Open();
        return XDocument.Load(entry);
    }

    /// <summary>
    /// The canonical round-trip: a docx whose only font specification is in w:docDefaults (Calibri, 22
    /// half-points = 11 pt) must survive a FreeW read→write cycle with docDefaults intact: same font name,
    /// same size value, and docDefaults must be the FIRST child of w:styles (schema order).
    /// </summary>
    [Fact]
    public void DocDefaults_FontAndSize_RoundTrip()
    {
        // ARRANGE — docDefaults with Calibri 11pt (22 half-points)
        var doc = Read("<w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/><w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/></w:rPr>");

        // The reader must populate DefaultRun correctly.
        doc.DefaultRun.FontFamily.Should().Be("Calibri");
        doc.DefaultRun.FontSizePt.Should().Be(11);
        doc.UseWordApplicationDefaultRunFormatting.Should().BeFalse();

        // WRITE and inspect styles.xml
        var stylesXml = WriteStylesXml(doc);
        var root = stylesXml.Root!;

        // docDefaults MUST be the first child of w:styles
        root.Elements().First().Name.Should().Be(W + "docDefaults",
            "w:docDefaults must precede all w:style elements (CT_Styles schema order)");

        var docDefaults = root.Element(W + "docDefaults");
        docDefaults.Should().NotBeNull("docDefaults must be emitted");

        var rPr = docDefaults!.Element(W + "rPrDefault")?.Element(W + "rPr");
        rPr.Should().NotBeNull("w:rPrDefault/w:rPr must be present");

        // Font family
        var rFonts = rPr!.Element(W + "rFonts");
        rFonts.Should().NotBeNull("w:rFonts must be emitted for the default font");
        rFonts!.Attribute(W + "ascii")?.Value.Should().Be("Calibri");
        rFonts.Attribute(W + "hAnsi")?.Value.Should().Be("Calibri");

        // Size — w:sz val should be 22 (half-points for 11pt)
        rPr.Element(W + "sz")?.Attribute(W + "val")?.Value.Should().Be("22",
            "11pt = 22 half-points in w:sz/@w:val");
    }

    /// <summary>
    /// Aptos 12pt (24 half-points) — the default body font for newer Word documents. Ensures the fix is not
    /// Calibri-specific.
    /// </summary>
    [Fact]
    public void DocDefaults_Aptos12pt_RoundTrip()
    {
        var doc = Read("<w:rPr><w:rFonts w:ascii=\"Aptos\" w:hAnsi=\"Aptos\"/><w:sz w:val=\"24\"/><w:szCs w:val=\"24\"/></w:rPr>");

        doc.DefaultRun.FontFamily.Should().Be("Aptos");
        doc.DefaultRun.FontSizePt.Should().Be(12);

        var stylesXml = WriteStylesXml(doc);
        var rPr = stylesXml.Root!
            .Element(W + "docDefaults")!
            .Element(W + "rPrDefault")!
            .Element(W + "rPr")!;

        rPr.Element(W + "rFonts")!.Attribute(W + "ascii")!.Value.Should().Be("Aptos");
        rPr.Element(W + "sz")!.Attribute(W + "val")!.Value.Should().Be("24");
    }

    [Fact]
    public void MissingRunDefaults_UsesWordApplicationTwelvePointFallback()
    {
        var doc = Read(rPrDefaultXml: null);

        doc.DefaultRun.FontFamily.Should().Be("Calibri");
        doc.DefaultRun.FontSizePt.Should().Be(12);
        doc.UseWordApplicationDefaultRunFormatting.Should().BeTrue();
    }

    /// <summary>
    /// A document built by FreeW from scratch (no read, just DefaultRun = Calibri 11pt) must also emit
    /// docDefaults on write so the body font is preserved for Word.
    /// </summary>
    [Fact]
    public void NewDocument_DefaultRun_IsEmittedAsDocDefaults()
    {
        var doc = new TextDocument();
        doc.DefaultRun = doc.DefaultRun with { FontFamily = "Calibri", FontSizePt = 11 };
        doc.Blocks.Add(new Paragraph("hello"));

        var stylesXml = WriteStylesXml(doc);
        var docDefaults = stylesXml.Root?.Element(W + "docDefaults");
        docDefaults.Should().NotBeNull("NewDocument with non-trivial DefaultRun must emit docDefaults");

        var rFonts = docDefaults!
            .Element(W + "rPrDefault")?.Element(W + "rPr")?.Element(W + "rFonts");
        rFonts.Should().NotBeNull();
        rFonts!.Attribute(W + "ascii")?.Value.Should().Be("Calibri");
    }

    /// <summary>
    /// Full read→write→read cycle: the document re-read after saving must still have the same DefaultRun.
    /// </summary>
    [Fact]
    public void DocDefaults_ReadWriteRead_Stable()
    {
        var doc1 = Read("<w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/><w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/></w:rPr>");

        using var ms = new MemoryStream();
        DocxWriter.Write(doc1, ms);
        ms.Position = 0;
        var doc2 = DocxReader.Read(ms);

        doc2.DefaultRun.FontFamily.Should().Be("Calibri");
        doc2.DefaultRun.FontSizePt.Should().Be(11);
    }

    [Fact]
    public void DocDefaults_ThemeLinkedColor_RoundTrips()
    {
        var document = Read(
            "<w:rPr><w:color w:val=\"1F4E79\" w:themeColor=\"accent1\" " +
            "w:themeShade=\"80\"/></w:rPr>");

        document.DefaultRun.ColorHex.Should().Be("#1F4E79");
        document.DefaultRun.ThemeColor.Should().Be(new WordThemeColor("accent1", "1F4E79", ShadeHex: "80"));

        var color = WriteStylesXml(document).Descendants(W + "color").Single();
        color.Attribute(W + "val")!.Value.Should().Be("1F4E79");
        color.Attribute(W + "themeColor")!.Value.Should().Be("accent1");
        color.Attribute(W + "themeShade")!.Value.Should().Be("80");
    }

    [Fact]
    public void DocDefaults_ClassicCharacterProperties_RoundTripInCanonicalOrder()
    {
        var doc = Read(
            "<w:rPr>" +
            "<w:rFonts w:ascii=\"Aptos\"/><w:b/><w:i/>" +
            "<w:caps/><w:smallCaps/><w:strike/><w:dstrike/>" +
            "<w:noProof/><w:vanish/><w:webHidden/><w:color w:val=\"123456\"/>" +
            "<w:sz w:val=\"22\"/>" +
            "<w:u w:val=\"single\"/>" +
            "<w:vertAlign w:val=\"superscript\"/><w:rtl/><w:lang w:val=\"ar-SA\"/>" +
            "</w:rPr>");

        doc.DefaultRun.AllCaps.Should().BeTrue();
        doc.DefaultRun.SmallCaps.Should().BeTrue();
        doc.DefaultRun.Strikethrough.Should().BeTrue();
        doc.DefaultRun.Underline.Should().BeTrue();
        doc.DefaultRun.VerticalAlign.Should().Be(VerticalAlign.Superscript);
        doc.DefaultRun.Rtl.Should().BeTrue();

        var stylesXml = WriteStylesXml(doc);
        var rPr = stylesXml.Root!
            .Element(W + "docDefaults")!
            .Element(W + "rPrDefault")!
            .Element(W + "rPr")!;

        rPr.Elements().Select(element => element.Name.LocalName).Should().Equal(
            "rFonts", "b", "i", "caps", "smallCaps", "strike", "dstrike", "noProof",
            "vanish", "webHidden", "color", "sz", "szCs", "u", "vertAlign", "rtl", "lang");
        rPr.Element(W + "u")!.Attribute(W + "val")!.Value.Should().Be("single");
        rPr.Element(W + "vertAlign")!.Attribute(W + "val")!.Value.Should().Be("superscript");

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        var reopened = DocxReader.Read(stream);
        reopened.DefaultRun.AllCaps.Should().BeTrue();
        reopened.DefaultRun.SmallCaps.Should().BeTrue();
        reopened.DefaultRun.Strikethrough.Should().BeTrue();
        reopened.DefaultRun.Underline.Should().BeTrue();
        reopened.DefaultRun.VerticalAlign.Should().Be(VerticalAlign.Superscript);
        reopened.DefaultRun.Rtl.Should().BeTrue();
    }

    [Fact]
    public void DocDefaults_AdvancedTypography_RoundTripsInCoreThenExtensionOrder()
    {
        var doc = Read(
            "<w:rPr>" +
            "<w:spacing w:val=\"30\"/><w:kern w:val=\"24\"/><w:position w:val=\"-4\"/>" +
            "<w14:ligatures w14:val=\"standardContextual\"/>" +
            "<w14:numForm w14:val=\"oldStyle\"/><w14:numSpacing w14:val=\"tabular\"/>" +
            "<w14:stylisticSets><w14:styleSet w14:id=\"7\"/></w14:stylisticSets>" +
            "</w:rPr>");

        doc.DefaultRun.CharacterSpacingPt.Should().Be(1.5);
        doc.DefaultRun.KerningMinSizePt.Should().Be(12);
        doc.DefaultRun.PositionPt.Should().Be(-2);
        doc.DefaultRun.Ligatures.Should().Be(LigatureMode.StandardContextual);
        doc.DefaultRun.NumberForm.Should().Be(NumberForm.OldStyle);
        doc.DefaultRun.NumberSpacing.Should().Be(NumberSpacing.Tabular);
        doc.DefaultRun.StylisticSet.Should().Be(7);

        var stylesXml = WriteStylesXml(doc);
        var rPr = stylesXml.Root!
            .Element(W + "docDefaults")!
            .Element(W + "rPrDefault")!
            .Element(W + "rPr")!;

        rPr.Elements().Select(element => element.Name.LocalName).Should().Equal(
            "rFonts", "spacing", "kern", "position", "sz", "szCs",
            "ligatures", "numForm", "numSpacing", "stylisticSets");
        rPr.Element(W + "spacing")!.Attribute(W + "val")!.Value.Should().Be("30");
        rPr.Element(W + "kern")!.Attribute(W + "val")!.Value.Should().Be("24");
        rPr.Element(W + "position")!.Attribute(W + "val")!.Value.Should().Be("-4");
        rPr.Element(W14 + "stylisticSets")!
            .Element(W14 + "styleSet")!
            .Attribute(W14 + "id")!.Value.Should().Be("7");

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        var reopened = DocxReader.Read(stream);
        reopened.DefaultRun.CharacterSpacingPt.Should().Be(1.5);
        reopened.DefaultRun.KerningMinSizePt.Should().Be(12);
        reopened.DefaultRun.PositionPt.Should().Be(-2);
        reopened.DefaultRun.Ligatures.Should().Be(LigatureMode.StandardContextual);
        reopened.DefaultRun.NumberForm.Should().Be(NumberForm.OldStyle);
        reopened.DefaultRun.NumberSpacing.Should().Be(NumberSpacing.Tabular);
        reopened.DefaultRun.StylisticSet.Should().Be(7);
    }

    [Fact]
    public void DocDefaults_NamedHighlight_RoundTripsWithCanonicalHighlightAndShadingFallback()
    {
        var doc = Read("<w:rPr><w:highlight w:val=\"yellow\"/></w:rPr>");

        doc.DefaultRun.HighlightColorHex.Should().Be("#FFFF00");
        doc.DefaultRun.CharacterShadingHex.Should().BeNull();

        var stylesXml = WriteStylesXml(doc);
        var rPr = stylesXml.Root!
            .Element(W + "docDefaults")!
            .Element(W + "rPrDefault")!
            .Element(W + "rPr")!;

        rPr.Elements().Select(element => element.Name.LocalName).Should().Equal(
            "rFonts", "sz", "szCs", "highlight", "shd");
        rPr.Element(W + "highlight")!.Attribute(W + "val")!.Value.Should().Be("yellow");
        rPr.Element(W + "shd")!.Attribute(W + "fill")!.Value.Should().Be("FFFF00");

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        var reopened = DocxReader.Read(stream);
        reopened.DefaultRun.HighlightColorHex.Should().Be("#FFFF00");
        reopened.DefaultRun.CharacterShadingHex.Should().BeNull();
    }

    [Fact]
    public void DocDefaults_PatternedCharacterShading_RoundTripsWithoutBecomingHighlight()
    {
        var doc = Read("<w:rPr><w:shd w:val=\"pct25\" w:color=\"auto\" w:fill=\"ABCDEF\"/></w:rPr>");

        doc.DefaultRun.HighlightColorHex.Should().BeNull();
        doc.DefaultRun.CharacterShadingHex.Should().Be("#ABCDEF");
        doc.DefaultRun.CharacterShadingPattern.Should().Be(ShadingPattern.Pct25);

        var stylesXml = WriteStylesXml(doc);
        var rPr = stylesXml.Root!
            .Element(W + "docDefaults")!
            .Element(W + "rPrDefault")!
            .Element(W + "rPr")!;

        rPr.Elements().Select(element => element.Name.LocalName).Should().Equal(
            "rFonts", "sz", "szCs", "shd");
        rPr.Element(W + "highlight").Should().BeNull();
        rPr.Element(W + "shd")!.Attribute(W + "val")!.Value.Should().Be("pct25");
        rPr.Element(W + "shd")!.Attribute(W + "fill")!.Value.Should().Be("ABCDEF");

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        var reopened = DocxReader.Read(stream);
        reopened.DefaultRun.HighlightColorHex.Should().BeNull();
        reopened.DefaultRun.CharacterShadingHex.Should().Be("#ABCDEF");
        reopened.DefaultRun.CharacterShadingPattern.Should().Be(ShadingPattern.Pct25);
    }
}
