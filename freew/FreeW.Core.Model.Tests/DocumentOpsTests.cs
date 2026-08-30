namespace FreeW.Core.Model.Tests;

public class DocumentOpsTests
{
    [Fact]
    public void InsertCoverPage_PrependsTitleAndSubtitle_FromProperties()
    {
        var doc = new TextDocument();
        doc.Properties.Title = "Annual Report";
        doc.Properties.Author = "Ada Lovelace";
        doc.Blocks.Add(new Paragraph("Body"));

        DocumentOps.InsertCoverPage(doc);

        var paragraphs = doc.Blocks.OfType<Paragraph>().ToList();
        paragraphs[0].StyleId.Should().Be("Title");
        paragraphs[0].PlainText.Should().Be("Annual Report");
        paragraphs[1].StyleId.Should().Be("Subtitle");
        paragraphs[1].PlainText.Should().Be("Ada Lovelace");
        // The spacer, then the original body, follow.
        paragraphs[2].PlainText.Should().BeEmpty();
        paragraphs.Last().PlainText.Should().Be("Body");
    }

    [Fact]
    public void BuildCoverPage_UsesPlaceholderTitle_AndOmitsSubtitle_WhenPropertiesUnset()
    {
        var doc = new TextDocument();

        var blocks = DocumentOps.BuildCoverPage(doc).OfType<Paragraph>().ToList();

        // No author -> no subtitle paragraph: just the placeholder Title and a spacer.
        blocks.Should().HaveCount(2);
        blocks[0].StyleId.Should().Be("Title");
        blocks[0].PlainText.Should().Be(DocumentOps.DefaultCoverTitle);
        blocks[1].PlainText.Should().BeEmpty();
        blocks.Any(p => p.StyleId == "Subtitle").Should().BeFalse();
    }

    [Fact]
    public void CreatePageBreak_SetsPageBreakBeforeFlag()
    {
        var paragraph = DocumentOps.CreatePageBreak();

        paragraph.Formatting.PageBreakBefore.Should().BeTrue();
        paragraph.PlainText.Should().BeEmpty();
    }

    [Fact]
    public void CreateColumnBreak_CreatesDistinctBreakRun()
    {
        var paragraph = DocumentOps.CreateColumnBreak();

        paragraph.Formatting.PageBreakBefore.Should().BeFalse();
        paragraph.Runs.Should().ContainSingle(run => run.IsColumnBreak);
        paragraph.Runs.Should().NotContain(run => run.IsPageBreak);
        paragraph.PlainText.Should().BeEmpty();
    }

    [Fact]
    public void BuildBlankPage_CreatesTwoPageBreakParagraphs()
    {
        var blocks = DocumentOps.BuildBlankPage().OfType<Paragraph>().ToList();

        blocks.Should().HaveCount(2);
        blocks.Should().OnlyContain(p => p.Formatting.PageBreakBefore);
        blocks.Should().OnlyContain(p => p.PlainText.Length == 0);
    }

    [Fact]
    public void CreateHorizontalRule_SetsBottomOnlyBorder()
    {
        var paragraph = DocumentOps.CreateHorizontalRule();

        paragraph.Formatting.Border.Should().NotBeNull();
        paragraph.Formatting.Border!.BottomOnly.Should().BeTrue();
        paragraph.PlainText.Should().BeEmpty();
    }

    [Fact]
    public void ParagraphBorder_DefaultsToFullBox_NonBreakingForExistingCallers()
    {
        // Existing callers that omit BottomOnly keep a full box (the historical behaviour).
        var border = new ParagraphBorder("#FF0000", 1.5);

        border.BottomOnly.Should().BeFalse();
    }

    [Theory]
    [InlineData(SectionBreakKind.NextPage)]
    [InlineData(SectionBreakKind.Continuous)]
    [InlineData(SectionBreakKind.EvenPage)]
    [InlineData(SectionBreakKind.OddPage)]
    public void CreateSectionBreak_SetsCorrectBreakKind(SectionBreakKind kind)
    {
        var paragraph = DocumentOps.CreateSectionBreak(kind);

        paragraph.SectionBreak.Should().NotBeNull();
        paragraph.SectionBreak!.BreakKind.Should().Be(kind);
        paragraph.PlainText.Should().BeEmpty();
    }

    [Fact]
    public void CreateSectionBreak_InheritsPageSettings_WhenProvided()
    {
        var page = new PageSettings { MarginLeftPt = 99 };

        var paragraph = DocumentOps.CreateSectionBreak(SectionBreakKind.NextPage, page);

        paragraph.SectionBreak!.Page.MarginLeftPt.Should().Be(99);
        paragraph.SectionBreak!.Page.Should().NotBeSameAs(page); // cloned, not the same reference
    }

    [Fact]
    public void CreateSectionBreak_UsesDefaultPageSettings_WhenNoneProvided()
    {
        var paragraph = DocumentOps.CreateSectionBreak(SectionBreakKind.Continuous);

        paragraph.SectionBreak.Should().NotBeNull();
        paragraph.SectionBreak!.Page.Should().NotBeNull();
    }

    // --- freew-sections-headers F1: CreateSectionBreak must not blank the header/footer that was
    // showing on the pages before the break -- see FreeW.Core.Model.DocumentOps.CreateSectionBreak's
    // inheritedHeadersFooters parameter and ResolveInheritedHeadersFooters below.

