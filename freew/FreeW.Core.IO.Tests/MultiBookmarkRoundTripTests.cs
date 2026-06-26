using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for multiple bookmarks on the same paragraph (AA2 fix). Previously the reader
/// discarded all but the first bookmark; the writer emitted only one pair. Now every named bookmark
/// (w:bookmarkStart/End pair) in a paragraph is preserved through a full read→write→read cycle.
/// </summary>
public class MultiBookmarkRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var ms = new MemoryStream();
        DocxWriter.Write(document, ms);
        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    private static XDocument WriteDocumentXml(TextDocument document)
    {
        using var ms = new MemoryStream();
        DocxWriter.Write(document, ms);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static TextDocument ReadDocx(string bodyXml)
    {
        const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = zip.CreateEntry("word/document.xml");
            using var sw = new System.IO.StreamWriter(e.Open(), new System.Text.UTF8Encoding(false));
            sw.Write($"<w:document xmlns:w=\"{Wns}\"><w:body>{bodyXml}</w:body></w:document>");
        }
        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    /// <summary>
    /// A paragraph with two w:bookmarkStart elements (Bm1 and Bm2) must have BOTH names present after read.
    /// </summary>
    [Fact]
    public void Read_TwoBookmarksOnOneParagraph_BothCaptured()
    {
        var doc = ReadDocx(
            "<w:p>" +
            "<w:bookmarkStart w:id=\"1\" w:name=\"Bm1\"/>" +
            "<w:bookmarkEnd w:id=\"1\"/>" +
            "<w:bookmarkStart w:id=\"2\" w:name=\"Bm2\"/>" +
            "<w:bookmarkEnd w:id=\"2\"/>" +
            "<w:r><w:t>text</w:t></w:r>" +
            "</w:p>");

        var paragraph = doc.Blocks.OfType<Paragraph>().First();
        paragraph.BookmarkNames.Should().Contain("Bm1", "first bookmark must be captured");
        paragraph.BookmarkNames.Should().Contain("Bm2", "second bookmark must not be dropped");
        paragraph.BookmarkNames.Should().HaveCount(2);
    }

    /// <summary>
    /// The BookmarkName property (first-slot accessor) should still return the first bookmark for backward
    /// compatibility.
    /// </summary>
    [Fact]
    public void Read_TwoBookmarks_BookmarkNameReturnsFirst()
    {
        var doc = ReadDocx(
            "<w:p>" +
            "<w:bookmarkStart w:id=\"1\" w:name=\"Bm1\"/>" +
            "<w:bookmarkEnd w:id=\"1\"/>" +
            "<w:bookmarkStart w:id=\"2\" w:name=\"Bm2\"/>" +
            "<w:bookmarkEnd w:id=\"2\"/>" +
            "<w:r><w:t>text</w:t></w:r>" +
            "</w:p>");

        var paragraph = doc.Blocks.OfType<Paragraph>().First();
        paragraph.BookmarkName.Should().Be("Bm1", "BookmarkName returns the first (backward-compat)");
    }

    /// <summary>
    /// A model-built paragraph with two bookmark names must emit BOTH w:bookmarkStart/w:bookmarkEnd pairs
    /// in the output document.xml, each with the correct (distinct) w:id values.
    /// </summary>
    [Fact]
    public void Write_TwoBookmarksOnParagraph_BothPairsEmitted()
    {
        var doc = new TextDocument();
        var para = new Paragraph("heading");
        para.BookmarkNames.Add("Bm1");
        para.BookmarkNames.Add("Bm2");
        doc.Blocks.Add(para);

        var xml = WriteDocumentXml(doc);

        var starts = xml.Descendants(W + "bookmarkStart").ToList();
        starts.Should().HaveCount(2, "both bookmarks must be emitted as bookmarkStart elements");

        var names = starts.Select(s => s.Attribute(W + "name")?.Value).ToHashSet();
        names.Should().Contain("Bm1");
        names.Should().Contain("Bm2");

        var ends = xml.Descendants(W + "bookmarkEnd").ToList();
        ends.Should().HaveCount(2, "both bookmarks must have bookmarkEnd elements");

        // Each start and its matching end must share the same w:id (no id cross-wiring).
        var startIds = starts.Select(s => s.Attribute(W + "id")!.Value).ToList();
        var endIds = ends.Select(e => e.Attribute(W + "id")!.Value).ToList();
        startIds.Should().BeEquivalentTo(endIds, "start and end ids must match");

        // The two bookmark ids must be distinct.
        startIds.Should().OnlyHaveUniqueItems("each bookmark must have a unique id");
    }

    /// <summary>
    /// Full round-trip: read a docx with two bookmarks, write it, read it back — both names survive.
    /// </summary>
    [Fact]
    public void RoundTrip_TwoBookmarks_BothSurvive()
    {
        var doc = ReadDocx(
            "<w:p>" +
            "<w:bookmarkStart w:id=\"5\" w:name=\"Bm1\"/>" +
            "<w:bookmarkEnd w:id=\"5\"/>" +
            "<w:bookmarkStart w:id=\"6\" w:name=\"Bm2\"/>" +
            "<w:bookmarkEnd w:id=\"6\"/>" +
            "<w:r><w:t>heading text</w:t></w:r>" +
            "</w:p>");

        var result = RoundTrip(doc);

        var paragraph = result.Blocks.OfType<Paragraph>().First();
        paragraph.BookmarkNames.Should().Contain("Bm1");
        paragraph.BookmarkNames.Should().Contain("Bm2");
    }

    /// <summary>
    /// The _GoBack internal bookmark must continue to be skipped on read (not stored in BookmarkNames).
    /// </summary>
    [Fact]
    public void Read_GoBackBookmark_IsSkipped()
    {
        var doc = ReadDocx(
            "<w:p>" +
            "<w:bookmarkStart w:id=\"1\" w:name=\"_GoBack\"/>" +
            "<w:bookmarkEnd w:id=\"1\"/>" +
            "<w:bookmarkStart w:id=\"2\" w:name=\"RealTarget\"/>" +
            "<w:bookmarkEnd w:id=\"2\"/>" +
            "<w:r><w:t>text</w:t></w:r>" +
            "</w:p>");

        var paragraph = doc.Blocks.OfType<Paragraph>().First();
        paragraph.BookmarkNames.Should().NotContain("_GoBack", "_GoBack is a Word-internal marker that must be filtered");
        paragraph.BookmarkNames.Should().Contain("RealTarget");
        paragraph.BookmarkNames.Should().HaveCount(1);
    }

    /// <summary>
    /// Paragraph with a single bookmark still works exactly as before (backward compat).
    /// </summary>
    [Fact]
    public void SingleBookmark_BackwardCompatible()
    {
        var doc = new TextDocument();
        var para = new Paragraph("text") { BookmarkName = "OnlyOne" };
        doc.Blocks.Add(para);

        var result = RoundTrip(doc);

        var paragraph = result.Blocks.OfType<Paragraph>().First();
        paragraph.BookmarkName.Should().Be("OnlyOne");
        paragraph.BookmarkNames.Should().HaveCount(1);
    }
}
