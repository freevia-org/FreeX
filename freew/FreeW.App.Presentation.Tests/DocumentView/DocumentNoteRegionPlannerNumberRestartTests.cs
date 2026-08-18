using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.DocumentView;

/// <summary>
/// Covers <see cref="DocumentNoteRegionPlanner.ComputeSequenceById"/> — the single authoritative
/// sequence calculator every note renderer (WPF note-region/body-mark builders, Avalonia PrintLayout
/// note bands + body-mark glyphs, the accessibility tree) must call. Before this was introduced, every
/// call site rolled its own "StartAt + index" continuous series that never looked at
/// <see cref="NoteNumberingOptions.NumberRestart"/>, so choosing "Restart each page" or "Restart each
/// section" in the Footnote/Endnote Options dialog had no visible effect anywhere.
/// </summary>
public sealed class DocumentNoteRegionPlannerNumberRestartTests
{
    [Fact]
    public void Continuous_NumbersFootnotesAcrossWholeDocument_FromStartAt()
    {
        var document = BuildDocumentWithFootnotes(1, 2, 3);
        document.FootnoteNumbering.StartAt = 5;
        // NumberRestart defaults to Continuous.

        var sequenceById = DocumentNoteRegionPlanner.ComputeSequenceById(document, isFootnote: true);

        sequenceById.Should().Equal(new Dictionary<int, int> { [1] = 5, [2] = 6, [3] = 7 });
    }

    [Fact]
    public void Continuous_AfterDeletingAnEarlierNote_ShiftsSurvivingIdsDown()
    {
        // The exact scenario the body-mark bug produced: three notes inserted (ids 1,2,3), the first
        // deleted. Word keeps the surviving ids stable (2, 3) but their DISPLAY sequence shifts to 1, 2.
        var document = BuildDocumentWithFootnotes(1, 2, 3);
        document.Footnotes.Remove(1);

        var sequenceById = DocumentNoteRegionPlanner.ComputeSequenceById(document, isFootnote: true);

        sequenceById.Should().Equal(new Dictionary<int, int> { [2] = 1, [3] = 2 });
    }

    [Fact]
    public void EachSection_RestartsFootnoteSequence_AtEveryDocumentSection()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachSection;

        var sectionOneFirst = new Paragraph();
        sectionOneFirst.Runs.Add(new Run("s1a"));
        sectionOneFirst.Runs.Add(Run.FootnoteReference(1));
        document.Blocks.Add(sectionOneFirst);

