using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Regression tests for F24: Word's standard <c>w:highlight</c> element must be read into
/// <see cref="RunFormatting.HighlightColorHex"/>, and the writer must emit <c>w:highlight</c>
/// for named highlight colors so Word's highlight gallery recognises them.
/// </summary>
public class HighlightRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// Builds a minimal hand-crafted .docx with the supplied run-properties XML and reads it
    /// back, returning the recovered <see cref="RunFormatting"/>.
    /// </summary>
    private static RunFormatting ReadRunFormattingFromXml(string rPrXml)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            static void AddEntry(ZipArchive z, string path, string xml)
            {
                var e = z.CreateEntry(path);
                using var w = new StreamWriter(e.Open());
                w.Write(xml);
            }

            AddEntry(zip, "word/document.xml",
                $"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p>
                      <w:r>
                        <w:rPr>{rPrXml}</w:rPr>
                        <w:t>text</w:t>
                      </w:r>
                    </w:p>
                  </w:body>
                </w:document>
                """);
        }

        ms.Position = 0;
        var doc = DocxReader.Read(ms);
        return doc.Blocks.OfType<Paragraph>().First().Runs.First().Formatting;
    }

    [Fact]
    public void ReadRunFormatting_WHighlightYellow_MapsToYellowHex()
    {
        var formatting = ReadRunFormattingFromXml(
            """<w:highlight xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" w:val="yellow"/>""");

        formatting.HighlightColorHex.Should().Be("#FFFF00",
            "w:highlight val=yellow must map to #FFFF00");
    }

    [Theory]
    [InlineData("yellow",      "#FFFF00")]
    [InlineData("green",       "#00FF00")]
    [InlineData("cyan",        "#00FFFF")]
    [InlineData("magenta",     "#FF00FF")]
    [InlineData("blue",        "#0000FF")]
    [InlineData("red",         "#FF0000")]
    [InlineData("darkBlue",    "#000080")]
    [InlineData("darkCyan",    "#008080")]
    [InlineData("darkGreen",   "#008000")]
    [InlineData("darkMagenta", "#800080")]
    [InlineData("darkRed",     "#800000")]
    [InlineData("darkYellow",  "#808000")]
    [InlineData("darkGray",    "#808080")]
    [InlineData("lightGray",   "#C0C0C0")]
    [InlineData("black",       "#000000")]
    [InlineData("white",       "#FFFFFF")]
    public void ReadRunFormatting_WHighlight_AllNamedTokensMapToExpectedHex(string token, string expectedHex)
    {
        var rPrXml = $"""<w:highlight xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" w:val="{token}"/>""";
        var formatting = ReadRunFormattingFromXml(rPrXml);
        formatting.HighlightColorHex.Should().Be(expectedHex, $"token {token} must map to {expectedHex}");
    }

    [Fact]
    public void ReadRunFormatting_WHighlightNone_LeavesHighlightNull()
    {
        var formatting = ReadRunFormattingFromXml(
            """<w:highlight xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" w:val="none"/>""");
        formatting.HighlightColorHex.Should().BeNull("none token means no highlight");
    }

    [Fact]
    public void ReadRunFormatting_BothWHighlightAndWShd_WHighlightWins()
    {
        // w:highlight takes precedence over w:shd for the highlight field
        var formatting = ReadRunFormattingFromXml(
            """
            <w:highlight xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" w:val="cyan"/>
            <w:shd xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" w:val="clear" w:color="auto" w:fill="FF0000"/>
            """);
        formatting.HighlightColorHex.Should().Be("#00FFFF", "w:highlight must take precedence over w:shd");
    }

    [Fact]
    public void Writer_YellowHighlight_EmitsWHighlightElement()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("hi", new RunFormatting { HighlightColorHex = "#FFFF00" }));
        doc.Blocks.Add(para);

        using var ms = new MemoryStream();
        DocxWriter.Write(doc, ms);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var xdoc = XDocument.Load(entry);

        var highlight = xdoc.Descendants(W + "highlight").FirstOrDefault();
        highlight.Should().NotBeNull("w:highlight must be emitted for named highlight colors");
        highlight!.Attribute(W + "val")!.Value.Should().Be("yellow");
    }

    [Fact]
    public void Writer_YellowHighlight_AlsoEmitsWShdForBackwardCompat()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("hi", new RunFormatting { HighlightColorHex = "#FFFF00" }));
        doc.Blocks.Add(para);

        using var ms = new MemoryStream();
        DocxWriter.Write(doc, ms);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var xdoc = XDocument.Load(entry);

        // w:shd must still be emitted for FreeW's own round-trip path
        var shd = xdoc.Descendants(W + "shd").FirstOrDefault();
        shd.Should().NotBeNull("w:shd must still be emitted for backward compatibility");
    }

    [Fact]
    public void Writer_ArbitraryHexHighlight_DoesNotEmitWHighlight()
    {
        // Non-named hex colors must NOT emit w:highlight (no matching named token)
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("hi", new RunFormatting { HighlightColorHex = "#123456" }));
        doc.Blocks.Add(para);

        using var ms = new MemoryStream();
        DocxWriter.Write(doc, ms);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        var xdoc = XDocument.Load(entry);

        var highlight = xdoc.Descendants(W + "highlight").FirstOrDefault();
        highlight.Should().BeNull("arbitrary hex must not emit w:highlight");
    }

    [Fact]
    public void RoundTrip_YellowHighlight_PreservesHighlightColorHex()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("hi", new RunFormatting { HighlightColorHex = "#FFFF00" }));
        doc.Blocks.Add(para);

        using var ms = new MemoryStream();
        DocxWriter.Write(doc, ms);
        ms.Position = 0;
        var read = DocxReader.Read(ms);
        read.Blocks.OfType<Paragraph>().First().Runs.First().Formatting.HighlightColorHex
            .Should().Be("#FFFF00");
    }
}
