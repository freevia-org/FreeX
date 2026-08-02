using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public class OdtRoundTripTests
{
    private static TextDocument DocOf(params string[] paragraphs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static byte[] Save(TextDocument document)
    {
        using var ms = new MemoryStream();
        OdtFileAdapter.Odt().Save(document, ms);
        return ms.ToArray();
    }

    private static byte[] Save(TextDocument document, OdtFileAdapter adapter)
    {
        using var ms = new MemoryStream();
        adapter.Save(document, ms);
        return ms.ToArray();
    }

    private static TextDocument Load(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return OdtFileAdapter.Odt().Load(ms);
    }

    private static string[] Lines(TextDocument document) =>
        document.Blocks.OfType<Paragraph>().Select(p => p.PlainText).ToArray();

    [Fact]
    public void RoundTrip_PreservesParagraphText()
    {
        var reloaded = Load(Save(DocOf("First paragraph", "Second", "Third")));
        Lines(reloaded).Should().Contain("First paragraph");
        Lines(reloaded).Should().Contain("Second");
        Lines(reloaded).Should().Contain("Third");
    }

    [Fact]
    public void RoundTrip_PreservesHeading()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Title here") { StyleId = "Heading1" });

        var reloaded = Load(Save(document));
        reloaded.Blocks.OfType<Paragraph>().Should().Contain(p => p.StyleId == "Heading1" && p.PlainText == "Title here");
    }

    [Fact]
    public void RoundTrip_PreservesBoldItalic()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("strong", new RunFormatting { Bold = true, Italic = true }));
        document.Blocks.Add(p);

        var reloaded = Load(Save(document));
        var run = reloaded.Blocks.OfType<Paragraph>().SelectMany(x => x.Runs).Single(r => r.Text == "strong");
        run.Formatting.Bold.Should().BeTrue();
        run.Formatting.Italic.Should().BeTrue();
    }

    [Fact]
    public void RoundTrip_PreservesDoubleStrikethroughDistinctFromOrdinaryStrikethrough()
    {
        XNamespace style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("double", new RunFormatting { DoubleStrikethrough = true }));
        paragraph.Runs.Add(new Run("single", new RunFormatting { Strikethrough = true }));
        document.Blocks.Add(paragraph);

        var bytes = Save(document);
        using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        using (var stream = archive.GetEntry("content.xml")!.Open())
        {
            var content = XDocument.Load(stream);
            var doubleProperties = content.Descendants(style + "text-properties")
                .Single(properties => (string?)properties.Attribute(style + "text-line-through-type") == "double");
            doubleProperties.Attribute(style + "text-line-through-style")!.Value.Should().Be("solid");
        }

        var reloadedRuns = Load(bytes).Blocks.OfType<Paragraph>().Single().Runs;
        reloadedRuns[0].Formatting.DoubleStrikethrough.Should().BeTrue();
        reloadedRuns[0].Formatting.Strikethrough.Should().BeFalse();
        reloadedRuns[1].Formatting.Strikethrough.Should().BeTrue();
        reloadedRuns[1].Formatting.DoubleStrikethrough.Should().BeFalse();
    }

    [Fact]
    public void RoundTrip_PreservesTable()
    {
        var table = new Table();
        var row = new TableRow();
        var c1 = new TableCell();
        c1.Paragraphs.Add(new Paragraph("A1"));
        var c2 = new TableCell();
        c2.Paragraphs.Add(new Paragraph("B1"));
        row.Cells.Add(c1);
        row.Cells.Add(c2);
        table.Rows.Add(row);

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(table);

        var reloaded = Load(Save(document));
        var cells = reloaded.Blocks.OfType<Table>().Single().Rows.Single().Cells;
        cells.Should().HaveCount(2);
        cells[0].Paragraphs.Single().PlainText.Should().Be("A1");
        cells[1].Paragraphs.Single().PlainText.Should().Be("B1");
    }

    [Fact]
    public void Mimetype_IsFirstEntry_StoredUncompressed_WithExactContent()
    {
        using var za = new ZipArchive(new MemoryStream(Save(DocOf("x"))), ZipArchiveMode.Read);

        za.Entries[0].FullName.Should().Be("mimetype");
        // Stored (uncompressed): the compressed size equals the raw size.
        za.Entries[0].CompressedLength.Should().Be(za.Entries[0].Length);

        using var reader = new StreamReader(za.Entries[0].Open());
        reader.ReadToEnd().Should().Be(OdtFileAdapter.MimeType);
    }

    [Fact]
    public void Save_ProducesPackageWithExpectedParts()
    {
        using var za = new ZipArchive(new MemoryStream(Save(DocOf("x"))), ZipArchiveMode.Read);
        var names = za.Entries.Select(e => e.FullName).ToList();
        names.Should().Contain("content.xml");
        names.Should().Contain("styles.xml");
        names.Should().Contain("META-INF/manifest.xml");
    }

    [Fact]
    public void Ott_IsTemplateDescriptor()
    {
        var format = OdtFileAdapter.Ott().Formats.Single();
        format.Extension.Should().Be(".ott");
        format.OpensAsTemplate.Should().BeTrue();
        format.CanOpen.Should().BeTrue();
        format.CanSave.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, ".odt", false)]
    [InlineData(true, ".ott", true)]
    public void Save_ProducesOdfPackageAndReloadsTextForDocumentAndTemplate(
        bool template,
        string extension,
        bool opensAsTemplate)
    {
        var adapter = template ? OdtFileAdapter.Ott() : OdtFileAdapter.Odt();
        var bytes = Save(DocOf("ODF evidence", "second"), adapter);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        archive.Entries[0].FullName.Should().Be("mimetype");
        archive.Entries.Select(entry => entry.FullName).Should().Contain(new[]
        {
            "content.xml",
            "styles.xml",
            "META-INF/manifest.xml",
        });

        using var stream = new MemoryStream(bytes);
        Lines(adapter.Load(stream)).Should().Contain(new[] { "ODF evidence", "second" });
        adapter.Formats.Single().Should().Match<FileFormatDescriptor>(format =>
            format.Extension == extension &&
            format.OpensAsTemplate == opensAsTemplate &&
            format.CanOpen &&
            format.CanSave);
    }
}
