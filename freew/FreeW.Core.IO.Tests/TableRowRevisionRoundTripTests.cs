using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip tests for tracked row-level changes (Word's <c>tr/trPr/w:ins</c> and <c>tr/trPr/w:del</c>):
/// an entire table row inserted/deleted under Track Changes must serialise as the trailing child of
/// <c>w:trPr</c> (after cantSplit/trHeight/tblHeader) and survive a load -> save -> load cycle intact on
/// <see cref="TableRow.RowRevision"/>/<see cref="TableRow.RowRevisionAuthor"/>/<see cref="TableRow.RowRevisionDateXml"/>.
/// </summary>
public class TableRowRevisionRoundTripTests
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

    private static TextDocument BuildDocumentWithTrackedRow(RevisionKind kind)
    {
        var doc = new TextDocument();
        var table = new Table();

        var ordinaryRow = new TableRow();
        ordinaryRow.Cells.Add(new TableCell("kept"));
        table.Rows.Add(ordinaryRow);

        var trackedRow = new TableRow
        {
            RowRevision = kind,
            RowRevisionAuthor = "Alice",
            RowRevisionDateXml = "2026-07-30T09:00:00Z"
        };
        trackedRow.Cells.Add(new TableCell("tracked"));
        table.Rows.Add(trackedRow);

        doc.Blocks.Add(table);
        return doc;
    }

    [Theory]
    [InlineData(RevisionKind.Inserted, "ins")]
    [InlineData(RevisionKind.Deleted, "del")]
    public void TrackedRow_SerialisesAsTrailingChildOfTrPr(RevisionKind kind, string elementName)
    {
        var xml = WriteDocumentXml(BuildDocumentWithTrackedRow(kind));
        var rows = xml.Descendants(W + "tr").ToList();
        rows.Should().HaveCount(2);

        // The first row is ordinary: no trPr revision marker at all.
        rows[0].Element(W + "trPr")?.Element(W + "ins").Should().BeNull();
        rows[0].Element(W + "trPr")?.Element(W + "del").Should().BeNull();

        // The second (tracked) row carries the marker as the LAST child of trPr.
        var trPr = rows[1].Element(W + "trPr")!;
        trPr.Elements().Last().Name.Should().Be(W + elementName);
        var marker = trPr.Element(W + elementName)!;
        marker.Attribute(W + "author")!.Value.Should().Be("Alice");
        marker.Attribute(W + "date")!.Value.Should().Be("2026-07-30T09:00:00Z");
        marker.Attribute(W + "id").Should().NotBeNull();
    }

    [Theory]
    [InlineData(RevisionKind.Inserted)]
    [InlineData(RevisionKind.Deleted)]
    public void TrackedRow_RoundTrips_PreservingKindAuthorAndDate(RevisionKind kind)
    {
        var reloaded = RoundTrip(BuildDocumentWithTrackedRow(kind));
        var table = reloaded.Blocks.OfType<Table>().Single();

        table.Rows[0].RowRevision.Should().Be(RevisionKind.None);
        table.Rows[0].RowRevisionAuthor.Should().BeNull();

        table.Rows[1].RowRevision.Should().Be(kind);
        table.Rows[1].RowRevisionAuthor.Should().Be("Alice");
        table.Rows[1].RowRevisionDateXml.Should().Be("2026-07-30T09:00:00Z");
    }

    // Sibling no-regression: an ordinary table (no tracked rows) must not grow spurious w:ins/w:del, and
    // its existing row properties (cantSplit/trHeight) must keep serialising correctly alongside the new
    // trailing-child logic.
    [Fact]
    public void OrdinaryTable_HasNoRowRevisions_AndEmitsNoTrPrInsOrDel()
    {
        var doc = new TextDocument();
        var table = new Table();
        var row = new TableRow { AllowBreakAcrossPages = false, HeightPt = 30, HeightRule = TableRowHeightRule.Exact };
        row.Cells.Add(new TableCell("plain"));
        table.Rows.Add(row);
        doc.Blocks.Add(table);

        var xml = WriteDocumentXml(doc);
        xml.Descendants(W + "tr").Single().Element(W + "trPr")!.Element(W + "ins").Should().BeNull();
        xml.Descendants(W + "tr").Single().Element(W + "trPr")!.Element(W + "del").Should().BeNull();
        // cantSplit/trHeight still round-trip correctly.
        xml.Descendants(W + "tr").Single().Element(W + "trPr")!.Element(W + "cantSplit").Should().NotBeNull();
        xml.Descendants(W + "tr").Single().Element(W + "trPr")!.Element(W + "trHeight").Should().NotBeNull();

        var reloaded = RoundTrip(doc);
        var reloadedRow = reloaded.Blocks.OfType<Table>().Single().Rows.Single();
        reloadedRow.RowRevision.Should().Be(RevisionKind.None);
        reloadedRow.AllowBreakAcrossPages.Should().BeFalse();
        reloadedRow.HeightPt.Should().BeApproximately(30, 0.5);
    }
}
