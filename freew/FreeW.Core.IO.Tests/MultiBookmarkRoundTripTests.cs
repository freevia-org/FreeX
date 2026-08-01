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

    [Fact]
    public void PartialParagraphBookmark_PreservesBoundaryPositionsAndMetadata()
    {
        var document = ReadDocx(
            "<w:p>" +
            "<w:r><w:t>A</w:t></w:r>" +
            "<w:bookmarkStart w:id=\"5\" w:name=\"Middle\" w:colFirst=\"1\" w:colLast=\"2\" w:displacedByCustomXml=\"next\"/>" +
            "<w:r><w:t>B</w:t></w:r>" +
            "<w:bookmarkEnd w:id=\"5\" w:displacedByCustomXml=\"prev\"/>" +
            "<w:r><w:t>C</w:t></w:r>" +
            "</w:p>");

        var paragraph = document.Paragraphs.Single();
        paragraph.BookmarkBoundaries.Should().Equal(
            new BookmarkBoundary("5", BookmarkBoundaryKind.Start, 1, "Middle", 1, 2, "next"),
            new BookmarkBoundary("5", BookmarkBoundaryKind.End, 2, DisplacedByCustomXml: "prev"));

        var output = WriteDocumentXml(document);
        var children = output.Descendants(W + "p").Single().Elements().ToList();
        children.Select(ElementToken).Should().Equal("run:A", "start:Middle", "run:B", "end", "run:C");
        var start = children[1];
        var end = children[3];
        start.Attribute(W + "id")?.Value.Should().Be(end.Attribute(W + "id")?.Value);
        start.Attribute(W + "colFirst")?.Value.Should().Be("1");
        start.Attribute(W + "colLast")?.Value.Should().Be("2");
        start.Attribute(W + "displacedByCustomXml")?.Value.Should().Be("next");
        end.Attribute(W + "displacedByCustomXml")?.Value.Should().Be("prev");

        var reopened = RoundTrip(document).Paragraphs.Single();
        reopened.BookmarkBoundaries.Select(boundary => boundary.RunIndex).Should().Equal(1, 2);
    }

    [Fact]
    public void CrossParagraphAndOverlappingBookmarks_PreservePairingAndOrder()
    {
        var document = ReadDocx(
            "<w:p>" +
            "<w:bookmarkStart w:id=\"7\" w:name=\"Outer\"/>" +
            "<w:r><w:t>A</w:t></w:r>" +
            "<w:bookmarkStart w:id=\"8\" w:name=\"Inner\"/>" +
            "<w:r><w:t>B</w:t></w:r>" +
            "<w:bookmarkEnd w:id=\"7\"/>" +
            "</w:p>" +
            "<w:p>" +
            "<w:r><w:t>C</w:t></w:r>" +
            "<w:bookmarkEnd w:id=\"8\"/>" +
            "<w:r><w:t>D</w:t></w:r>" +
            "</w:p>");

        var output = WriteDocumentXml(document);
        var paragraphs = output.Descendants(W + "p").ToList();
        var startsByName = paragraphs.SelectMany(item => item.Elements(W + "bookmarkStart"))
            .ToDictionary(item => item.Attribute(W + "name")!.Value);
        var firstEnd = paragraphs[0].Elements(W + "bookmarkEnd").Single();
        var secondEnd = paragraphs[1].Elements(W + "bookmarkEnd").Single();

        firstEnd.Attribute(W + "id")?.Value.Should().Be(startsByName["Outer"].Attribute(W + "id")?.Value);
        secondEnd.Attribute(W + "id")?.Value.Should().Be(startsByName["Inner"].Attribute(W + "id")?.Value);
        paragraphs[0].Elements().Select(ElementToken)
            .Should().Equal("start:Outer", "run:A", "start:Inner", "run:B", "end");
        paragraphs[1].Elements().Select(ElementToken)
            .Should().Equal("run:C", "end", "run:D");
    }

    [Fact]
    public void ZeroWidthAndGoBackBookmarks_RetainBoundariesWithoutPublishingInternalName()
    {
        var document = ReadDocx(
            "<w:p>" +
            "<w:r><w:t>A</w:t></w:r>" +
            "<w:bookmarkStart w:id=\"1\" w:name=\"_GoBack\"/>" +
            "<w:bookmarkEnd w:id=\"1\"/>" +
            "<w:bookmarkStart w:id=\"2\" w:name=\"Point\"/>" +
            "<w:bookmarkEnd w:id=\"2\"/>" +
            "<w:r><w:t>B</w:t></w:r>" +
            "</w:p>");

        var paragraph = document.Paragraphs.Single();
        paragraph.BookmarkNames.Should().Equal("Point");
        paragraph.BookmarkBoundaries.Should().HaveCount(4);
        paragraph.BookmarkBoundaries.Should().OnlyContain(boundary => boundary.RunIndex == 1);

        var output = WriteDocumentXml(document);
        output.Descendants(W + "bookmarkStart").Select(item => item.Attribute(W + "name")?.Value)
            .Should().Equal("_GoBack", "Point");
        output.Descendants(W + "bookmarkEnd").Should().HaveCount(2);
    }

    [Fact]
    public void RemovingImportedBookmarkName_DoesNotResurrectRetainedRange()
    {
        var document = ReadDocx(
            "<w:p><w:bookmarkStart w:id=\"3\" w:name=\"RemoveMe\"/>" +
            "<w:r><w:t>text</w:t></w:r><w:bookmarkEnd w:id=\"3\"/></w:p>");
        document.Paragraphs.Single().BookmarkNames.Clear();

        var output = WriteDocumentXml(document);
        output.Descendants(W + "bookmarkStart").Should().BeEmpty();
        output.Descendants(W + "bookmarkEnd").Should().BeEmpty();
    }

    [Fact]
    public void RenamingImportedBookmark_RetainsItsPartialRange()
    {
        var document = ReadDocx(
            "<w:p><w:r><w:t>A</w:t></w:r>" +
            "<w:bookmarkStart w:id=\"3\" w:name=\"Original\"/>" +
            "<w:r><w:t>B</w:t></w:r><w:bookmarkEnd w:id=\"3\"/>" +
            "<w:r><w:t>C</w:t></w:r></w:p>");

        document.Paragraphs.Single().BookmarkName = "Renamed";

        var output = WriteDocumentXml(document);
        output.Descendants(W + "p").Single().Elements().Select(ElementToken)
            .Should().Equal("run:A", "start:Renamed", "run:B", "end", "run:C");
        output.Descendants(W + "bookmarkStart").Should().ContainSingle()
            .Which.Attribute(W + "name")?.Value.Should().Be("Renamed");
    }

    [Fact]
    public void BoundaryInsideSharedHyperlinkSpan_SplitsWrapperAtExactPosition()
    {
        var document = ReadDocx(
            "<w:p>" +
            "<w:hyperlink w:anchor=\"Target\"><w:r><w:t>A</w:t></w:r>" +
            "<w:bookmarkStart w:id=\"4\" w:name=\"Inside\"/>" +
            "<w:r><w:t>B</w:t></w:r>" +
            "<w:bookmarkEnd w:id=\"4\"/></w:hyperlink>" +
            "</w:p>");

        var output = WriteDocumentXml(document);
        var paragraph = output.Descendants(W + "p").Single();
        paragraph.Elements(W + "hyperlink").Should().HaveCount(2);
        paragraph.Elements().Select(ElementToken)
            .Should().Equal("hyperlink", "start:Inside", "hyperlink", "end");
        paragraph.Elements(W + "hyperlink").Select(item => item.Value).Should().Equal("A", "B");
    }

    [Fact]
    public void BoundaryInsideContentControlSpan_RemainsInsideSingleWrapper()
    {
        var document = ReadDocx(
            "<w:p><w:sdt><w:sdtPr><w:tag w:val=\"Control\"/><w:text/></w:sdtPr><w:sdtContent>" +
            "<w:r><w:t>A</w:t></w:r>" +
            "<w:bookmarkStart w:id=\"9\" w:name=\"InsideControl\"/>" +
            "<w:r><w:t>B</w:t></w:r>" +
            "<w:bookmarkEnd w:id=\"9\"/>" +
            "</w:sdtContent></w:sdt></w:p>");

        var output = WriteDocumentXml(document);
        var paragraph = output.Descendants(W + "p").Single();
        var control = paragraph.Elements(W + "sdt").Should().ContainSingle().Which;
        paragraph.Elements().Select(ElementToken).Should().Equal("sdt");
        control.Element(W + "sdtContent")!.Elements().Select(ElementToken)
            .Should().Equal("run:A", "start:InsideControl", "run:B", "end");
    }

    [Fact]
    public void RemovedBookmarkInsideContentControl_DoesNotSplitWrapper()
    {
        var document = ReadDocx(
            "<w:p><w:sdt><w:sdtPr><w:tag w:val=\"Control\"/><w:text/></w:sdtPr><w:sdtContent>" +
            "<w:r><w:t>A</w:t></w:r><w:bookmarkStart w:id=\"9\" w:name=\"Remove\"/>" +
            "<w:r><w:t>B</w:t></w:r><w:bookmarkEnd w:id=\"9\"/>" +
            "</w:sdtContent></w:sdt></w:p>");
        document.Paragraphs.Single().BookmarkNames.Clear();

        var output = WriteDocumentXml(document);
        var paragraph = output.Descendants(W + "p").Single();
        paragraph.Elements(W + "sdt").Should().ContainSingle();
        paragraph.Descendants(W + "bookmarkStart").Should().BeEmpty();
        paragraph.Descendants(W + "bookmarkEnd").Should().BeEmpty();
        paragraph.Value.Should().Be("AB");
    }

    private static string ElementToken(XElement element)
    {
        if (element.Name == W + "r")
            return "run:" + element.Descendants(W + "t").SingleOrDefault()?.Value;
        if (element.Name == W + "bookmarkStart")
            return "start:" + element.Attribute(W + "name")?.Value;
        if (element.Name == W + "bookmarkEnd")
            return "end";
        return element.Name.LocalName;
    }
}
