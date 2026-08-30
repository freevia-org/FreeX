using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentBlockInsertionMutationPlannerTests
{
    [Theory]
    [InlineData(CoverPagePreset.Default)]
    [InlineData(CoverPagePreset.Banded)]
    [InlineData(CoverPagePreset.Motion)]
    public void Cover_page_is_one_atomic_prepend_plan(CoverPagePreset preset)
    {
        var document = DocumentWithTwoParagraphs();
        document.Properties.Title = "Parity Plan";
        document.Properties.Author = "FreeW";

        var plan = DocumentBlockInsertionMutationPlanner.PlanCoverPage(document, preset);

        plan.StartIndex.Should().Be(0);
        plan.RemoveCount.Should().Be(0);
        plan.Replacement.Should().NotBeEmpty();
        plan.Replacement.Cast<Paragraph>()
            .Should().Contain(paragraph => paragraph.PlainText.Contains("Parity Plan", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(20, 2)]
    [InlineData(int.MaxValue, 2)]
    public void Insertion_index_is_clamped_after_caret(int caretBlockIndex, int expectedIndex)
    {
        var document = DocumentWithTwoParagraphs();

        var plan = DocumentBlockInsertionMutationPlanner.PlanPageBreak(document, caretBlockIndex);

        plan.StartIndex.Should().Be(expectedIndex);
        plan.RemoveCount.Should().Be(0);
        plan.Replacement.Should().ContainSingle()
            .Which.Should().BeOfType<Paragraph>()
            .Which.Formatting.PageBreakBefore.Should().BeTrue();
    }

    [Fact]
    public void Blank_page_is_one_atomic_two_block_replacement()
    {
        var plan = DocumentBlockInsertionMutationPlanner.PlanBlankPage(DocumentWithTwoParagraphs(), 0);

        plan.StartIndex.Should().Be(1);
        plan.RemoveCount.Should().Be(0);
        plan.Replacement.Should().HaveCount(2);
        plan.Replacement.Cast<Paragraph>()
            .Should().OnlyContain(paragraph => paragraph.Formatting.PageBreakBefore);
    }

    [Fact]
    public void Horizontal_rule_and_column_break_use_shared_model_factories()
    {
        var document = DocumentWithTwoParagraphs();

        var rule = DocumentBlockInsertionMutationPlanner.PlanHorizontalRule(document, 0)
            .Replacement.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which;
        rule.Formatting.Border.Should().NotBeNull();
        rule.Formatting.Border!.BottomOnly.Should().BeTrue();

        var columnBreak = DocumentBlockInsertionMutationPlanner.PlanColumnBreak(document, 0)
            .Replacement.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which;
        columnBreak.Runs.Should().ContainSingle().Which.IsColumnBreak.Should().BeTrue();
    }

    [Theory]
    [InlineData(SectionBreakKind.NextPage)]
    [InlineData(SectionBreakKind.Continuous)]
    [InlineData(SectionBreakKind.EvenPage)]
    [InlineData(SectionBreakKind.OddPage)]
    public void Section_break_inherits_document_page_settings_for_single_section_document(SectionBreakKind kind)
    {
        // Sibling no-regression case: a plain single-section document (the overwhelmingly common case)
        // must keep inheriting document.Page exactly as before the multi-section fix below, since
        // PageSettingsSectionResolver.ResolveSectionIndex short-circuits to section 0 (== document.Page)
        // whenever document.Sections.Count <= 1.
        var document = DocumentWithTwoParagraphs();
        document.Page.WidthPt = 700;
        document.Page.HeightPt = 900;

        var paragraph = DocumentBlockInsertionMutationPlanner.PlanSectionBreak(document, 1, kind)
            .Replacement.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which;

        paragraph.SectionBreak.Should().NotBeNull();
        paragraph.SectionBreak!.BreakKind.Should().Be(kind);
        paragraph.SectionBreak.Page.WidthPt.Should().Be(700);
        paragraph.SectionBreak.Page.HeightPt.Should().Be(900);
    }

    /// <summary>
    /// Regression coverage for the multi-section case: the new section-break paragraph must inherit the
    /// <see cref="PageSettings"/> of the section the caret block is actually in (the document's FIRST
    /// section here), not unconditionally <see cref="TextDocument.Page"/> (the document's final section).
    /// Mirrors
    /// <c>FreeW.App.Host.Tests.InsertSectionBreakSectionScopeTests.InsertSectionBreak_WithCaretInFirstSection_NewSectionInheritsFirstSectionPageSettings</c>.
    /// </summary>
    [Fact]
    public void Section_break_with_caret_in_first_section_inherits_first_section_page_settings()
    {
        var document = new TextDocument();
        document.Blocks.Clear();
        var section0Page = new PageSettings
        {
            MarginLeftPt = 111,
            WidthPt = 1008,
            HeightPt = 612,
            Landscape = true,
            ColumnCount = 2
        };
        document.Blocks.Add(new Paragraph("Section one body.")
        {
            SectionBreak = new Section(section0Page, SectionBreakKind.NextPage)
        });
        document.Blocks.Add(new Paragraph("Section two (final) body."));
        document.Page.MarginLeftPt = 222;
        document.Page.WidthPt = 612;
        document.Page.HeightPt = 792;
        document.Page.Landscape = false;
        document.Page.ColumnCount = 1;

        // Caret in block 0 (the first section's own body paragraph, before its section-break block).
        var paragraph = DocumentBlockInsertionMutationPlanner.PlanSectionBreak(document, 0, SectionBreakKind.NextPage)
            .Replacement.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which;

        var insertedPage = paragraph.SectionBreak!.Page;
        insertedPage.MarginLeftPt.Should().Be(section0Page.MarginLeftPt);
        insertedPage.WidthPt.Should().Be(section0Page.WidthPt);
        insertedPage.HeightPt.Should().Be(section0Page.HeightPt);
        insertedPage.Landscape.Should().Be(section0Page.Landscape);
        insertedPage.ColumnCount.Should().Be(section0Page.ColumnCount);

        // Must NOT have silently copied the document's final-section (Page) settings instead.
        insertedPage.Landscape.Should().NotBe(document.Page.Landscape);
        insertedPage.WidthPt.Should().NotBe(document.Page.WidthPt);
    }

    [Fact]
    public void Section_break_with_caret_in_final_section_inherits_final_section_page_settings()
    {
        // Sibling no-regression case: caret in the (already-final) section must keep inheriting the
        // document's final-section page settings, exactly as before this fix.
        var document = new TextDocument();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Section one body.")
        {
            SectionBreak = new Section(new PageSettings { WidthPt = 1008 }, SectionBreakKind.NextPage)
        });
        document.Blocks.Add(new Paragraph("Section two (final) body."));
        document.Page.MarginLeftPt = 222;
        document.Page.WidthPt = 612;
        document.Page.HeightPt = 792;

        // Caret in block 1 (the final section's own body paragraph).
        var paragraph = DocumentBlockInsertionMutationPlanner.PlanSectionBreak(document, 1, SectionBreakKind.NextPage)
            .Replacement.Should().ContainSingle().Which.Should().BeOfType<Paragraph>().Which;

        var insertedPage = paragraph.SectionBreak!.Page;
        insertedPage.MarginLeftPt.Should().Be(document.Page.MarginLeftPt);
        insertedPage.WidthPt.Should().Be(document.Page.WidthPt);
        insertedPage.HeightPt.Should().Be(document.Page.HeightPt);
    }

    /// <summary>
    /// End-to-end regression test for freew-sections-headers F1: Insert &gt; Section Break must not
    /// blank the header/footer that was showing on the pages before the break. Exercises the full
    /// production pipeline both editors' InsertSectionBreak and print/PDF export use: build the
    /// section-break paragraph via <see cref="DocumentBlockInsertionMutationPlanner.PlanSectionBreak"/>,
    /// then resolve per-page headers/footers via
    /// <see cref="HeaderFooterPagePlanner.MapPagesToSections(TextDocument, IReadOnlyList{int}, int)"/> and
    /// <see cref="HeaderFooterPagePlanner.ResolveSlots"/> -- the same resolver the screen editor
    /// (PaginatedEditorPanel) and print/PDF export (SectionAwareDocumentPaginator) both call. Asserts the
    /// writer (the planner that creates the section break) and the reader (the page planner that decides
    /// what to draw) agree, not just that the model carries a non-null field.
    /// </summary>
    [Fact]
    public void Section_break_preserves_effective_header_and_footer_on_pages_before_the_break()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Body before the break."));
        document.Header = new HeaderFooter("MY RUNNING HEADER");
        document.Footer = new HeaderFooter("MY RUNNING FOOTER");

        var plan = DocumentBlockInsertionMutationPlanner.PlanSectionBreak(document, 0, SectionBreakKind.NextPage);
        document.Blocks.InsertRange(plan.StartIndex, plan.Replacement);
        document.Blocks.Add(new Paragraph("Body after the break."));

        // Block 0 = body before the break (section 0), block 1 = the section-break marker paragraph
        // (section 0), block 2 = body after the break (section 1, the document's unchanged final
        // section). Page 0 renders block 0 (and the marker); page 1 renders block 2.
        var pages = HeaderFooterPagePlanner.MapPagesToSections(
            document,
            blockPageAssignments: [0, 0, 1],
            pageCount: 2);

        pages[0].SectionIndex.Should().Be(0);
        pages[1].SectionIndex.Should().Be(1);

        var pageZeroSlots = HeaderFooterPagePlanner.ResolveSlots(
            pages[0].HeadersFooters, sectionRelativePageNumber: 1, pages[0].PageSettings, differentOddEvenPages: false);
        var pageOneSlots = HeaderFooterPagePlanner.ResolveSlots(
            pages[1].HeadersFooters, sectionRelativePageNumber: 1, pages[1].PageSettings, differentOddEvenPages: false);

        // The page BEFORE the break (section 0, newly created by the break) must still show the header
        // and footer that were showing there before the break was inserted.
        pageZeroSlots.Header.Should().NotBeNull("the header must not be blanked by inserting a section break");
        pageZeroSlots.Header!.PlainText.Should().Be("MY RUNNING HEADER");
        pageZeroSlots.Footer.Should().NotBeNull("the footer must not be blanked by inserting a section break");
        pageZeroSlots.Footer!.PlainText.Should().Be("MY RUNNING FOOTER");

        // Sibling no-regression: the page AFTER the break (the document's original final section) must
        // keep showing the same header/footer, unaffected by the fix.
        pageOneSlots.Header!.PlainText.Should().Be("MY RUNNING HEADER");
        pageOneSlots.Footer!.PlainText.Should().Be("MY RUNNING FOOTER");
    }

    private static TextDocument DocumentWithTwoParagraphs()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("First"));
        document.Blocks.Add(new Paragraph("Second"));
        return document;
    }
}
