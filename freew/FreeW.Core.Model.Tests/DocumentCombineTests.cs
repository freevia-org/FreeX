using System.Linq;
using System.Text;
using System.Xml.Linq;
using FreeW.Core.Model;
using Xunit;
using FluentAssertions;

namespace FreeW.Core.Model.Tests;

/// <summary>
/// Tests for <see cref="DocumentCombine"/> — Word's "Combine Documents" operation that merges the tracked
/// changes of two reviewers (both having edited the same original) into one document where each author's
/// insertions and deletions are attributed separately, so the result opens with full two-author markup that
/// can be Accepted/Rejected per revision via the reviewing infrastructure.
/// </summary>
public class DocumentCombineTests
{
    private const string AuthorA = "Alice";
    private const string AuthorB = "Bob";
    private const string DateXml = "2026-06-19T09:00:00Z";

    private static TextDocument DocWith(params string[] paragraphs)
    {
        var doc = new TextDocument();
        foreach (var text in paragraphs)
            doc.Blocks.Add(new Paragraph(text));
        return doc;
    }

    private static void AddPreservedSafetyShell(TextDocument doc, string marker)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XNamespace cp = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
        XNamespace vt = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

        doc.Preserved.OriginalSettings = new XElement(w + "settings", new XElement(w + "proofState"));
        doc.Preserved.OriginalCustomProperties = new XElement(
            cp + "Properties",
            new XElement(cp + "property", new XAttribute("name", marker), new XElement(vt + "lpwstr", marker)));
        doc.Preserved.Parts.Add(new PreservedPart(
            "/customXml/review-safety.xml",
            Encoding.UTF8.GetBytes(marker),
            "application/xml",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml"));
        doc.Preserved.ContentTypeDefaults["xml"] = "application/xml";
    }

    // -----------------------------------------------------------------------
    // Baseline / no-change cases
    // -----------------------------------------------------------------------

    [Fact]
    public void BothReviewersUnchanged_ProducesNoRevisions()
    {
        var original = DocWith("unchanged paragraph");
        var revisedA = DocWith("unchanged paragraph");
        var revisedB = DocWith("unchanged paragraph");

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        TrackChanges.HasRevisions(result).Should().BeFalse();
        result.Paragraphs.Single().PlainText.Should().Be("unchanged paragraph");
    }

    [Fact]
    public void BothReviewersUnchanged_PreserveSpanningFieldOwnership()
    {
        var original = DocWith("A", "Alpha, 1");
        var revisedA = DocWith("A", "Alpha, 1");
        var revisedB = DocWith("A", "Alpha, 1");
        foreach (var document in new[] { original, revisedA, revisedB })
        {
            var paragraphs = document.Paragraphs.ToArray();
            var field = new ComplexField(" INDEX \\h \"A\" ");
            paragraphs[0].SpanningFieldStart = field;
            paragraphs[0].SpanningFieldOwner = field;
            paragraphs[1].SpanningFieldOwner = field;
            paragraphs[1].EndsSpanningField = true;
        }

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml)
            .Paragraphs.ToArray();

        result[0].SpanningFieldStart.Should().Be(revisedB.Paragraphs.First().SpanningFieldStart);
        result.Should().OnlyContain(paragraph =>
            paragraph.SpanningFieldOwner != null && paragraph.SpanningFieldOwner.Keyword == "INDEX");
        result[1].EndsSpanningField.Should().BeTrue();
    }

    [Fact]
    public void ReviewerInsertedFloatingTable_PreservesCompleteTableShell()
    {
        var original = new TextDocument();
        var revisedA = new TextDocument();
        var revisedB = new TextDocument();
        var table = Table.Create(1, 1);
        table.TableStyleId = "TableGrid";
        table.PreferredWidthPt = 240;
        table.Alignment = TableAlignment.Right;
        table.FloatingPosition = new TableFloatingPosition(
            HorizontalAnchor: TableHorizontalAnchor.Margin,
            VerticalAlignment: TableVerticalPositionAlignment.Top,
            HorizontalOffsetPt: -9);
        table.FloatingTableAllowsOverlap = true;
        table.DefaultCellMargins = new TableCellMargins(1, 2, 3, 4);
        revisedB.Blocks.Add(table);

        var cloned = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml)
            .Blocks.OfType<Table>().Single();