        // This paragraph's SectionBreak marks it as the LAST paragraph of section 0 — its own
        // reference (id 2) is still numbered inside section 0.
        var sectionOneLast = new Paragraph { SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage) };
        sectionOneLast.Runs.Add(new Run("s1b"));
        sectionOneLast.Runs.Add(Run.FootnoteReference(2));
        document.Blocks.Add(sectionOneLast);

        var sectionTwo = new Paragraph();
        sectionTwo.Runs.Add(new Run("s2a"));
        sectionTwo.Runs.Add(Run.FootnoteReference(3));
        document.Blocks.Add(sectionTwo);

        document.Footnotes[1] = new Footnote(1, "one");
        document.Footnotes[2] = new Footnote(2, "two");
        document.Footnotes[3] = new Footnote(3, "three");

        var sequenceById = DocumentNoteRegionPlanner.ComputeSequenceById(document, isFootnote: true);

        sequenceById.Should().Equal(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 1 },
            "id 3 is the first footnote of the second section, so it must restart at StartAt");
    }

    [Fact]
    public void EachSection_RestartsEndnoteSequence_TooNotOnlyFootnotes()
    {
        // Sibling of the footnote EachSection test: endnotes must get the same restart treatment
        // (a distinct code path in TextDocument.Endnotes / EndnoteNumbering).
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.EndnoteNumbering.NumberRestart = NoteNumberRestart.EachSection;

        var sectionOne = new Paragraph { SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage) };
        sectionOne.Runs.Add(new Run("s1"));
        sectionOne.Runs.Add(Run.EndnoteReference(10));
        document.Blocks.Add(sectionOne);

        var sectionTwo = new Paragraph();
        sectionTwo.Runs.Add(new Run("s2"));
        sectionTwo.Runs.Add(Run.EndnoteReference(20));
        document.Blocks.Add(sectionTwo);

        document.Endnotes[10] = new Endnote(10, "ten");
        document.Endnotes[20] = new Endnote(20, "twenty");

        var sequenceById = DocumentNoteRegionPlanner.ComputeSequenceById(document, isFootnote: false);

        sequenceById.Should().Equal(new Dictionary<int, int> { [10] = 1, [20] = 1 },
            "endnote 20 opens the second section, so it restarts at StartAt just like the footnote case");
    }

    [Fact]
    public void EachPage_RestartsWithinTheSuppliedPageGroup_IgnoringOtherDocumentIds()
    {
        var document = BuildDocumentWithFootnotes(1, 2, 3, 4);
        document.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachPage;
        document.FootnoteNumbering.StartAt = 1;

        // Simulates the per-page note-region call: only the ids landing on THIS physical page are
        // passed as the page group (ids 1,2 belong to an earlier page; not part of this call).
        var sequenceById = DocumentNoteRegionPlanner.ComputeSequenceById(document, isFootnote: true, [3, 4]);

        sequenceById.Should().Equal(new Dictionary<int, int> { [3] = 1, [4] = 2 });
        sequenceById.Should().NotContainKey(1);
        sequenceById.Should().NotContainKey(2);
    }

    [Fact]
    public void EachPage_WithoutAPageGroup_FallsBackToContinuous()
    {
        // Height/measurement-only callers (and endnotes, which never supply a page group — Word offers
        // no "restart each page" option for endnotes) get a safe continuous fallback instead of throwing
        // or numbering everything as page 1.
        var document = BuildDocumentWithFootnotes(1, 2, 3);
        document.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachPage;

        var sequenceById = DocumentNoteRegionPlanner.ComputeSequenceById(document, isFootnote: true);

        sequenceById.Should().Equal(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 });
    }

    [Fact]
    public void BuildFootnoteRegion_EachPageRestart_ProducesRestartedLabelsPerPageCall()
    {
        // End-to-end through the public API real renderers call (PageBox/PaginationEngine/
        // PrintPreviewWindow): building the note region once per physical page with that page's own
        // footnote ids must restart the visible label at StartAt on every call.
        var document = BuildDocumentWithFootnotes(1, 2, 3);
        document.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachPage;

        var pageOnePlan = DocumentNoteRegionPlanner.BuildFootnoteRegion(document, [1, 2], pageNumber: 1, contentWidthDip: 400);
        var pageTwoPlan = DocumentNoteRegionPlanner.BuildFootnoteRegion(document, [3], pageNumber: 2, contentWidthDip: 400);

        pageOnePlan.Rows.Select(r => r.Label).Should().Equal("1", "2");
        pageTwoPlan.Rows.Select(r => r.Label).Should().Equal("1");
    }

    [Fact]
    public void BuildFootnoteRegion_Continuous_NoRegression_LabelsRunAcrossPages()
    {
        // Sibling no-regression check for the same two-call shape, but under the default Continuous
        // restart: labels must keep counting across page boundaries instead of restarting.
        var document = BuildDocumentWithFootnotes(1, 2, 3);
        // NumberRestart left at its Continuous default.

        var pageOnePlan = DocumentNoteRegionPlanner.BuildFootnoteRegion(document, [1, 2], pageNumber: 1, contentWidthDip: 400);
        var pageTwoPlan = DocumentNoteRegionPlanner.BuildFootnoteRegion(document, [3], pageNumber: 2, contentWidthDip: 400);

        pageOnePlan.Rows.Select(r => r.Label).Should().Equal("1", "2");
        pageTwoPlan.Rows.Select(r => r.Label).Should().Equal("3");
    }

    [Fact]
    public void BuildFootnoteRegion_EmptyFootnoteText_StillProducesARow_WithCorrectNumbering()
    {
        // r140 finding freew-notes-3: a footnote whose text is empty (or whitespace-only) was dropped
        // from the note region entirely, even though its reference mark still shows in the body — Word
        // still prints the separator plus an empty numbered line for it.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("body "));
        paragraph.Runs.Add(Run.FootnoteReference(1));
        paragraph.Runs.Add(new Run(" more "));
        paragraph.Runs.Add(Run.FootnoteReference(2));
        document.Blocks.Add(paragraph);

        // Note 1 was inserted but never typed into (or its text was selected and deleted) — its content
        // is a single blank paragraph, exactly what ReplaceNoteContentCommand leaves behind.
        var emptyFootnote = new Footnote(1);
        emptyFootnote.Content.Add(new Paragraph());
        document.Footnotes[1] = emptyFootnote;
        document.Footnotes[2] = new Footnote(2, "second note text");

        var plan = DocumentNoteRegionPlanner.BuildFootnoteRegion(document, [1, 2], pageNumber: 1, contentWidthDip: 400);

        plan.Rows.Should().HaveCount(2, "the empty footnote must still contribute a row instead of being dropped");
        plan.Rows[0].NoteId.Should().Be(1);
        plan.Rows[0].Label.Should().Be("1");
        plan.Rows[0].Text.Should().BeEmpty();
        plan.Rows[1].NoteId.Should().Be(2);
        plan.Rows[1].Label.Should().Be("2", "the surviving note's display number must not shift because of the empty note");
        plan.Rows[1].Text.Should().Be("second note text");
        plan.HasContent.Should().BeTrue();
    }

    [Fact]
    public void BuildFootnoteRegion_NonEmptyFootnotes_Unaffected_ByEmptyNoteHandling()
    {
        // Sibling no-regression check: ordinary non-empty footnotes must still produce exactly one row
        // each, with unchanged labels and text, after the empty-note fix above.
        var document = BuildDocumentWithFootnotes(1, 2, 3);

        var plan = DocumentNoteRegionPlanner.BuildFootnoteRegion(document, [1, 2, 3], pageNumber: 1, contentWidthDip: 400);

        plan.Rows.Should().HaveCount(3);
        plan.Rows.Select(r => r.Label).Should().Equal("1", "2", "3");
        plan.Rows.Select(r => r.Text).Should().Equal("note 1", "note 2", "note 3");
    }

    [Fact]
    public void BuildFootnoteRegion_IdWithNoNoteAtAll_IsStillSkipped()
    {
        // An id with no backing Footnote entry at all (as opposed to one with empty text) must still be
        // skipped — the fix only stops dropping notes that exist but have blank content.
        var document = BuildDocumentWithFootnotes(1);

        var plan = DocumentNoteRegionPlanner.BuildFootnoteRegion(document, [1, 999], pageNumber: 1, contentWidthDip: 400);

        plan.Rows.Should().HaveCount(1);
        plan.Rows[0].NoteId.Should().Be(1);
    }

    private static TextDocument BuildDocumentWithFootnotes(params int[] ids)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        foreach (var id in ids)
        {
            paragraph.Runs.Add(new Run($"text{id} "));
            paragraph.Runs.Add(Run.FootnoteReference(id));
            document.Footnotes[id] = new Footnote(id, $"note {id}");
        }
        document.Blocks.Add(paragraph);
        return document;
    }
}
