using System.IO;
using System.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip + recomputation coverage for the reference/numbering complex fields driven by F9 /
/// Update-Field: a <c>REF</c> cross-reference to a bookmark and a <c>SEQ</c> sequence counter survive a
/// save+reload as <c>w:fldChar</c>/<c>w:instrText</c> fields, and re-running <see cref="ComplexFieldEngine"/>
/// after editing the document recomputes their results against the reloaded model.
/// </summary>
public class ComplexFieldUpdateRoundTripTests
{
    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    [Fact]
    public void RefAndSeqFields_SurviveRoundTrip_ThenRecomputeAfterTargetChanges()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        // 0: a bookmarked target the REF points at.
        doc.Blocks.Add(new Paragraph("Section Alpha") { BookmarkName = "sec" });
        // 1: a REF to that bookmark (cached "Section Alpha").
        var refPara = new Paragraph();
        refPara.Runs.Add(Run.ComplexFieldRun(" REF sec ", "Section Alpha"));
        doc.Blocks.Add(refPara);
        // 2 & 3: two SEQ Figure counters (cached "1"/"2").
        var seq1 = new Paragraph();
        seq1.Runs.Add(Run.ComplexFieldRun(" SEQ Figure ", "1"));
        doc.Blocks.Add(seq1);
        var seq2 = new Paragraph();
        seq2.Runs.Add(Run.ComplexFieldRun(" SEQ Figure ", "2"));
        doc.Blocks.Add(seq2);

        var reloaded = RoundTrip(doc);

        // The fields survived as complex fields with their instructions intact.
        var paras = reloaded.Blocks.OfType<Paragraph>().ToList();
        paras[1].Runs.Single().ComplexField!.Instruction.Should().Be(" REF sec ");
        paras[2].Runs.Single().ComplexField!.Instruction.Should().Be(" SEQ Figure ");
        paras[3].Runs.Single().ComplexField!.Instruction.Should().Be(" SEQ Figure ");

        // Baseline recompute on the reloaded doc matches the document state.
        ComplexFieldEngine.Recompute(reloaded, 1, 0).Should().Be("Section Alpha");
        ComplexFieldEngine.Recompute(reloaded, 2, 0).Should().Be("1");
        ComplexFieldEngine.Recompute(reloaded, 3, 0).Should().Be("2");

        // Now edit the document: rename the REF target, and insert a new figure before the existing ones.
        var target = (Paragraph)reloaded.Blocks[0];
        target.Runs.Clear();
        target.Runs.Add(new Run("Section Beta"));

        var inserted = new Paragraph();
        inserted.Runs.Add(Run.ComplexFieldRun(" SEQ Figure ", "?"));
        reloaded.Blocks.Insert(2, inserted); // pushes the original SEQ fields to indices 3 and 4

        // F9 recomputation reflects the edits: REF follows the new text; SEQ fields renumber 1,2,3.
        ComplexFieldEngine.Recompute(reloaded, 1, 0).Should().Be("Section Beta");
        ComplexFieldEngine.Recompute(reloaded, 2, 0).Should().Be("1");
        ComplexFieldEngine.Recompute(reloaded, 3, 0).Should().Be("2");
        ComplexFieldEngine.Recompute(reloaded, 4, 0).Should().Be("3");
    }

    [Fact]
    public void IfField_SurvivesRoundTrip_ThenRecomputesFromBookmarkText()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("125") { BookmarkName = "order" });
        var field = new Paragraph();
        field.Runs.Add(Run.ComplexFieldRun(
            " IF order >= 100 \"Thanks\" \"The minimum order is 100 units\" ",
            "stale"));
        doc.Blocks.Add(field);

        var reloaded = RoundTrip(doc);
        var run = ((Paragraph)reloaded.Blocks[1]).Runs.Single();
        run.ComplexField!.Instruction.Should().Be(
            " IF order >= 100 \"Thanks\" \"The minimum order is 100 units\" ");
        ComplexFieldEngine.Recompute(reloaded, 1, 0).Should().Be("Thanks");

        var target = (Paragraph)reloaded.Blocks[0];
        target.Runs.Clear();
        target.Runs.Add(new Run("80"));
        ComplexFieldEngine.Recompute(reloaded, 1, 0).Should().Be("The minimum order is 100 units");
    }
}