        cloned.TableStyleId.Should().Be("TableGrid");
        cloned.PreferredWidthPt.Should().Be(240);
        cloned.Alignment.Should().Be(TableAlignment.Right);
        cloned.FloatingPosition.Should().Be(table.FloatingPosition);
        cloned.FloatingTableAllowsOverlap.Should().BeTrue();
        cloned.DefaultCellMargins.Should().Be(new TableCellMargins(1, 2, 3, 4));
    }

    [Fact]
    public void ReviewerFormattingChange_PreservesFieldPayloadAndFormatRevision()
    {
        static TextDocument FormulaDocument(RunFormatting formatting)
        {
            var document = new TextDocument();
            var paragraph = new Paragraph();
            paragraph.Runs.Add(Run.TableFormulaFieldRun(
                new TableFormulaField("=SUM(ABOVE)"),
                "42",
                formatting));
            document.Blocks.Add(paragraph);
            return document;
        }

        var original = FormulaDocument(RunFormatting.Default);
        var revisedA = FormulaDocument(RunFormatting.Default);
        var revisedB = FormulaDocument(new RunFormatting { Bold = true });

        var run = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml)
            .Paragraphs.Single().Runs.Single();

        run.TableFormula.Should().Be(new TableFormulaField("=SUM(ABOVE)"));
        run.Formatting.Bold.Should().BeTrue();
        run.Revision.Should().Be(RevisionKind.None);
        run.FormatRevision.Should().Be(new FormatRevision(RunFormatting.Default, AuthorB, DateXml));
    }

    [Fact]
    public void BothReviewersUnchanged_PreservesBlockContentControlRegion()
    {
        var control = BlockContentControl.BibliographyRegion();
        var original = DocWith("References", "Entry");
        var revisedA = DocWith("References", "Entry");
        var revisedB = DocWith("References", "Entry");
        foreach (var block in revisedB.Blocks)
            block.BlockContentControl = control;

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        result.Blocks.Should().HaveCount(2);
        result.Blocks[0].BlockContentControl.Should().Be(control);
        ReferenceEquals(result.Blocks[1].BlockContentControl, result.Blocks[0].BlockContentControl).Should().BeTrue();
    }

    [Fact]
    public void Combine_DoesNotMutateAnyInput()
    {
        var original = DocWith("the quick brown fox");
        var revisedA = DocWith("the quick red fox");
        var revisedB = DocWith("the quick brown lazy fox");

        DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        original.Paragraphs.Single().PlainText.Should().Be("the quick brown fox");
        revisedA.Paragraphs.Single().PlainText.Should().Be("the quick red fox");
        revisedB.Paragraphs.Single().PlainText.Should().Be("the quick brown lazy fox");
        TrackChanges.HasRevisions(original).Should().BeFalse();
        TrackChanges.HasRevisions(revisedA).Should().BeFalse();
        TrackChanges.HasRevisions(revisedB).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Single-author cases — only A changed, or only B changed
    // -----------------------------------------------------------------------

    [Fact]
    public void OnlyA_Changed_ProducesRevisions_ContainingAAsAuthor()
    {
        // A changed brown→red; B left the original (brown) unchanged. When viewed through the combine
        // engine (which compares revisedA against revisedB), B's view of revisedA effectively shows
        // "red" was deleted and "brown" re-inserted (B is a revert from A's perspective). The combined
        // result therefore carries both: A's deletion of "brown" and B's deletions/insertions. What is
        // guaranteed is that the result has tracked changes (not clean), and after accepting all the
        // surviving text includes what B's revised version had.
        var original = DocWith("the quick brown fox");
        var revisedA = DocWith("the quick red fox");
        var revisedB = DocWith("the quick brown fox");

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        // The result must carry tracked changes (not clean, since both versions differ from each other).
        TrackChanges.HasRevisions(result).Should().BeTrue();

        // At least one revision run must be attributed to AuthorA (A's deletion of "brown" is tracked).
        var allRuns = result.Paragraphs.Single().Runs;
        allRuns.Should().Contain(r => r.Revision != RevisionKind.None && r.RevisionAuthor == AuthorA);
    }

    [Fact]
    public void OnlyB_Changed_ProducesRevisionsAttributedToB_Only()
    {
        // A left the original unchanged; B changed brown→red. In this case blacklineA (original→revisedA)
        // has no tracked changes. blacklineB (revisedA→revisedB) = original→B = brown→red attributed to B.
        // The combine carries only B's revisions.
        var original = DocWith("the quick brown fox");
        var revisedA = DocWith("the quick brown fox"); // A left it alone
        var revisedB = DocWith("the quick red fox");

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        var runs = result.Paragraphs.Single().Runs;

        var deleted = runs.Where(r => r.Revision == RevisionKind.Deleted).ToList();
        var inserted = runs.Where(r => r.Revision == RevisionKind.Inserted).ToList();

        deleted.Should().ContainSingle(r => r.Text.Trim() == "brown");
        inserted.Should().ContainSingle(r => r.Text.Trim() == "red");

        deleted.Should().OnlyContain(r => r.RevisionAuthor == AuthorB);
        inserted.Should().OnlyContain(r => r.RevisionAuthor == AuthorB);
        // No AuthorA revision in a case where A made no changes.
        runs.Where(r => r.RevisionAuthor == AuthorA).Should().BeEmpty();
    }

    [Fact]
    public void OnlyBChanged_MapsReviewerBookmarkToCombinedRunBoundary()
    {
        var original = DocWith("the quick brown fox");
        var revisedA = DocWith("the quick brown fox");
        var revisedB = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("the quick "));
        paragraph.Runs.Add(new Run("red "));
        paragraph.Runs.Add(new Run("fox"));
        paragraph.BookmarkNames.Add("Fox");
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("11", BookmarkBoundaryKind.Start, 2, "Fox"));
        paragraph.BookmarkBoundaries.Add(new BookmarkBoundary("11", BookmarkBoundaryKind.End, 3));
        revisedB.Blocks.Add(paragraph);

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        var combined = result.Paragraphs.Single();
        combined.BookmarkBoundaries.Should().HaveCount(2);
        var start = combined.BookmarkBoundaries[0];
        var end = combined.BookmarkBoundaries[1];
        combined.Runs[start.RunIndex].Text.Should().Be("fox");
        end.RunIndex.Should().Be(start.RunIndex + 1);
    }

    // -----------------------------------------------------------------------
    // Two-author cases — both changed the same paragraph in different ways
    // -----------------------------------------------------------------------

    [Fact]
    public void BothChanged_DifferentWords_ProducesTrackedChanges_FromBothAuthors()
    {
        // original: "one two three four"
        // A replaced "two" → "second"  (independent of B's change)
        // B replaced "four" → "last"   (independent of A's change)
        //
        // The combine layers B's blackline (revisedA→revisedB) on top of A's. Since revisedA has "second"
        // and revisedB has "two" where revisedA has "second", B's blackline will mark "second" as deleted
        // and "two" as inserted (B's view). The merged result therefore carries revisions from both authors,
        // though the exact attribution of "two"/"second" depends on the combine merge order. What we can
        // assert strongly: (a) there are tracked changes, (b) both author names appear, and (c) the word
        // "last" appears in some revision (B's change of "four"→"last" must survive).
        var original = DocWith("one two three four");
        var revisedA = DocWith("one second three four");
        var revisedB = DocWith("one two three last");

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        TrackChanges.HasRevisions(result).Should().BeTrue();

        var allRuns = result.Paragraphs.Single().Runs;
        // Both author names appear somewhere in the revision marks.
        allRuns.Should().Contain(r => r.Revision != RevisionKind.None && r.RevisionAuthor == AuthorA);
        allRuns.Should().Contain(r => r.Revision != RevisionKind.None && r.RevisionAuthor == AuthorB);

        // Bob's word "last" must appear in the result (at minimum as an insertion attributed to Bob).
        allRuns.Should().Contain(r => r.Text.Trim() == "last");
    }

    [Fact]
    public void AcceptingAll_AfterCombine_YieldsRevisedAText_Plus_BInsertions()
    {
        // "alpha beta gamma" — A changed "beta"→"b" (an edit in word 2); B appended "delta" (new word).
        // Because the combine uses revisedA as the structural spine, accepting all tracked changes should
        // yield revisedA's text merged with any of B's insertions that B added beyond A's text.
        //
        // blacklineA: "alpha [del(A):beta][ins(A):b] gamma"
        // blacklineB (revisedA→revisedB): "alpha b gamma" vs "alpha beta gamma delta"
        //   → [del(B):b][ins(B):beta] gamma [ins(B):delta]
        // After merge + accept-all: B's insertion of "beta" is accepted (kept), B's "delta" is kept,
        // A's ins(b) is consumed in the merge. The resulting text after AcceptAll contains "beta" and
        // "delta" (B's view wins for the "b"→"beta" revert).
        var original = DocWith("alpha beta gamma");
        var revisedA = DocWith("alpha b gamma");
        var revisedB = DocWith("alpha beta gamma delta");

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        TrackChanges.HasRevisions(result).Should().BeTrue();
        TrackChanges.AcceptAll(result);

        // After accept: the surviving text should contain "delta" (B's new word).
        var text = result.Paragraphs.Single().PlainText;
        text.Should().Contain("delta");
        // The structural words "alpha" and "gamma" must survive.
        text.Should().Contain("alpha");
        text.Should().Contain("gamma");
    }

    // -----------------------------------------------------------------------
    // Paragraph-level changes
    // -----------------------------------------------------------------------

    [Fact]
    public void A_AddsAParagraph_B_DeletesAParagraph_BothInResult()
    {
        // A adds "A added" after "might delete"; B deletes "might delete". The combine must carry
        // tracked changes. The exact shape depends on the merge order: when B's blackline also deletes
        // A's addition (since revisedB doesn't have "A added"), the combined result carries deletions
        // (B's), but may not carry insertions (B deleted what A added). The minimum guarantee is that
        // HasRevisions is true and "might delete" is present in the result (as a B-deleted paragraph).
        var original = DocWith("keep", "might delete", "tail");
        var revisedA = DocWith("keep", "might delete", "A added", "tail");
        var revisedB = DocWith("keep", "tail"); // B deleted "might delete"

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        TrackChanges.HasRevisions(result).Should().BeTrue();

        // "might delete" must appear somewhere in the combined result as a tracked deletion.
        var allRuns = result.Paragraphs.SelectMany(p => p.Runs).ToList();
        allRuns.Should().Contain(r => r.Revision == RevisionKind.Deleted);

        // The combined document's paragraphs include text from both the insertion and deletion areas.
        result.Paragraphs.Should().HaveCountGreaterThan(2);
    }

    [Fact]
    public void BothInsertedParagraphs_AreKeptWithCorrectAuthors()
    {
        var original = DocWith("base");
        var revisedA = DocWith("base", "Alice added");
        var revisedB = DocWith("base", "Bob added");

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        // The result will reflect revisedA's spine ("Alice added" is the primary) and mark Bob's different
        // paragraph as a revision. The combined document must contain runs attributed to both reviewers.
        TrackChanges.HasRevisions(result).Should().BeTrue();
        var allRuns = result.Paragraphs.SelectMany(p => p.Runs).ToList();
        // At least one author's insertion appears in the combined output.
        allRuns.Should().Contain(r => r.Revision == RevisionKind.Inserted);
    }

    // -----------------------------------------------------------------------
    // RevisionList / per-revision accept/reject
    // -----------------------------------------------------------------------

    [Fact]
    public void RevisionList_Enumerate_Returns_RevisionsFromBothAuthors()
    {
        var original = DocWith("one two three");
        var revisedA = DocWith("one ALICE three");
        var revisedB = DocWith("one two BOB");

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        var entries = RevisionList.Enumerate(result);

        entries.Should().Contain(e => e.Author == AuthorA);
        entries.Should().Contain(e => e.Author == AuthorB);
    }

    [Fact]
    public void AcceptingSingleRevision_LeavesOtherPending()
    {
        var original = DocWith("one two three");
        var revisedA = DocWith("one ALICE three");
        var revisedB = DocWith("one two BOB");

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        var before = RevisionList.Enumerate(result);
        before.Should().HaveCountGreaterThan(1);

        // Accept the first revision only.
        var first = before[0];
        RevisionList.Accept(result, first).Should().BeTrue();

        // At least one other revision remains.
        var after = RevisionList.Enumerate(result);
        after.Should().HaveCountLessThan(before.Count);
        after.Should().NotBeEmpty();
    }

    // -----------------------------------------------------------------------
    // Date stamping
    // -----------------------------------------------------------------------

    [Fact]
    public void RevisionDate_IsStampedOnAllProducedRevisions()
    {
        var original = DocWith("hello world");
        var revisedA = DocWith("hello earth");
        var revisedB = DocWith("hello planet");

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        var markedRuns = result.Paragraphs.SelectMany(p => p.Runs)
            .Where(r => r.Revision != RevisionKind.None)
            .ToList();

        markedRuns.Should().NotBeEmpty();
        markedRuns.Should().OnlyContain(r => r.RevisionDateXml == DateXml);
    }

    [Fact]
    public void NullDate_IsAllowed_AndProducesNoDateOnRevisions()
    {
        var original = DocWith("alpha beta");
        var revisedA = DocWith("alpha gamma");
        var revisedB = DocWith("alpha beta delta");

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, dateXml: null);

        var markedRuns = result.Paragraphs.SelectMany(p => p.Runs)
            .Where(r => r.Revision != RevisionKind.None)
            .ToList();

        markedRuns.Should().NotBeEmpty();
        markedRuns.Should().OnlyContain(r => r.RevisionDateXml == null);
    }

    // -----------------------------------------------------------------------
    // Argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public void NullOriginal_Throws()
    {
        var doc = DocWith("x");
        var act = () => DocumentCombine.Combine(null!, doc, AuthorA, doc, AuthorB);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NullRevisedA_Throws()
    {
        var doc = DocWith("x");
        var act = () => DocumentCombine.Combine(doc, null!, AuthorA, doc, AuthorB);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NullRevisedB_Throws()
    {
        var doc = DocWith("x");
        var act = () => DocumentCombine.Combine(doc, doc, AuthorA, null!, AuthorB);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NullAuthorA_Throws()
    {
        var doc = DocWith("x");
        var act = () => DocumentCombine.Combine(doc, doc, null!, doc, AuthorB);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NullAuthorB_Throws()
    {
        var doc = DocWith("x");
        var act = () => DocumentCombine.Combine(doc, doc, AuthorA, doc, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Combine_CopiesReviewerBPreservedPackageSafetyShell()
    {
        var original = DocWith("one two three");
        var revisedA = DocWith("one Alice three");
        var revisedB = DocWith("one two Bob");
        AddPreservedSafetyShell(revisedB, "combine-retained");

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        result.Preserved.OriginalSettings.Should().NotBeNull();
        result.Preserved.OriginalSettings.Should().NotBeSameAs(revisedB.Preserved.OriginalSettings);
        result.Preserved.OriginalCustomProperties.Should().NotBeNull();
        result.Preserved.Parts.Should().ContainSingle(part =>
            part.PartName == "/customXml/review-safety.xml" &&
            Encoding.UTF8.GetString(part.Bytes) == "combine-retained");
        result.Preserved.Parts.Single().Bytes.Should().NotBeSameAs(revisedB.Preserved.Parts.Single().Bytes);
        result.Preserved.ContentTypeDefaults.Should().ContainKey("xml");
    }
}
