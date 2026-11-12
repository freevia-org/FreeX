using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Header/footer markup the corpus showed being mis-read: a PAGE field whose fldChar runs sit inside
/// tracked insertions (stress023), and a legacy VML circle holding the page number in the bottom margin
/// (stress015/stress018, Word's "Page Numbers (Margins)" gallery).
/// </summary>
public class HeaderFooterFieldAndFrameReadTests
{
    private const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Rns = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Vns = "urn:schemas-microsoft-com:vml";

    internal static TextDocument ReadWithFooter(string footerInnerXml, string bodyXml = "<w:p><w:r><w:t>Body</w:t></w:r></w:p>")
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var entry = zip.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }

            Add("word/document.xml",
                $"<w:document xmlns:w=\"{Wns}\" xmlns:r=\"{Rns}\"><w:body>{bodyXml}" +
                "<w:sectPr><w:footerReference w:type=\"default\" r:id=\"rIdF\"/>" +
                "<w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"720\" w:right=\"720\" w:bottom=\"720\" w:left=\"720\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/>" +
                "</w:sectPr></w:body></w:document>");
            Add("word/_rels/document.xml.rels",
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rIdF\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/>" +
                "</Relationships>");
            Add("word/footer1.xml",
                $"<w:ftr xmlns:w=\"{Wns}\" xmlns:r=\"{Rns}\" xmlns:v=\"{Vns}\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:w10=\"urn:schemas-microsoft-com:office:word\">{footerInnerXml}</w:ftr>");
        }

        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    [Fact]
    public void PageFieldWithFieldCharactersInsideTrackedInsertions_ReadsAsOneField()
    {
        var doc = ReadWithFooter(
            "<w:p>" +
            "<w:ins w:id=\"1\" w:author=\"A\"><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
            "<w:r><w:instrText xml:space=\"preserve\"> PAGE   \\* MERGEFORMAT </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r></w:ins>" +
            "<w:r><w:t>1</w:t></w:r>" +
            "<w:ins w:id=\"2\" w:author=\"A\"><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:ins>" +
            "<w:r><w:t xml:space=\"preserve\"> - Footer text</w:t></w:r></w:p>");

        var footer = doc.FinalSectionHeadersFooters.Footer;
        footer.Should().NotBeNull();
        var runs = footer!.Paragraphs.Single().Runs;
        var field = runs.Single(run => run.ComplexField is not null);
        field.ComplexField!.Instruction.Trim().Should().StartWith("PAGE");
        field.Text.Should().Be("1");
        runs.Where(run => run.ComplexField is null).Select(run => run.Text)
            .Should().Equal(" - Footer text");
    }

    internal const string MarginCirclePageNumberFooter =
        "<w:p><w:r><w:pict><v:oval id=\"_x0000_s2049\" style=\"position:absolute;margin-left:0;margin-top:0;width:49.35pt;height:49.35pt;" +
        "z-index:251658240;mso-position-horizontal:center;mso-position-horizontal-relative:margin;mso-position-vertical:center;" +
        "mso-position-vertical-relative:bottom-margin-area;v-text-anchor:middle\" fillcolor=\"#365f91 [2404]\" stroked=\"f\">" +
        "<v:textbox inset=\",0,,0\"><w:txbxContent><w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr>" +
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> PAGE </w:instrText></w:r>" +
        "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>" +
        "</w:p></w:txbxContent></v:textbox><w10:wrap anchorx=\"margin\" anchory=\"page\"/></v:oval></w:pict></w:r></w:p>" +
        "<w:p><w:r><w:t>Plain footer</w:t></w:r></w:p>";

    [Fact]
    public void PositionedVmlTextBox_TagsItsFlattenedParagraphsWithTheShapeFrame()
    {
        var doc = ReadWithFooter(MarginCirclePageNumberFooter);

        var paragraphs = doc.FinalSectionHeadersFooters.Footer!.Paragraphs;
        var framed = paragraphs.Single(paragraph => paragraph.Runs.Any(run => run.ComplexField is not null));
        var frame = framed.LegacyTextBoxFrame;
        frame.Should().NotBeNull();
        frame!.IsEllipse.Should().BeTrue();
        frame.WidthPt.Should().BeApproximately(49.35, 0.001);
        frame.FillColorHex.Should().Be("#365F91");
        frame.StrokeColorHex.Should().BeNull();
        frame.HorizontalAlignment.Should().Be(LegacyFrameHorizontalAlignment.Center);
        frame.HorizontalReference.Should().Be(LegacyFrameReference.Margin);
        frame.VerticalAlignment.Should().Be(LegacyFrameVerticalAlignment.Center);
        frame.VerticalReference.Should().Be(LegacyFrameReference.BottomMarginArea);
        frame.TextAnchor.Should().Be(LegacyFrameTextAnchor.Middle);
        frame.InsetTopPt.Should().Be(0);
        frame.InsetLeftPt.Should().BeApproximately(7.2, 0.001);
        paragraphs.Single(paragraph => paragraph.PlainText == "Plain footer").LegacyTextBoxFrame.Should().BeNull();
    }

    [Theory]
    [InlineData("F0B7")]
    [InlineData("B7")]
    public void SymbolElement_ReadsAsItsPrivateUseCharacterInTheSymbolFont(string code)
    {
        var doc = ReadWithFooter(
            "<w:p><w:r><w:t xml:space=\"preserve\">a </w:t><w:sym w:font=\"Symbol\" w:char=\"" + code + "\"/><w:t xml:space=\"preserve\"> b</w:t></w:r></w:p>");

        var runs = doc.FinalSectionHeadersFooters.Footer!.Paragraphs.Single().Runs;
        runs.Select(run => run.Text).Should().Equal("a ", "", " b");
        runs[1].Formatting.SymbolFont.Should().Be("Symbol");
        runs[1].Formatting.FontFamily.Should().BeNull();
        runs[0].Formatting.SymbolFont.Should().BeNull();
        runs[2].Formatting.SymbolFont.Should().BeNull();
        ThemeFontTokenResolver.ApplyForRendering(runs[1].Formatting, doc).FontFamily.Should().Be("Symbol");
    }

    [Fact]
    public void SymbolElement_IsWrittenBackAsSym()
    {
        var doc = ReadWithFooter(
            "<w:p><w:r><w:t>x</w:t></w:r></w:p>",
            "<w:p><w:r><w:sym w:font=\"Wingdings\" w:char=\"F0FC\"/></w:r></w:p>");
        using var saved = new MemoryStream();
        DocxWriter.Write(doc, saved);

        var body = DocumentXml(saved);
        var symbol = body.Descendants(W + "sym").Single();
        symbol.Attribute(W + "font")!.Value.Should().Be("Wingdings");
        symbol.Attribute(W + "char")!.Value.Should().Be("F0FC");
        body.Descendants(W + "t").Should().NotContain(text => text.Value.Contains(''));

        saved.Position = 0;
        var run = DocxReader.Read(saved).Blocks.OfType<Paragraph>().First().Runs.Single(candidate => candidate.Text.Length > 0);
        run.Formatting.SymbolFont.Should().Be("Wingdings");
        run.Text.Should().Be("");
        run.Formatting.FontFamily.Should().BeNull();
    }

    /// <summary>
    /// stress015: body runs carry only <c>w:asciiTheme="majorBidi"</c>. Run-level theme tokens were never
    /// resolved (only docDefaults/styles were), so those runs fell back to the Normal style's Calibri; and
    /// the theme's empty <c>a:cs</c> must give way to its script font for the theme bidi language
    /// (ar-SA -> Arab -> Times New Roman), as Word does.
    /// </summary>
    [Theory]
    [InlineData("majorBidi", "Times New Roman")]
    [InlineData("minorBidi", "Arial")]
    [InlineData("majorHAnsi", "Cambria")]
    public void RunThemeFontToken_StaysATokenAndResolvesAgainstTheDocumentTheme(string token, string expected)
    {
        var doc = ReadThemedDocument(
            $"<w:p><w:r><w:rPr><w:rFonts w:asciiTheme=\"{token}\" w:hAnsiTheme=\"{token}\" w:cstheme=\"{token}\"/></w:rPr><w:t>themed</w:t></w:r>" +
            "<w:r><w:t>plain</w:t></w:r></w:p>");

        var runs = doc.Blocks.OfType<Paragraph>().Single().Runs;
        runs[0].Formatting.FontFamily.Should().BeNull("the token is not baked into a literal font");
        runs[0].Formatting.ThemeFonts.Should().Be(new RunThemeFontTokens(token, token, null, token));
        ThemeFontTokenResolver.ApplyForRendering(runs[0].Formatting, doc).FontFamily.Should().Be(expected);
        runs[1].Formatting.ThemeFonts.Should().BeNull();
    }

    [Fact]
    public void RunThemeFontTokens_AreWrittenBackForEverySlot()
    {
        var doc = ReadThemedDocument(
            "<w:p><w:r><w:rPr><w:rFonts w:asciiTheme=\"majorBidi\" w:hAnsiTheme=\"majorBidi\" w:eastAsiaTheme=\"minorEastAsia\" w:cstheme=\"majorBidi\"/></w:rPr><w:t>themed</w:t></w:r></w:p>");
        using var saved = new MemoryStream();
        DocxWriter.Write(doc, saved);

        var rFonts = DocumentXml(saved).Descendants(W + "r")
            .Single(run => run.Element(W + "t")?.Value == "themed")
            .Element(W + "rPr")!.Element(W + "rFonts")!;

        rFonts.Attribute(W + "asciiTheme")!.Value.Should().Be("majorBidi");
        rFonts.Attribute(W + "hAnsiTheme")!.Value.Should().Be("majorBidi");
        rFonts.Attribute(W + "eastAsiaTheme")!.Value.Should().Be("minorEastAsia");
        rFonts.Attribute(W + "cstheme")!.Value.Should().Be("majorBidi");
        rFonts.Attribute(W + "ascii").Should().BeNull("no resolved literal font is baked into the run");
    }

    private static readonly System.Xml.Linq.XNamespace W = Wns;

    private static System.Xml.Linq.XElement DocumentXml(MemoryStream package)
    {
        package.Position = 0;
        using var zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        return System.Xml.Linq.XElement.Load(stream);
    }

    private static TextDocument ReadThemedDocument(string bodyXml)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var entry = zip.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }

            Add("word/document.xml",
                $"<w:document xmlns:w=\"{Wns}\"><w:body>{bodyXml}</w:body></w:document>");
            Add("word/settings.xml", $"<w:settings xmlns:w=\"{Wns}\"><w:themeFontLang w:val=\"fr-FR\" w:bidi=\"ar-SA\"/></w:settings>");
            Add("word/theme/theme1.xml",
                "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Office\"><a:themeElements>" +
                "<a:fontScheme name=\"Office\">" +
                "<a:majorFont><a:latin typeface=\"Cambria\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/><a:font script=\"Arab\" typeface=\"Times New Roman\"/><a:font script=\"Hebr\" typeface=\"David\"/></a:majorFont>" +
                "<a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/><a:font script=\"Arab\" typeface=\"Arial\"/></a:minorFont>" +
                "</a:fontScheme></a:themeElements></a:theme>");
        }

        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    [Fact]
    public void PlainTrackedInsertion_IsUnaffected()
    {
        var doc = ReadWithFooter(
            "<w:p><w:ins w:id=\"1\" w:author=\"A\"><w:r><w:t>new</w:t></w:r></w:ins></w:p>");

        var run = doc.FinalSectionHeadersFooters.Footer!.Paragraphs.Single().Runs.Single();
        run.Text.Should().Be("new");
        run.Revision.Should().Be(RevisionKind.Inserted);
        run.ComplexField.Should().BeNull();
    }
}
