using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Reader coverage for the document default paragraph spacing (w:docDefaults) and automatic spacing
/// (w:beforeAutospacing/w:afterAutospacing). FreeW previously ignored both, rendering every paragraph at
/// 0 space-after regardless of the document — which drifts vs Word down the page.
/// </summary>
public class DocDefaultsSpacingReaderTests
{
    private const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var e = zip.CreateEntry(path);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(xml);
    }

    private static TextDocument Read(string bodyXml, string? docDefaultsSpacing = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "word/document.xml", $"<w:document xmlns:w=\"{Wns}\"><w:body>{bodyXml}</w:body></w:document>");
            var dd = docDefaultsSpacing is null
                ? ""
                : $"<w:docDefaults><w:pPrDefault><w:pPr>{docDefaultsSpacing}</w:pPr></w:pPrDefault></w:docDefaults>";
            Add(zip, "word/styles.xml", $"<w:styles xmlns:w=\"{Wns}\">{dd}</w:styles>");
        }
        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    private static ParagraphFormatting FirstFormatting(TextDocument doc) =>
        doc.Blocks.OfType<Paragraph>().First().Formatting;

    [Fact]
    public void DocDefaultSpacing_AppliesToParagraphWithoutOwnSpacing()
    {
        var doc = Read(
            "<w:p><w:r><w:t>body</w:t></w:r></w:p>",
            docDefaultsSpacing: "<w:spacing w:after=\"200\" w:line=\"276\" w:lineRule=\"auto\"/>");
        var f = FirstFormatting(doc);
        Assert.Equal(10, f.SpaceAfterPt, 1);            // after=200 dxa = 10 pt
        Assert.Equal(1.15, f.LineSpacing, 2);           // line=276 / 240
    }

    [Fact]
    public void ParagraphOwnSpacing_WinsOverDocDefault()
    {
        var doc = Read(
            "<w:p><w:pPr><w:spacing w:after=\"40\"/></w:pPr><w:r><w:t>body</w:t></w:r></w:p>",
            docDefaultsSpacing: "<w:spacing w:after=\"200\"/>");
        Assert.Equal(2, FirstFormatting(doc).SpaceAfterPt, 1); // own after=40 dxa = 2 pt
    }

    [Fact]
    public void NoDocDefaults_ParagraphWithoutSpacing_StaysZero()
    {
        // Documents without docDefaults keep the prior behaviour (no extra space-after).
        var doc = Read("<w:p><w:r><w:t>body</w:t></w:r></w:p>");
        Assert.Equal(0, FirstFormatting(doc).SpaceAfterPt, 1);
    }

    [Fact]
    public void Autospacing_OverridesLiteralValue()
    {
        // w:afterAutospacing means Word ignores the literal after value and uses automatic (~one line)
        // spacing; the reader applies the auto approximation rather than the tiny literal 100 dxa = 5 pt.
        var doc = Read("<w:p><w:pPr><w:spacing w:after=\"100\" w:afterAutospacing=\"1\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p>");
        Assert.True(FirstFormatting(doc).SpaceAfterPt >= 12, "auto spacing should be ~one line, not the 5pt literal");
    }
}
