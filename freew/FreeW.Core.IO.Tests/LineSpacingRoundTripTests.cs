using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for paragraph line spacing (pPr/w:spacing/@w:line + @w:lineRule), which FreeW
/// previously neither read nor wrote — every paragraph rendered at the 1.15 default regardless of the
/// document.
/// </summary>
public class LineSpacingRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static TextDocument DocWith(ParagraphFormatting formatting)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph { Formatting = formatting };
        p.Runs.Add(new Run("text"));
        doc.Blocks.Add(p);
        return doc;
    }

    private static XDocument DocumentXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    [Fact]
    public void DoubleSpacing_Multiple_RoundTrips()
    {
        var result = RoundTrip(DocWith(ParagraphFormatting.Default with { LineSpacing = 2.0 }));
        var f = result.Blocks.OfType<Paragraph>().First().Formatting;
        Assert.Equal(LineSpacingRule.Multiple, f.LineRule);
        Assert.Equal(2.0, f.LineSpacing, 3);
    }

    [Fact]
    public void ExactSpacing_RoundTrips_AndEmitsExactRule()
    {
        var doc = DocWith(ParagraphFormatting.Default with { LineRule = LineSpacingRule.Exact, LineHeightPt = 18 });

        var spacing = DocumentXml(doc).Descendants(W + "spacing").First();
        Assert.Equal("exact", spacing.Attribute(W + "lineRule")?.Value);
        Assert.Equal("360", spacing.Attribute(W + "line")?.Value); // 18 pt * 20 = 360 twentieths

        var f = RoundTrip(doc).Blocks.OfType<Paragraph>().First().Formatting;
        Assert.Equal(LineSpacingRule.Exact, f.LineRule);
        Assert.Equal(18, f.LineHeightPt, 3);
    }

    [Fact]
    public void DefaultSpacing_EmitsNoLineAttribute()
    {
        // The default (1.15, Multiple) must stay byte-stable: no w:line attribute is written.
        var xml = DocumentXml(DocWith(ParagraphFormatting.Default));
        Assert.DoesNotContain(xml.Descendants(W + "spacing"), s => s.Attribute(W + "line") is not null);
    }
}
