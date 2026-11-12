using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// A package's default paragraph style is identified by <c>w:style/@w:default="1"</c>, not by the id
/// "Normal" — localized Word builds write e.g. <c>w:styleId="a"</c>. The reader must retain the flag
/// (the renderer resolves pStyle-less paragraphs through it) without baking the style id into
/// paragraphs, and the writer must re-emit it.
/// </summary>
public class DefaultParagraphStyleRoundTripTests
{
    private const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W = Wns;

    private const string StylesXml =
        $"<w:styles xmlns:w=\"{Wns}\">" +
        "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/><w:sz w:val=\"22\"/></w:rPr></w:rPrDefault>" +
        "<w:pPrDefault><w:pPr><w:spacing w:after=\"200\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"a\"><w:name w:val=\"Normal\"/>" +
        "<w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr>" +
        "<w:rPr><w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"28\"/></w:rPr></w:style>" +
        "<w:style w:type=\"character\" w:default=\"1\" w:styleId=\"a0\"><w:name w:val=\"Default Paragraph Font\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Plain\"><w:name w:val=\"Plain\"/></w:style>" +
        "</w:styles>";

    private const string DocumentXml =
        $"<w:document xmlns:w=\"{Wns}\"><w:body>" +
        "<w:p><w:r><w:t>unstyled</w:t></w:r></w:p>" +
        "<w:p><w:pPr><w:pStyle w:val=\"Plain\"/></w:pPr><w:r><w:t>styled</w:t></w:r></w:p>" +
        "</w:body></w:document>";

    private static MemoryStream BuildPackage()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddPart(zip, "word/document.xml", DocumentXml);
            AddPart(zip, "word/styles.xml", StylesXml);
        }
        ms.Position = 0;
        return ms;
    }

    private static void AddPart(ZipArchive zip, string path, string xml)
    {
        var e = zip.CreateEntry(path);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(xml);
    }

    private static byte[] Write(TextDocument document)
    {
        using var ms = new MemoryStream();
        DocxWriter.Write(document, ms);
        return ms.ToArray();
    }

    private static XDocument Part(byte[] package, string path)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var entry = zip.GetEntry(path)!.Open();
        return XDocument.Load(entry);
    }

    [Fact]
    public void Reader_FlagsTheDefaultParagraphStyle_WithoutBakingItIntoParagraphs()
    {
        using var package = BuildPackage();
        var doc = DocxReader.Read(package);

        doc.Styles["a"].IsDefault.Should().BeTrue();
        doc.Styles["a"].Type.Should().Be(StyleType.Paragraph);
        doc.Styles["a0"].IsDefault.Should().BeTrue();
        doc.Styles["Plain"].IsDefault.Should().BeFalse();
        // The default *paragraph* style is "a", never the default character style.
        DocumentStyle.FindDefaultParagraphStyleId(doc).Should().Be("a");

        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        paragraphs[0].StyleId.Should().BeNull("the default style is resolved at render time, not baked in");
        paragraphs[0].Runs.Single().Formatting.FontFamily.Should().BeNull();
        paragraphs[0].Runs.Single().Formatting.FontSizePt.Should().BeNull();
        paragraphs[1].StyleId.Should().Be("Plain");
    }

    [Fact]
    public void Writer_ReEmitsTheDefaultFlag_AndKeepsUnstyledParagraphsUnstyled()
    {
        using var package = BuildPackage();
        var written = Write(DocxReader.Read(package));

        var styles = Part(written, "word/styles.xml").Root!.Elements(W + "style").ToList();
        styles.Single(s => s.Attribute(W + "styleId")!.Value == "a")
            .Attribute(W + "default")!.Value.Should().Be("1");
        styles.Single(s => s.Attribute(W + "styleId")!.Value == "a0")
            .Attribute(W + "default")!.Value.Should().Be("1");
        styles.Single(s => s.Attribute(W + "styleId")!.Value == "Plain")
            .Attribute(W + "default").Should().BeNull();

        var body = Part(written, "word/document.xml").Root!.Element(W + "body")!;
        var first = body.Elements(W + "p").First();
        first.Element(W + "pPr")?.Element(W + "pStyle").Should().BeNull();
        first.Descendants(W + "rFonts").Should().BeEmpty("resolved style fonts must not leak into runs");
        first.Descendants(W + "sz").Should().BeEmpty("resolved style sizes must not leak into runs");
    }

    [Fact]
    public void ReadWriteCycle_IsStable()
    {
        using var package = BuildPackage();
        var first = Write(DocxReader.Read(package));
        var second = Write(DocxReader.Read(new MemoryStream(first)));

        foreach (var path in new[] { "word/styles.xml", "word/document.xml" })
        {
            Part(second, path).ToString(SaveOptions.DisableFormatting)
                .Should().Be(Part(first, path).ToString(SaveOptions.DisableFormatting));
        }
    }

    [Fact]
    public void AuthoredDocuments_DoNotGainADefaultFlag()
    {
        var written = Write(TextDocument.CreateEmpty());

        Part(written, "word/styles.xml").Root!.Elements(W + "style")
            .Where(s => s.Attribute(W + "default") is not null).Should().BeEmpty();
    }
}
