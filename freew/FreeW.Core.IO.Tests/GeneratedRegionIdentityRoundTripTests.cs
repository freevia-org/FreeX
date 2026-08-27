using System.IO;
using System.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// A generated region's identity is the <see cref="Paragraph.SpanningFieldOwner"/> its builder stamps
/// on it: one freshly constructed <see cref="ComplexField"/> per build, so two separately inserted
/// regions hold two distinct instances even when their instruction text is byte-identical. That
/// identity is what lets DocumentReferenceEditingCoordinator.GeneratedRegionIndices refresh one region
/// without deleting its siblings, and these pin that it survives a docx save/load rather than existing
/// only for as long as the objects built it stay in memory.
/// </summary>
public class GeneratedRegionIdentityRoundTripTests
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
    public void TwoIndependentIndexRegionsKeepDistinctOwnersAcrossARoundTrip()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph
        {
            Runs = { DocumentIndex.MarkRun(new IndexMark("Alpha")), DocumentIndex.MarkRun(new IndexMark("Beta")) }
        });
        var first = DocumentIndex.Build(document);
        var second = DocumentIndex.Build(document);
        first.Should().NotBeEmpty();
        ReferenceEquals(first[0].SpanningFieldOwner, second[0].SpanningFieldOwner).Should().BeFalse(
            "each build stamps its own region with a freshly constructed field");
        first[0].SpanningFieldOwner!.Instruction.Should().Be(
            second[0].SpanningFieldOwner!.Instruction,
            "the two regions are otherwise identical -- instruction text cannot tell them apart, " +
            "which is exactly why reference identity is the boundary rule");

        foreach (var paragraph in first)
            document.Blocks.Add(paragraph);
        document.Blocks.Add(new Paragraph("Between"));
        foreach (var paragraph in second)
            document.Blocks.Add(paragraph);

        var reloaded = IndexParagraphs(RoundTrip(document));

        reloaded.Should().HaveCount(first.Count + second.Count);
        reloaded.Should().OnlyContain(paragraph => paragraph.SpanningFieldOwner != null);
        reloaded
            .Select(paragraph => paragraph.SpanningFieldOwner)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Should().HaveCount(
                2,
                "the two regions must come back as two distinct owners, not merged into one");
        reloaded
            .Take(first.Count)
            .Select(paragraph => paragraph.SpanningFieldOwner)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Should().HaveCount(1, "the first region's paragraphs must all share one owner");
        reloaded
            .Skip(first.Count)
            .Select(paragraph => paragraph.SpanningFieldOwner)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Should().HaveCount(1, "the second region's paragraphs must all share one owner");
    }
}
