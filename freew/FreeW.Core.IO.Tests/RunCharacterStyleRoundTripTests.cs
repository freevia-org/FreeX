using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for a run's character-style reference (w:rPr/w:rStyle) — the run-level analog of
/// <see cref="Paragraph.StyleId"/> (w:pPr/w:pStyle). Before this fix, DocxReader.ReadRunFormatting never
/// read w:rStyle, so any run styled purely through a character style (e.g. the Styles gallery's "Intense
/// Emphasis", or a Word-inserted hyperlink carrying the built-in "Hyperlink" character style) loaded with
/// no style reference and no visible formatting, and DocxWriter never re-emitted w:rStyle even if one had
/// been set — so a plain open-and-save round-trip silently dropped the character style.
/// </summary>
public class RunCharacterStyleRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XElement DocumentXmlRoot(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        using var zip = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry).Root!;
    }

    private static TextDocument DocWithIntenseEmphasisStyle()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Styles["IntenseEmphasis"] = new DocumentStyle
        {
            Id = "IntenseEmphasis",
            Name = "Intense Emphasis",
            Type = StyleType.Character,
            Run = new RunFormatting { Bold = true, Italic = true, ColorHex = "#4472C4" },
        };
        return doc;
    }

    /// <summary>
    /// The primary reported failure scenario: a run styled purely via a character style (like selecting
    /// "important" and applying "Intense Emphasis" from the Styles gallery) must keep its style reference
    /// across an open-and-save round-trip.
    /// </summary>
    [Fact]
    public void Run_CharacterStyleReference_RoundTrips()
    {
        var doc = DocWithIntenseEmphasisStyle();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("important") { StyleId = "IntenseEmphasis" });
        doc.Blocks.Add(paragraph);

        var result = RoundTrip(doc);

        var run = ((Paragraph)result.Blocks.Single()).Runs.Single();
        run.Text.Should().Be("important");
        run.StyleId.Should().Be("IntenseEmphasis");
    }

    /// <summary>
    /// Direct (on-top-of-the-style) run formatting must keep layering over the character style rather than
    /// being replaced by it or dropping the style reference — mirrors Word's own direct-formatting-over-
    /// linked-style behaviour.
    /// </summary>
    [Fact]
    public void Run_DirectFormatting_LayersOverCharacterStyle_AfterRoundTrip()
    {
        var doc = DocWithIntenseEmphasisStyle();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("important", new RunFormatting { Underline = true })
        {
            StyleId = "IntenseEmphasis",
        });
        doc.Blocks.Add(paragraph);

        var result = RoundTrip(doc);

        var run = ((Paragraph)result.Blocks.Single()).Runs.Single();
        run.StyleId.Should().Be("IntenseEmphasis");
        // The run's own direct formatting (not owned by the style) survives independently.
        run.Formatting.Underline.Should().BeTrue();
    }

    /// <summary>
    /// w:rStyle must be emitted as the first child of w:rPr (CT_RPr schema order) so Word's strict
    /// validator accepts the run.
    /// </summary>
    [Fact]
    public void Run_CharacterStyleReference_EmitsRStyle_AsFirstRPrChild()
    {
        var doc = DocWithIntenseEmphasisStyle();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("important", new RunFormatting { Bold = true }) { StyleId = "IntenseEmphasis" });
        doc.Blocks.Add(paragraph);

        var run = DocumentXmlRoot(doc).Descendants(W + "r").Single(r => r.Element(W + "t")?.Value == "important");
        var rPr = run.Element(W + "rPr")!;

        rPr.Elements().First().Name.Should().Be(W + "rStyle");
        rPr.Element(W + "rStyle")!.Attribute(W + "val")!.Value.Should().Be("IntenseEmphasis");
    }

    /// <summary>
    /// Sibling/neighbouring-behaviour guard: an ordinary run that carries no character style must keep
    /// round-tripping with no w:rStyle element and a null <see cref="Run.StyleId"/>, proving the fix does
    /// not spuriously attach a style reference to unrelated runs.
    /// </summary>
    [Fact]
    public void Run_WithoutCharacterStyle_RoundTrips_WithNoRStyle()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("plain text", new RunFormatting { Bold = true }));
        doc.Blocks.Add(paragraph);

        var result = RoundTrip(doc);
        var run = ((Paragraph)result.Blocks.Single()).Runs.Single();

        run.StyleId.Should().BeNull();
        run.Formatting.Bold.Should().BeTrue();

        var xmlRun = DocumentXmlRoot(doc).Descendants(W + "r").Single(r => r.Element(W + "t")?.Value == "plain text");
        xmlRun.Element(W + "rPr")?.Element(W + "rStyle").Should().BeNull();
    }
}
