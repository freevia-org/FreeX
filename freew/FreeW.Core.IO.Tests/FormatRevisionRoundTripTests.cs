using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip tests for tracked formatting changes (Word's w:rPrChange): a run whose formatting was
/// changed under Track Changes carries its previous formatting plus author/date, which must serialise as
/// w:rPr/w:rPrChange and read back onto <see cref="Run.FormatRevision"/>.
/// </summary>
public class FormatRevisionRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XDocument WriteDocumentXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static TextDocument BuildDocumentWithFormatRevision()
    {
        // The run is now bold (the new formatting); it was previously plain (the previous formatting),
        // and Alice made the change.
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("hello", new RunFormatting { Bold = true })
        {
            FormatRevision = new FormatRevision(RunFormatting.Default, "Alice", "2026-06-19T09:00:00Z")
        });
        doc.Blocks.Add(paragraph);
        return doc;
    }

    [Fact]
    public void FormatRevision_SerialisesAsRprChange_AsLastChildOfRpr()
    {
        var rPr = WriteDocumentXml(BuildDocumentWithFormatRevision())
            .Descendants(W + "rPr")
            // The run's own rPr (the one that directly hosts rPrChange), not the nested previous-rPr.
            .First(e => e.Element(W + "rPrChange") is not null);

        // rPrChange is the LAST child of the run properties (CT_RPr schema requirement).
        rPr.Elements().Last().Name.Should().Be(W + "rPrChange");

        var change = rPr.Element(W + "rPrChange")!;
        change.Attribute(W + "author")!.Value.Should().Be("Alice");
        change.Attribute(W + "date")!.Value.Should().Be("2026-06-19T09:00:00Z");
        change.Attribute(W + "id").Should().NotBeNull();
        // The nested w:rPr carries the previous (plain) formatting: no w:b element.
        change.Element(W + "rPr").Should().NotBeNull();
        change.Element(W + "rPr")!.Element(W + "b").Should().BeNull();
    }

    [Fact]
    public void FormatRevision_RoundTrips_PreservingPreviousFormattingAndAuthor()
    {
        var reloaded = RoundTrip(BuildDocumentWithFormatRevision());

        var run = reloaded.Paragraphs.First().Runs.First();
        // Current formatting is the new (bold) formatting.
        run.Formatting.Bold.Should().BeTrue();
        // The format revision survived with its previous (plain) formatting and metadata.
        run.FormatRevision.Should().NotBeNull();
        run.FormatRevision!.PreviousFormatting.Bold.Should().BeFalse();
        run.FormatRevision.Author.Should().Be("Alice");
        run.FormatRevision.DateXml.Should().Be("2026-06-19T09:00:00Z");
    }

    [Fact]
    public void FormatRevision_RoundTrips_WhenChangedRunHasNoOtherRunProperties()
    {
        // Run currently has default formatting but was previously bold — the run's rPr would be empty
        // except for the rPrChange, so the writer must still emit an rPr to host it.
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("plain now")
        {
            FormatRevision = new FormatRevision(new RunFormatting { Bold = true }, "Bob", null)
        });
        doc.Blocks.Add(paragraph);

        var reloaded = RoundTrip(doc);
        var run = reloaded.Paragraphs.First().Runs.First();
        run.Formatting.Bold.Should().BeFalse();
        run.FormatRevision.Should().NotBeNull();
        run.FormatRevision!.PreviousFormatting.Bold.Should().BeTrue();
        run.FormatRevision.Author.Should().Be("Bob");
    }

    [Fact]
    public void OrdinaryRun_HasNoFormatRevision_AndEmitsNoRprChange()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("no revisions"));

        WriteDocumentXml(doc).Descendants(W + "rPrChange").Should().BeEmpty();
        RoundTrip(doc).Paragraphs.First().Runs.First().FormatRevision.Should().BeNull();
    }
}
