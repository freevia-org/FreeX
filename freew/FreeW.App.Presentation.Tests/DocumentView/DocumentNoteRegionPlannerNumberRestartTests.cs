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

    [Fact]
    public void Continuous_OrdersFootnotesByReadingPosition_NotByInternalId()
    {
        // r152 finding freew-footnote-numbering F1: mirrors the user gesture exactly. Insert a footnote
        // after the SECOND sentence first (it becomes id 1), then insert a footnote after the FIRST
        // sentence (it becomes id 2, since NextFootnoteId() = max(existing)+1). Id 2's reference mark is
        // physically first when reading the page, so it must display as "1"; id 1's mark is physically
        // second, so it must display as "2" -- the opposite of id order.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var firstSentence = new Paragraph();
        firstSentence.Runs.Add(new Run("First sentence."));
        firstSentence.Runs.Add(Run.FootnoteReference(2));
        document.Blocks.Add(firstSentence);

        var secondSentence = new Paragraph();
        secondSentence.Runs.Add(new Run("Second sentence."));
        secondSentence.Runs.Add(Run.FootnoteReference(1));
        document.Blocks.Add(secondSentence);

        document.Footnotes[1] = new Footnote(1, "inserted second, reads second");
        document.Footnotes[2] = new Footnote(2, "inserted last, reads first");

        var sequenceById = DocumentNoteRegionPlanner.ComputeSequenceById(document, isFootnote: true);

        sequenceById.Should().Equal(new Dictionary<int, int> { [2] = 1, [1] = 2 },
            "id 2's reference appears first in reading order, so it must display as 1 even though its internal id is higher");
    }

    [Fact]
    public void Continuous_WithIdsAlreadyInReadingOrder_NoRegression()
    {
        // Sibling no-regression check for F1: the ordinary case, where insertion order, id order, and
        // reading-order position all agree, must keep numbering exactly as before.
        var document = BuildDocumentWithFootnotes(1, 2, 3);

        var sequenceById = DocumentNoteRegionPlanner.ComputeSequenceById(document, isFootnote: true);

        sequenceById.Should().Equal(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 });
    }

    [Fact]
    public void EachSection_RestartsFootnoteReferencedOnlyFromATextBox_InANewSection()
    {
        // r152 finding freew-footnote-numbering F2: id 2's ONLY reference run lives inside a text box
        // (Run.Shape.TextParagraphs) in the second section. The old Scan only looked at a paragraph's own
        // Runs, never descended into a shape's text-box content, so it could never find id 2's reference
        // and silently defaulted it to section 0 alongside id 1 -- continuing the section-1 series instead
        // of restarting for its own section.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachSection;

        var sectionOneLast = new Paragraph { SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage) };
        sectionOneLast.Runs.Add(new Run("s1"));
        sectionOneLast.Runs.Add(Run.FootnoteReference(1));
        document.Blocks.Add(sectionOneLast);

        var textBoxParagraph = new Paragraph();
        textBoxParagraph.Runs.Add(new Run("box text"));
        textBoxParagraph.Runs.Add(Run.FootnoteReference(2));
        var sectionTwo = new Paragraph();
        sectionTwo.Runs.Add(new Run(string.Empty) { Shape = new Shape { TextParagraphs = { textBoxParagraph } } });
        document.Blocks.Add(sectionTwo);

        document.Footnotes[1] = new Footnote(1, "one");
        document.Footnotes[2] = new Footnote(2, "two");

        var sequenceById = DocumentNoteRegionPlanner.ComputeSequenceById(document, isFootnote: true);

        sequenceById.Should().Equal(new Dictionary<int, int> { [1] = 1, [2] = 1 },
            "id 2's only reference lives inside a text box in the second section, so it must restart at StartAt there instead of defaulting to section 0");
    }

    [Fact]
    public void EachSection_RestartsFootnoteReferencedOnlyFromAHeader_InANewSection()
    {
        // Sibling case for F2: the reference lives in the second section's own header rather than a text
        // box. Word allows a footnote/endnote reference in a header/footer; the old Scan never walked
        // document.Sections[i].HeadersFooters at all, so this id also silently defaulted to section 0.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachSection;

        var headerParagraph = new Paragraph();
        headerParagraph.Runs.Add(new Run("header text"));
        headerParagraph.Runs.Add(Run.FootnoteReference(2));

        var sectionOneLast = new Paragraph { SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage) };
        sectionOneLast.Runs.Add(new Run("s1"));
        sectionOneLast.Runs.Add(Run.FootnoteReference(1));
        document.Blocks.Add(sectionOneLast);

        var sectionTwo = new Paragraph();
        sectionTwo.Runs.Add(new Run("s2"));
        document.Blocks.Add(sectionTwo);

        // sectionOneLast's SectionBreak makes the document two sections; the second (index 1) is the
        // trailing/final section, whose header slot is document.Header (== FinalSectionHeadersFooters.Header).
        document.Header = new HeaderFooter { Paragraphs = { headerParagraph } };

        document.Footnotes[1] = new Footnote(1, "one");
        document.Footnotes[2] = new Footnote(2, "two");

        var sequenceById = DocumentNoteRegionPlanner.ComputeSequenceById(document, isFootnote: true);

        sequenceById.Should().Equal(new Dictionary<int, int> { [1] = 1, [2] = 1 },
            "id 2's only reference lives in the second section's own header, so it must restart at StartAt there instead of defaulting to section 0");
    }

    [Fact]
    public void EachSection_TableCellReference_NoRegression()
    {
        // Sibling no-regression check: a reference inside a table cell (the branch this fix did not
        // change) must still be attributed to its own section correctly.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachSection;

        var sectionOneLast = new Paragraph { SectionBreak = new Section(new PageSettings(), SectionBreakKind.NextPage) };
        sectionOneLast.Runs.Add(new Run("s1"));
        sectionOneLast.Runs.Add(Run.FootnoteReference(1));
        document.Blocks.Add(sectionOneLast);

        var cellParagraph = new Paragraph();
        cellParagraph.Runs.Add(new Run("cell text"));
        cellParagraph.Runs.Add(Run.FootnoteReference(2));
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Paragraphs.Add(cellParagraph);
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Blocks.Add(table);

        document.Footnotes[1] = new Footnote(1, "one");
        document.Footnotes[2] = new Footnote(2, "two");

        var sequenceById = DocumentNoteRegionPlanner.ComputeSequenceById(document, isFootnote: true);

        sequenceById.Should().Equal(new Dictionary<int, int> { [1] = 1, [2] = 1 },
            "id 2's reference is in a table cell in the second section, so it must restart there, unchanged by this fix");
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
