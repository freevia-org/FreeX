using System.IO;
using System.Linq;
using FreeW.App.Presentation.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// End-to-end cover for the case the unit tests each only see half of: "Update Index" on a document that
/// came back off disk. DocumentReferenceEditingCoordinator.GeneratedRegionIndices scopes a refresh to one
/// region by the identity of the spanning field that owns it, and DocxReader has to reconstruct that
/// identity — one field object per spanning field, not one per paragraph — or every reloaded paragraph
/// looks like its own region and a refresh rebuilds only the first of them. Presentation-layer tests build
/// their documents in memory and never exercise the reader; the reader's own tests do not run a refresh.
/// </summary>
public sealed class ReloadedGeneratedRegionRefreshTests
{
    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static Paragraph[] IndexParagraphs(TextDocument document) =>
        document.Blocks
            .Where(block => DocumentIndex.IsIndexParagraph(block, null))
            .Cast<Paragraph>()
            .ToArray();

    [Fact]
    public void UpdateIndexOnAReloadedDocumentRebuildsTheWholeTargetedRegion()
    {
        var authored = new TextDocument();
        authored.Blocks.Add(new Paragraph
        {
            Runs =
            {
                DocumentIndex.MarkRun(new IndexMark("Alpha")),
                DocumentIndex.MarkRun(new IndexMark("Beta")),
            }
        });
        var region = DocumentIndex.Build(authored);
        region.Count.Should().BeGreaterThan(
            2,
            "a multi-paragraph region is the point: a per-paragraph field identity would refresh only " +
            "the first paragraph and leave the rest stale");
        foreach (var paragraph in region)
            authored.Blocks.Add(paragraph);

        var reloaded = RoundTrip(authored);
        var before = IndexParagraphs(reloaded);
        before.Should().HaveCount(region.Count);
        before
            .Select(paragraph => paragraph.SpanningFieldOwner)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Should().HaveCount(1, "the reloaded region is one region, so it has one owner");

        var session = new DocumentEditingSession();
        session.LoadDocument(reloaded);
        session.References
            .RefreshIndex(new DocumentTextPosition(0, 0), identifier: null, pageReferenceOf: null)
            .Applied.Should().BeTrue();

        IndexParagraphs(reloaded).Should().HaveCount(
            region.Count,
            "the whole region is replaced -- no stale paragraphs are left behind alongside the rebuild");
    }
}
