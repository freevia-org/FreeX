using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Concurrency regression coverage for <see cref="DocxWriter"/>'s per-write id counters (drawing/shape
/// <c>wp:docPr</c>, <c>w:bookmarkStart</c> and <c>w:ins</c>/<c>w:del</c> revision ids). These were once
/// shared mutable <c>static</c> fields reset/advanced inside every <c>Write</c>, so two concurrent writes
/// raced — one resetting or advancing a counter while another was mid-emit produced colliding or wrong ids.
///
/// The id state is now local to each write, so writing many documents in parallel must produce, for EACH
/// document independently, the exact id sequence a single-threaded write would: image+shape docPr ids
/// 1..N with no collision, bookmark ids 1..K, and revision ids 1..R. This test reliably failed against the
/// old static-counter code (ids bled across the parallel writes) and passes once the counters are per-write.
/// </summary>
public class ConcurrentWriteIdRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    /// <summary>A minimal valid 1×1 PNG so the inline-image (drawing) path is exercised alongside shapes.</summary>
    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary>
    /// Builds a document carrying, in deterministic order: an image run, a shape run, a WordArt run (all
    /// allocate <c>wp:docPr</c> ids), several bookmarked paragraphs, and several tracked-change runs
    /// (w:ins/w:del). <paramref name="seed"/> varies the counts so distinct documents have distinct id
    /// ranges — a cross-document leak therefore surfaces as a wrong/colliding id, not a coincidental match.
    /// </summary>
    private static TextDocument BuildDocument(int seed)
    {
        var doc = new TextDocument();

        // First paragraph mixes the three docPr-allocating drawings (image, shape, WordArt) plus a bookmark.
        var drawingParagraph = new Paragraph { BookmarkName = $"mark_{seed}_0" };
        drawingParagraph.Runs.Add(new Run("img") { Image = new InlineImage(OnePixelPng(), 10, 10) });
        drawingParagraph.Runs.Add(Run.FromShape(Shape.Preset(ShapeKind.Rectangle, 30, 20, "#112233")));
        drawingParagraph.Runs.Add(Run.FromWordArt(WordArt.Create($"Art{seed}", WordArtStyle.GradientFill, 36)));
        doc.Blocks.Add(drawingParagraph);

        // A varying number of extra image+shape paragraphs so the docPr id range differs per document.
        var extraDrawings = 1 + seed % 3;
        for (var i = 0; i < extraDrawings; i++)
        {
            var p = new Paragraph();
            p.Runs.Add(new Run("img") { Image = new InlineImage(OnePixelPng(), 8, 8) });
            p.Runs.Add(Run.FromShape(Shape.Preset(ShapeKind.Ellipse, 20, 20)));
            doc.Blocks.Add(p);
        }

        // A varying number of additional bookmarked paragraphs.
        var extraBookmarks = 1 + seed % 4;
        for (var i = 0; i < extraBookmarks; i++)
        {
            var p = new Paragraph { BookmarkName = $"mark_{seed}_{i + 1}" };
            p.Runs.Add(new Run($"bookmarked {i}"));
            doc.Blocks.Add(p);
        }

        // A varying number of tracked-change (revision) runs. Alternating kinds with distinct authors keep
        // each run in its own w:ins/w:del wrapper, so the revision-id count is deterministic.
        var revisionCount = 2 + seed % 5;
        var revisionParagraph = new Paragraph();
        for (var i = 0; i < revisionCount; i++)
        {
            var kind = i % 2 == 0 ? RevisionKind.Inserted : RevisionKind.Deleted;
            revisionParagraph.Runs.Add(new Run($"rev{i} ")
            {
                Revision = kind,
                RevisionAuthor = $"author-{seed}-{i}",
                RevisionDateXml = "2026-01-01T00:00:00Z",
            });
        }
        doc.Blocks.Add(revisionParagraph);

        return doc;
    }

    private sealed record ExpectedIds(int DrawingCount, int BookmarkCount, int RevisionCount);

    private static ExpectedIds Expected(int seed)
    {
        var drawingParagraph = 3;                 // image + shape + wordart
        var extraDrawings = (1 + seed % 3) * 2;   // image + shape each
        var drawingCount = drawingParagraph + extraDrawings;
        var bookmarkCount = 1 + (1 + seed % 4);   // first paragraph's bookmark + the extra bookmarked paragraphs
        var revisionCount = 2 + seed % 5;
        return new ExpectedIds(drawingCount, bookmarkCount, revisionCount);
    }

    /// <summary>Extracts the word/document.xml of a freshly written document as XML for id inspection.</summary>
    private static XDocument WriteDocumentXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    /// <summary>
    /// Asserts every id family in <paramref name="xml"/> matches the deterministic single-threaded sequence
    /// for <paramref name="seed"/>: drawing docPr ids are exactly 1..DrawingCount (unique, no collision),
    /// bookmark ids are exactly 1..BookmarkCount, and revision ids are exactly 1..RevisionCount.
    /// </summary>
    private static void AssertIds(int seed, XDocument xml)
    {
        var expected = Expected(seed);

        var drawingIds = xml.Descendants(Wp + "docPr")
            .Select(d => int.Parse(d.Attribute("id")!.Value))
            .OrderBy(x => x)
            .ToList();
        drawingIds.Should().OnlyHaveUniqueItems($"seed {seed}: drawing docPr ids must not collide");
        drawingIds.Should().Equal(Enumerable.Range(1, expected.DrawingCount),
            $"seed {seed}: drawing docPr ids must be exactly 1..{expected.DrawingCount}");

        var bookmarkIds = xml.Descendants(W + "bookmarkStart")
            .Select(b => int.Parse(b.Attribute(W + "id")!.Value))
            .OrderBy(x => x)
            .ToList();
        bookmarkIds.Should().Equal(Enumerable.Range(1, expected.BookmarkCount),
            $"seed {seed}: bookmark ids must be exactly 1..{expected.BookmarkCount}");

        var revisionIds = xml.Descendants(W + "ins").Concat(xml.Descendants(W + "del"))
            .Select(r => int.Parse(r.Attribute(W + "id")!.Value))
            .OrderBy(x => x)
            .ToList();
        revisionIds.Should().OnlyHaveUniqueItems($"seed {seed}: revision ids must not collide");
        revisionIds.Should().Equal(Enumerable.Range(1, expected.RevisionCount),
            $"seed {seed}: revision ids must be exactly 1..{expected.RevisionCount}");
    }

    [Fact]
    public void SingleThreaded_ProducesTheExpectedDeterministicIdSequence()
    {
        // Pins the expected single-threaded behaviour the concurrent test compares against (and proves the
        // seeds exercise distinct id ranges).
        for (var seed = 0; seed < 12; seed++)
            AssertIds(seed, WriteDocumentXml(BuildDocument(seed)));
    }

    [Fact]
    public void ManyConcurrentWrites_KeepEachDocumentsIdsCorrectAndNonColliding()
    {
        const int documentCount = 50;

        // Build all the models up front so the parallel region only exercises DocxWriter.Write.
        var documents = Enumerable.Range(0, documentCount).Select(BuildDocument).ToArray();

        var results = new ConcurrentDictionary<int, XDocument>();
        var failures = new ConcurrentBag<Exception>();

        // Re-read inside the parallel body too, so the whole write→read path runs under contention (this is
        // what flushed out the shared-static race in practice).
        Parallel.For(0, documentCount, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, seed =>
        {
            try
            {
                results[seed] = WriteDocumentXml(documents[seed]);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        });

        failures.Should().BeEmpty("concurrent writes must not throw");
        results.Should().HaveCount(documentCount);

        foreach (var (seed, xml) in results)
            AssertIds(seed, xml);
    }
}
