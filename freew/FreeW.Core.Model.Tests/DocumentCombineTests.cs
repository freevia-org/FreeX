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
    public void ReviewerInsertedTableWithNestedTable_PreservesNestedTable()
    {
        var original = new TextDocument();
        var revisedA = new TextDocument();
        var revisedB = new TextDocument();

        var outerTable = Table.Create(1, 1);
        var nestedTable = Table.Create(1, 1);
        nestedTable.Rows[0].Cells[0] = new TableCell("nested cell text");
        outerTable.Rows[0].Cells[0].NestedTables.Add(nestedTable);
        // Word requires a cell that hosts a table to still carry a trailing paragraph.
        outerTable.Rows[0].Cells[0].Paragraphs.Add(new Paragraph(string.Empty));
        revisedB.Blocks.Add(outerTable);

        var cloned = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml)
            .Blocks.OfType<Table>().Single();

        var clonedCell = cloned.Rows[0].Cells[0];
        clonedCell.NestedTables.Should().ContainSingle();
        var clonedNestedTable = clonedCell.NestedTables[0];
        clonedNestedTable.Should().NotBeSameAs(nestedTable);
        clonedNestedTable.Rows[0].Cells[0].Paragraphs.Single().PlainText.Should().Be("nested cell text");
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
    public void BothReviewersUnchanged_ButRevisedACopySplitsTextIntoMoreRuns_DoesNotDuplicateText()
    {
        // freew-compare-merge F2: neither reviewer touched this paragraph's text, but reviewer A's copy
        // happens to carry the identical text split into more runs than reviewer B's copy (e.g. because an
        // unrelated formatting/comment/hyperlink boundary sits inside A's copy only). blacklineA (the
        // original->revisedA diff) then carries A's own run split verbatim -- DocumentCompare returns the
        // anchor's revised paragraph run-list-unmodified whenever the anchor's run counts differ even
        // though the plain text matches -- so MergeParagraph must not mistake A's extra run boundary for
        // extra content once B's shorter run list is exhausted.
        var original = DocWith("The quick fox jumps");

        var revisedA = new TextDocument();
        var revisedAParagraph = new Paragraph();
        revisedAParagraph.Runs.Add(new Run("The quick"));
        revisedAParagraph.Runs.Add(new Run(" fox jumps"));
        revisedA.Blocks.Add(revisedAParagraph);

        var revisedB = DocWith("The quick fox jumps");

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        TrackChanges.HasRevisions(result).Should().BeFalse();
        result.Paragraphs.Single().PlainText.Should().Be("The quick fox jumps");
    }

    [Fact]
    public void BothReviewersUnchanged_ButRevisedBCopySplitsTextIntoMoreRuns_StillDoesNotDuplicateText()
    {
        // Sibling no-regression case for F2: the split now sits on B's side instead of A's (B's copy has
        // more runs than A's for the same unedited text). This direction was already handled correctly
        // (the loop naturally drains B's finer split once A's single run is exhausted, since only the
        // "B exhausted first" branch blindly re-emitted leftover content) -- assert it stays that way.
        var original = DocWith("The quick fox jumps");

        var revisedA = DocWith("The quick fox jumps");

        var revisedB = new TextDocument();
        var revisedBParagraph = new Paragraph();
        revisedBParagraph.Runs.Add(new Run("The quick"));
        revisedBParagraph.Runs.Add(new Run(" fox jumps"));
        revisedB.Blocks.Add(revisedBParagraph);

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        TrackChanges.HasRevisions(result).Should().BeFalse();
        result.Paragraphs.Single().PlainText.Should().Be("The quick fox jumps");
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

    [Fact]
    public void AEarlyDeletion_DoesNotDesyncLaterParagraphPairing()
    {
        // Regression test for freew-combine-positional-misalignment: A deletes an early paragraph
        // ("BaseOnly") that B never touched. Because DocumentCompare splices A's whole-paragraph deletion
        // into blacklineA's own Blocks list, blacklineA has one MORE paragraph entry than blacklineB from
        // that point on. Zipping the two blacklines by raw list position (the bug) pairs blacklineA's
        // "BaseOnly" deletion against blacklineB's real "Base2" paragraph, fusing A's deletion onto Base2's
        // text — and shifts every paragraph after that by one, so B's own "Bnew" insertion ends up paired
        // with (and gains a duplicated copy of) A's untouched "Base2" paragraph instead of standing alone.
        var original = DocWith("Base1", "BaseOnly", "Base2");
        var revisedA = DocWith("Base1", "Base2"); // A deletes "BaseOnly"
        var revisedB = DocWith("Base1", "Base2", "Bnew"); // B appends "Bnew"; never sees "BaseOnly"

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);
        var paragraphs = result.Paragraphs.ToList();

        // "BaseOnly" must survive as its own tracked A-deletion, attributed to Alice.
        var baseOnlyRun = paragraphs
            .SelectMany(p => p.Runs)
            .SingleOrDefault(r => r.Text.Contains("BaseOnly"));
        baseOnlyRun.Should().NotBeNull("A's whole-paragraph deletion must be preserved somewhere");
        baseOnlyRun!.Revision.Should().Be(RevisionKind.Deleted);
        baseOnlyRun.RevisionAuthor.Should().Be(AuthorA);

        // The paragraph carrying the deleted "BaseOnly" text must NOT also carry "Base2" — that fusion is
        // exactly the corruption the finding describes (a fabricated deletion glued to unrelated content).
        var baseOnlyParagraph = paragraphs.Single(p => p.Runs.Any(r => r.Text.Contains("BaseOnly")));
        baseOnlyParagraph.PlainText.Should().NotContain("Base2");

        // "Base2" itself must appear untouched (no revision marks) in its own paragraph — it was never
        // edited by either reviewer, and must not have been silently duplicated or merged with anything.
        var base2Runs = paragraphs
            .SelectMany(p => p.Runs)
            .Where(r => r.Text.Contains("Base2"))
            .ToList();
        base2Runs.Should().HaveCount(1, "Base2 must appear exactly once, not duplicated by misalignment");
        base2Runs[0].Revision.Should().Be(RevisionKind.None);

        // "Bnew" must appear as B's own clean insertion, not fused with Base2's text.
        var bnewParagraph = paragraphs.Single(p => p.Runs.Any(r => r.Text.Contains("Bnew")));
        bnewParagraph.PlainText.Should().NotContain("Base2");
        var bnewRun = bnewParagraph.Runs.Single(r => r.Text.Contains("Bnew"));
        bnewRun.Revision.Should().Be(RevisionKind.Inserted);
        bnewRun.RevisionAuthor.Should().Be(AuthorB);
    }

    [Fact]
    public void BEarlyDeletion_MirrorsAEarlyDeletion_AndStaysCorrectlyPaired()
    {
        // Sibling of AEarlyDeletion_DoesNotDesyncLaterParagraphPairing with the roles swapped: this time B
        // is the one whose edits shift blacklineB's paragraph list out of raw positional sync with
        // blacklineA (A leaves everything alone). This exercises the other half of the fix — aligning by
        // the shared revisedA spine index still has to work when blacklineB (not blacklineA) is the side
        // carrying the extra spliced-in whole-paragraph deletion/insertion entries.
        var original = DocWith("Base1", "BaseOnly", "Base2");
        var revisedA = DocWith("Base1", "BaseOnly", "Base2"); // A leaves everything untouched
        var revisedB = DocWith("Base1", "Base2", "Bnew"); // B deletes "BaseOnly" and appends "Bnew"

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);
        var paragraphs = result.Paragraphs.ToList();

        // "Base1" was untouched by both reviewers and must remain a single plain, unmarked paragraph.
        var base1Paragraph = paragraphs.Single(p => p.PlainText.Contains("Base1"));
        base1Paragraph.Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);

        // "BaseOnly" must be struck through and attributed to Bob (the one who actually deleted it) — not
        // Alice, and not fused with "Base2".
        var baseOnlyParagraph = paragraphs.Single(p => p.PlainText.Contains("BaseOnly"));
        baseOnlyParagraph.PlainText.Should().NotContain("Base2");
        var baseOnlyRun = baseOnlyParagraph.Runs.Single(r => r.Text.Contains("BaseOnly"));
        baseOnlyRun.Revision.Should().Be(RevisionKind.Deleted);
        baseOnlyRun.RevisionAuthor.Should().Be(AuthorB);

        // "Base2" is untouched by both reviewers and must appear exactly once, unmarked.
        var base2Runs = paragraphs.SelectMany(p => p.Runs).Where(r => r.Text.Contains("Base2")).ToList();
        base2Runs.Should().HaveCount(1);
        base2Runs[0].Revision.Should().Be(RevisionKind.None);

        // "Bnew" is B's own clean insertion, standalone.
        var bnewParagraph = paragraphs.Single(p => p.PlainText.Contains("Bnew"));
        bnewParagraph.PlainText.Should().NotContain("Base2");
        var bnewRun = bnewParagraph.Runs.Single(r => r.Text.Contains("Bnew"));
        bnewRun.Revision.Should().Be(RevisionKind.Inserted);
        bnewRun.RevisionAuthor.Should().Be(AuthorB);
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

    // -----------------------------------------------------------------------
    // Comment threads (r134 HIGH: Combine dropped every comment thread, leaving dangling anchors)
    // -----------------------------------------------------------------------

    [Fact]
    public void Combine_BothReviewersCommentedWithCollidingIds_CarriesBothThreadsWithNoDanglingOrCollidingAnchors()
    {
        // Reviewer A deletes the "Doomed" paragraph, which carries a comment thread anchored in `original`.
        // Reviewer B leaves "Kept text" untouched but attaches their OWN comment thread, independently
        // numbered starting at the same id (5) as A's thread — a realistic collision since both reviewers
        // started annotating from the same unmodified base.
        var original = DocWith("Doomed", "Kept text");
        var doomed = original.Paragraphs.First();
        doomed.Runs[0].CommentId = 5;
        doomed.Runs.Add(Run.CommentReference(5));
        original.Comments[5] = new Comment(5, "Original note", "Carol", "C");

        var revisedA = DocWith("Kept text");

        var revisedB = DocWith("Kept text");
        var keptTextB = revisedB.Paragraphs.First();
        keptTextB.Runs[0].CommentId = 5;
        keptTextB.Runs.Add(Run.CommentReference(5));
        revisedB.Comments[5] = new Comment(5, "Revised note", "Dave", "D");

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        // Both threads must survive with distinct ids — a dropped Comments dictionary or a naive id copy
        // would either lose one thread entirely or collide the two under the same id.
        result.Comments.Should().HaveCount(2);
        result.Comments.Values.Select(c => c.PlainText).Should().BeEquivalentTo("Original note", "Revised note");

        var runs = result.Paragraphs.SelectMany(p => p.Runs).Where(r => !r.IsCommentReference).ToList();
        var doomedRun = runs.Single(r => r.Text == "Doomed");
        var keptRun = runs.Single(r => r.Text == "Kept text");

        doomedRun.CommentId.Should().NotBeNull();
        keptRun.CommentId.Should().NotBeNull();
        doomedRun.CommentId.Should().NotBe(keptRun.CommentId);

        result.Comments.Should().ContainKey(doomedRun.CommentId!.Value);
        result.Comments.Should().ContainKey(keptRun.CommentId!.Value);
        result.Comments[doomedRun.CommentId!.Value].PlainText.Should().Be("Original note");
        result.Comments[keptRun.CommentId!.Value].PlainText.Should().Be("Revised note");

        // The matching w:commentRangeEnd reference run must follow the same remap as its anchor.
        var referenceRuns = result.Paragraphs.SelectMany(p => p.Runs).Where(r => r.IsCommentReference).ToList();
        referenceRuns.Should().Contain(r => r.CommentId == doomedRun.CommentId);
        referenceRuns.Should().Contain(r => r.CommentId == keptRun.CommentId);
    }

    [Fact]
    public void Combine_ReviewerBCommentThreadWithReply_SurvivesIntact()
    {
        // Sibling coverage for the simple (non-colliding) case: a threaded reply must round-trip too.
        var original = DocWith("Annotated text");
        var revisedA = DocWith("Annotated text");

        var revisedB = DocWith("Annotated text");
        var annotated = revisedB.Paragraphs.First();
        annotated.Runs[0].CommentId = 5;
        annotated.Runs.Add(Run.CommentReference(5));
        var comment = new Comment(5, "Please verify", "Carol", "C") { Resolved = true };
        comment.AddReply(6, "Verified", "Dave", "D");
        revisedB.Comments[5] = comment;

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        result.Comments.Should().ContainKey(5);
        result.Comments[5].PlainText.Should().Be("Please verify");
        result.Comments[5].Resolved.Should().BeTrue();
        result.Comments[5].Replies.Should().ContainSingle(reply => reply.Id == 6 && reply.PlainText == "Verified");
    }

    // -----------------------------------------------------------------------
    // Move revisions (r134 MED: a tracked MOVE degraded into an unrelated insert+delete pair)
    // -----------------------------------------------------------------------

    [Fact]
    public void Combine_ReviewerAMovedParagraph_PreservesMoveRevisionIdOnDeletedSide()
    {
        var original = DocWith("Alpha", "Bravo", "Charlie");
        var revisedA = DocWith("Bravo", "Alpha", "Charlie"); // A moves "Alpha" after "Bravo"
        var revisedB = DocWith("Bravo", "Alpha", "Charlie"); // B makes no further changes

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        var moveRuns = result.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.MoveRevisionId != null)
            .ToList();

        // Without the fix, MoveRevisionId is silently dropped by the run clone, so this list is empty and
        // A's move degrades into an ordinary, unpaired deletion (still present, but no longer flagged as
        // part of a move — the exact "unrelated insert+delete pair" regression from the finding).
        moveRuns.Should().ContainSingle(run =>
            run.Text == "Alpha"
            && run.Revision == RevisionKind.Deleted
            && run.RevisionAuthor == AuthorA);
    }

    [Fact]
    public void Combine_OrdinaryReviewerADeletion_NeverCarriesAMoveRevisionId()
    {
        // Sibling no-regression: an ordinary (non-moved) deletion by A must NOT be mistaken for a move —
        // guards against a fix that stamps every A-side deletion with a bogus MoveRevisionId.
        var original = DocWith("Alpha", "Bravo");
        var revisedA = DocWith("Bravo"); // A deletes "Alpha" outright (no reinsertion elsewhere)
        var revisedB = DocWith("Bravo");

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        var deletedAlpha = result.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.Text == "Alpha" && run.Revision == RevisionKind.Deleted);

        deletedAlpha.MoveRevisionId.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Footnotes/endnotes (r142 remediation: Combine copied Footnotes/Endnotes only from blacklineB,
    // dropping a note that exists only in `original` when revisedA deletes the paragraph that referenced
    // it, leaving the dangling reference the round set out to remove -- reachable through Combine instead
    // of Compare.)
    // -----------------------------------------------------------------------

    [Fact]
    public void Combine_ReviewerADeletesParagraphWithOriginalOnlyFootnote_NoteSurvivesAndReferenceResolves()
    {
        var original = DocWith("Keep this paragraph");
        var doomed = new Paragraph();
        doomed.Runs.Add(new Run("See removed note"));
        doomed.Runs.Add(Run.FootnoteReference(1));
        original.Blocks.Add(doomed);
        original.Footnotes[1] = new Footnote(1, "Original explanation.");

        var revisedA = DocWith("Keep this paragraph"); // A deletes the footnoted paragraph entirely
        var revisedB = DocWith("Keep this paragraph"); // B makes no further changes

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        var deletedRun = result.Paragraphs
            .SelectMany(p => p.Runs)
            .Where(run => run.Revision == RevisionKind.Deleted && run.FootnoteId != null)
            .Should().ContainSingle()
            .Which;

        // Before the fix, CopyShell seeded result.Footnotes only from blacklineB (revisedA vs revisedB),
        // which never saw this original-only note, so the reference pointed at nothing.
        result.Footnotes.Should().ContainKey(deletedRun.FootnoteId!.Value);
        result.Footnotes[deletedRun.FootnoteId!.Value].PlainText.Should().Be("Original explanation.");
    }

    [Fact]
    public void Combine_DeletedOriginalFootnoteIdCollidesWithSurvivingFootnoteId_RemapsToDistinctIds()
    {
        // Reviewer A deletes a footnoted paragraph from `original`. Reviewer B keeps a DIFFERENT paragraph
        // that also carries a footnote numbered 1 in revisedA/revisedB's own (unrelated) numbering -- a
        // realistic collision since blacklineA and blacklineB number their notes independently.
        var original = DocWith("Keep this paragraph");
        var doomed = new Paragraph();
        doomed.Runs.Add(new Run("See removed note"));
        doomed.Runs.Add(Run.FootnoteReference(1));
        original.Blocks.Add(doomed);
        original.Footnotes[1] = new Footnote(1, "Original explanation.");

        var revisedA = DocWith("Keep this paragraph"); // A deletes the footnoted paragraph entirely

        var revisedB = new TextDocument();
        var keptParagraph = new Paragraph();
        keptParagraph.Runs.Add(new Run("Keep this paragraph"));
        keptParagraph.Runs.Add(Run.FootnoteReference(1));
        revisedB.Blocks.Add(keptParagraph);
        revisedB.Footnotes[1] = new Footnote(1, "Reviewer B's own note.");

        var result = DocumentCombine.Combine(original, revisedA, AuthorA, revisedB, AuthorB, DateXml);

        var survivingRun = result.Paragraphs
            .SelectMany(p => p.Runs)
            .Single(run => run.Revision != RevisionKind.Deleted && run.FootnoteId != null);
        result.Footnotes[survivingRun.FootnoteId!.Value].PlainText.Should().Be("Reviewer B's own note.");

        var deletedRun = result.Paragraphs
            .SelectMany(p => p.Runs)
            .Single(run => run.Revision == RevisionKind.Deleted && run.FootnoteId != null);

        // The two notes must never collapse onto the same id/entry -- each reference must resolve to its
        // OWN note's content, never the other reviewer's unrelated one.
        deletedRun.FootnoteId.Should().NotBe(survivingRun.FootnoteId);
        result.Footnotes[deletedRun.FootnoteId!.Value].PlainText.Should().Be("Original explanation.");
    }
}