    [Fact]
    public void CreateSectionBreak_UsesEmptyHeadersFooters_WhenNoneProvided()
    {
        // Sibling no-regression case: callers that omit the new parameter (there are none left in the
        // shipping app, but this pins the default) keep the pre-fix behaviour of a brand-new, empty
        // SectionHeadersFooters -- unchanged from before this fix.
        var paragraph = DocumentOps.CreateSectionBreak(SectionBreakKind.NextPage);

        var headersFooters = paragraph.SectionBreak!.HeadersFooters;
        headersFooters.IsEmpty.Should().BeTrue();
        headersFooters.Header.Should().BeNull();
        headersFooters.Footer.Should().BeNull();
    }

    [Fact]
    public void CreateSectionBreak_InheritsHeadersFooters_WhenProvided()
    {
        var inherited = new SectionHeadersFooters
        {
            Header = new HeaderFooter("MY RUNNING HEADER"),
            Footer = new HeaderFooter("MY RUNNING FOOTER"),
            FirstHeader = new HeaderFooter("First Header")
        };

        var paragraph = DocumentOps.CreateSectionBreak(SectionBreakKind.NextPage, inheritedHeadersFooters: inherited);

        var headersFooters = paragraph.SectionBreak!.HeadersFooters;
        headersFooters.Header.Should().NotBeNull();
        headersFooters.Header!.PlainText.Should().Be("MY RUNNING HEADER");
        headersFooters.Footer!.PlainText.Should().Be("MY RUNNING FOOTER");
        headersFooters.FirstHeader!.PlainText.Should().Be("First Header");
        headersFooters.EvenHeader.Should().BeNull();
        headersFooters.EvenFooter.Should().BeNull();
        headersFooters.FirstFooter.Should().BeNull();

        // Cloned, not shared: mutating the source afterwards must not affect the new section.
        headersFooters.Header.Should().NotBeSameAs(inherited.Header);
        inherited.Header.Paragraphs[0].Runs[0].Text = "Mutated";
        headersFooters.Header!.PlainText.Should().Be("MY RUNNING HEADER");
    }

    [Fact]
    public void ResolveInheritedHeadersFooters_SingleSectionDocument_ReturnsDocumentHeaderAndFooter()
    {
        // Direct reproduction of freew-sections-headers F1: a single-section document that already
        // has a header/footer must resolve that same header/footer for section 0 (the section the
        // caret is in before any break exists), so CreateSectionBreak can carry it into the new leading
        // section a section break creates.
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Only section."));
        document.Header = new HeaderFooter("MY RUNNING HEADER");
        document.Footer = new HeaderFooter("MY RUNNING FOOTER");

        var resolved = DocumentOps.ResolveInheritedHeadersFooters(document, sectionIndex: 0);

        resolved.Header.Should().BeSameAs(document.Header);
        resolved.Footer.Should().BeSameAs(document.Footer);
    }

    [Fact]
    public void ResolveInheritedHeadersFooters_WalksBackwardPerSlot_WhenSectionDefinesNoSlotOfItsOwn()
    {
        // Sibling no-regression case: a later section that does not define its own default header must
        // still resolve to the nearest earlier section that does (per-slot "link to previous"), matching
        // FreeW.App.Presentation.Ribbon.HeaderFooterPagePlanner's resolution used for display/print.
        var document = new TextDocument();
        var firstSection = new Section(new PageSettings());
        firstSection.HeadersFooters.Header = new HeaderFooter("Section One Header");
        document.Blocks.Add(new Paragraph("Section one body.") { SectionBreak = firstSection });
        document.Blocks.Add(new Paragraph("Section two body (defines no header of its own)."));

        var resolved = DocumentOps.ResolveInheritedHeadersFooters(document, sectionIndex: 1);

        resolved.Header.Should().BeSameAs(firstSection.HeadersFooters.Header);
    }

    [Fact]
    public void ResolveInheritedHeadersFooters_ReturnsNull_WhenNothingEarlierDefinesSlot()
    {
        // Sibling no-regression case: when a leading section genuinely has no header/footer anywhere
        // before or on it, resolution must stay blank (matching Word), not reach forward into a later
        // section that happens to define one.
        var document = new TextDocument();
        var emptyFirstSection = new Section(new PageSettings());
        document.Blocks.Add(new Paragraph("Section one body.") { SectionBreak = emptyFirstSection });
        document.Blocks.Add(new Paragraph("Section two body."));
        document.Header = new HeaderFooter("Only On The Final Section");

        var resolved = DocumentOps.ResolveInheritedHeadersFooters(document, sectionIndex: 0);

        resolved.Header.Should().BeNull();
    }

    [Theory]
    [InlineData(CoverPagePreset.Default)]
    [InlineData(CoverPagePreset.Banded)]
    [InlineData(CoverPagePreset.Motion)]
    public void BuildCoverPage_AllPresets_ProduceTitleParagraphWithDocTitle(CoverPagePreset preset)
    {
        var doc = new TextDocument();
        doc.Properties.Title = "Test Title";

        var blocks = DocumentOps.BuildCoverPage(doc, preset).OfType<Paragraph>().ToList();

        blocks.Should().NotBeEmpty();
        blocks[0].PlainText.Should().Be("Test Title");
    }

    [Theory]
    [InlineData(CoverPagePreset.Default)]
    [InlineData(CoverPagePreset.Banded)]
    [InlineData(CoverPagePreset.Motion)]
    public void BuildCoverPage_AllPresets_UseDefaultTitleWhenPropertiesUnset(CoverPagePreset preset)
    {
        var doc = new TextDocument();

        var blocks = DocumentOps.BuildCoverPage(doc, preset).OfType<Paragraph>().ToList();

        blocks[0].PlainText.Should().Be(DocumentOps.DefaultCoverTitle);
    }
}
