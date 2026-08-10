using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class HeaderFooterPagePlannerTests
{
    [Fact]
    public void ResolveSlots_FirstPageUsesFirstSlotsBeforeEvenSlots()
    {
        var headersFooters = new SectionHeadersFooters
        {
            Header = new HeaderFooter("Default Header"),
            Footer = new HeaderFooter("Default Footer"),
            FirstHeader = new HeaderFooter("First Header"),
            FirstFooter = new HeaderFooter("First Footer"),
            EvenHeader = new HeaderFooter("Even Header"),
            EvenFooter = new HeaderFooter("Even Footer")
        };
        var page = new PageSettings { DifferentFirstPage = true };

        var plan = HeaderFooterPagePlanner.ResolveSlots(
            headersFooters,
            sectionRelativePageNumber: 1,
            page,
            differentOddEvenPages: true);

        plan.HeaderSlot.Should().Be(HeaderFooterSlotKind.FirstHeader);
        plan.HeaderSlotName.Should().Be("first-header");
        plan.Header.Should().BeSameAs(headersFooters.FirstHeader);
        plan.FooterSlot.Should().Be(HeaderFooterSlotKind.FirstFooter);
        plan.FooterSlotName.Should().Be("first-footer");
        plan.Footer.Should().BeSameAs(headersFooters.FirstFooter);
    }

    [Fact]
    public void ResolveSlots_EvenPageUsesEvenSlotsEvenWhenSlotIsMissing()
    {
        var headersFooters = new SectionHeadersFooters
        {
            Header = new HeaderFooter("Default Header"),
            Footer = new HeaderFooter("Default Footer"),
            EvenFooter = new HeaderFooter("Even Footer")
        };

        var plan = HeaderFooterPagePlanner.ResolveSlots(
            headersFooters,
            sectionRelativePageNumber: 2,
            new PageSettings(),
            differentOddEvenPages: true);

        plan.HeaderSlot.Should().Be(HeaderFooterSlotKind.EvenHeader);
        plan.HeaderSlotName.Should().Be("even-header");
        plan.Header.Should().BeNull();
        plan.FooterSlot.Should().Be(HeaderFooterSlotKind.EvenFooter);
        plan.FooterSlotName.Should().Be("even-footer");
        plan.Footer.Should().BeSameAs(headersFooters.EvenFooter);
    }

    [Fact]
    public void ResolveSlots_PageNumberRestartUsesLogicalParity()
    {
        var headersFooters = new SectionHeadersFooters
        {
            Header = new HeaderFooter("Default Header"),
            EvenHeader = new HeaderFooter("Even Header")
        };

        var evenStart = HeaderFooterPagePlanner.ResolveSlots(
            headersFooters,
            sectionRelativePageNumber: 1,
            new PageSettings(),
            differentOddEvenPages: true,
            logicalPageNumber: 2);
        var oddStart = HeaderFooterPagePlanner.ResolveSlots(
            headersFooters,
            sectionRelativePageNumber: 2,
            new PageSettings(),
            differentOddEvenPages: true,
            logicalPageNumber: 3);

        evenStart.HeaderSlot.Should().Be(HeaderFooterSlotKind.EvenHeader);
        evenStart.Header.Should().BeSameAs(headersFooters.EvenHeader);
        oddStart.HeaderSlot.Should().Be(HeaderFooterSlotKind.Header);
        oddStart.Header.Should().BeSameAs(headersFooters.Header);
    }

    [Fact]
    public void UsesDifferentOddEvenPages_IncludesNonFinalSectionFlags()
    {
        var document = new TextDocument();
        var firstSection = new Section(new PageSettings { DifferentOddEvenPages = true });
        var sectionBreakParagraph = new Paragraph("Section 1") { SectionBreak = firstSection };
        document.Blocks.Add(sectionBreakParagraph);
        document.Blocks.Add(new Paragraph("Section 2"));

        HeaderFooterPagePlanner.UsesDifferentOddEvenPages(document).Should().BeTrue();
    }

    [Theory]
    [InlineData(SectionBreakKind.NextPage, 2, false)]
    [InlineData(SectionBreakKind.Continuous, 2, false)]
    [InlineData(SectionBreakKind.EvenPage, 2, false)]
    [InlineData(SectionBreakKind.OddPage, 3, true)]
    public void BuildPhysicalPagePlan_ExpandsFirstSectionTransitionToRequestedParity(
        SectionBreakKind breakKind,
        int expectedPhysicalPages,
        bool expectsBlank)
    {
        var sections = new[]
        {
            new Section(new PageSettings { WidthPt = 500 }, breakKind),
            new Section(new PageSettings { WidthPt = 600 })
        };
        var bodyPages = new[]
        {
            PagePlan(sectionIndex: 0, sectionRelativePage: 1, sections[0]),
            PagePlan(sectionIndex: 1, sectionRelativePage: 1, sections[1])
        };

        var physical = HeaderFooterPagePlanner.BuildPhysicalPagePlan(bodyPages, sections);

        physical.Should().HaveCount(expectedPhysicalPages);
        physical.Count(page => page.IsParityBlank).Should().Be(expectsBlank ? 1 : 0);
        physical.Where(page => !page.IsParityBlank)
            .Select(page => page.BodyPageIndex)
            .Should().Equal(0, 1);
        physical.Where(page => page.PageSection.SectionIndex == 0)
            .Should().OnlyContain(page => page.PageSection.SectionPageCount == (expectsBlank ? 2 : 1));
        physical[^1].PageSection.SectionPageCount.Should().Be(1);
        if (expectsBlank)
        {
            physical[1].BodyPageIndex.Should().BeNull();
            physical[1].PageSection.SectionIndex.Should().Be(0);
            physical[1].PageSection.PageSettings.Should().BeSameAs(sections[0].Page);
            physical[2].PhysicalPageIndex.Should().Be(2);
        }
    }

    [Theory]
    [InlineData(SectionBreakKind.EvenPage, 4, true)]
    [InlineData(SectionBreakKind.OddPage, 3, false)]
    public void BuildPhysicalPagePlan_UsesActualCandidateParityAfterOverflowPages(
        SectionBreakKind breakKind,
        int expectedPhysicalPages,
        bool expectsBlank)
    {
        var sections = new[]
        {
            new Section(new PageSettings(), breakKind),
            new Section(new PageSettings())
        };
        var bodyPages = new[]
        {
            PagePlan(0, 1, sections[0]),
            PagePlan(0, 2, sections[0]),
            PagePlan(1, 1, sections[1])
        };

        var physical = HeaderFooterPagePlanner.BuildPhysicalPagePlan(bodyPages, sections);

        physical.Should().HaveCount(expectedPhysicalPages);
        physical.Count(page => page.IsParityBlank).Should().Be(expectsBlank ? 1 : 0);
        physical.Where(page => page.PageSection.SectionIndex == 0)
            .Should().OnlyContain(page => page.PageSection.SectionPageCount == (expectsBlank ? 3 : 2));
        physical[^1].PageSection.SectionPageCount.Should().Be(1);
        physical[^1].BodyPageIndex.Should().Be(2);
        physical[^1].PhysicalPageIndex.Should().Be(expectedPhysicalPages - 1);
    }

    [Fact]
    public void MapPagesToSections_UsesOwningSectionAndSectionRelativePageNumbers()
    {
        var document = new TextDocument();
        var firstSection = new Section(new PageSettings { HeaderDistancePt = 24 });
        firstSection.HeadersFooters.Header = new HeaderFooter("Section Header");
        document.Blocks.Add(new Paragraph("Section 1") { SectionBreak = firstSection });
        document.Blocks.Add(new Paragraph("Section 2"));
        document.FinalSectionHeadersFooters.Header = new HeaderFooter("Final Header");

        var pages = HeaderFooterPagePlanner.MapPagesToSections(
            document,
            blockPageAssignments: [0, 1],
            pageCount: 2);

        pages.Should().HaveCount(2);
        pages[0].SectionIndex.Should().Be(0);
        pages[0].HeadersFooters.Should().BeSameAs(firstSection.HeadersFooters);
        pages[0].PageSettings.Should().BeSameAs(firstSection.Page);
        pages[0].SectionRelativePageNumber.Should().Be(1);
        pages[0].SectionPageCount.Should().Be(1);
        pages[1].SectionIndex.Should().Be(1);
        pages[1].HeadersFooters.Should().BeSameAs(document.FinalSectionHeadersFooters);
        pages[1].PageSettings.Should().BeSameAs(document.Page);
        pages[1].SectionRelativePageNumber.Should().Be(1);
        pages[1].SectionPageCount.Should().Be(1);
    }

    [Fact]
    public void MapPagesToSections_EmptySectionHeadersFootersUseDocumentLevelStore()
    {
        var document = new TextDocument();
        var emptyFirstSection = new Section(new PageSettings());
        document.Blocks.Add(new Paragraph("Section 1") { SectionBreak = emptyFirstSection });
        document.Blocks.Add(new Paragraph("Section 2"));
        document.FinalSectionHeadersFooters.Header = new HeaderFooter("Document Header");

        var pages = HeaderFooterPagePlanner.MapPagesToSections(
            document,
            blockPageAssignments: [0, 2],
            pageCount: 3);

        pages[0].SectionIndex.Should().Be(0);
        pages[0].HeadersFooters.Should().BeSameAs(document.FinalSectionHeadersFooters);
        pages[1].SectionIndex.Should().Be(0);
        pages[1].HeadersFooters.Should().BeSameAs(document.FinalSectionHeadersFooters);
        pages[1].SectionRelativePageNumber.Should().Be(2);
        pages[0].SectionPageCount.Should().Be(2);
        pages[1].SectionPageCount.Should().Be(2);
        pages[2].SectionIndex.Should().Be(1);
        pages[2].HeadersFooters.Should().BeSameAs(document.FinalSectionHeadersFooters);
        pages[2].SectionPageCount.Should().Be(1);
    }

    [Fact]
    public void MapPagesToSections_LinkedSectionInheritsNearestPrecedingSectionHeaderNotFinalSection()
    {
        // Four sections: section 1 (no header), section 2 (defines a header), section 3 (link to
        // previous -- defines nothing of its own), section 4 / final (defines a DIFFERENT header).
        // Section 3 must inherit section 2's header (the nearest preceding definer), not section 4's.
        var document = new TextDocument();
        var section1 = new Section(new PageSettings());
        var section2 = new Section(new PageSettings());
        section2.HeadersFooters.Header = new HeaderFooter("Section 2 Header");
        var section3 = new Section(new PageSettings());

        document.Blocks.Add(new Paragraph("Section 1") { SectionBreak = section1 });
        document.Blocks.Add(new Paragraph("Section 2") { SectionBreak = section2 });
        document.Blocks.Add(new Paragraph("Section 3") { SectionBreak = section3 });
        document.Blocks.Add(new Paragraph("Section 4"));
        document.FinalSectionHeadersFooters.Header = new HeaderFooter("Final Header");

        var pages = HeaderFooterPagePlanner.MapPagesToSections(
            document,
            blockPageAssignments: [0, 1, 2, 3],
            pageCount: 4);

        pages[2].SectionIndex.Should().Be(2);
        pages[2].HeadersFooters.Header.Should().BeSameAs(section2.HeadersFooters.Header);
        pages[2].HeadersFooters.Header!.Should().NotBeSameAs(document.FinalSectionHeadersFooters.Header);
    }

    [Fact]
    public void MapPagesToSections_ResolvesEachHeaderFooterSlotTypeIndependently()
    {
        // Section 2 defines only its even-page header; its default header must still be inherited
        // from section 1 independently, rather than the whole section falling back as one unit.
        var document = new TextDocument();
        var section1 = new Section(new PageSettings());
        section1.HeadersFooters.Header = new HeaderFooter("Doc Default Header");

        document.Blocks.Add(new Paragraph("Section 1") { SectionBreak = section1 });
        document.Blocks.Add(new Paragraph("Section 2"));
        document.FinalSectionHeadersFooters.EvenHeader = new HeaderFooter("Section 2 Even Header");

        var pages = HeaderFooterPagePlanner.MapPagesToSections(
            document,
            blockPageAssignments: [0, 1],
            pageCount: 2);

        pages[1].SectionIndex.Should().Be(1);
        pages[1].HeadersFooters.Header.Should().BeSameAs(section1.HeadersFooters.Header);
        pages[1].HeadersFooters.EvenHeader.Should().BeSameAs(document.FinalSectionHeadersFooters.EvenHeader);
    }

    [Fact]
    public void MapPagesToSections_SectionDefiningOwnHeaderDoesNotInheritFromEarlierSection()
    {
        // Sibling/no-regression: a section that defines its OWN header must keep it, not inherit
        // from an earlier section just because an earlier section also happens to define one.
        var document = new TextDocument();
        var section1 = new Section(new PageSettings());
        var section2 = new Section(new PageSettings());
        section2.HeadersFooters.Header = new HeaderFooter("Section 2 Header");
        var section3 = new Section(new PageSettings());
        section3.HeadersFooters.Header = new HeaderFooter("Section 3 Own Header");

        document.Blocks.Add(new Paragraph("Section 1") { SectionBreak = section1 });
        document.Blocks.Add(new Paragraph("Section 2") { SectionBreak = section2 });
        document.Blocks.Add(new Paragraph("Section 3") { SectionBreak = section3 });
        document.Blocks.Add(new Paragraph("Section 4"));
        document.FinalSectionHeadersFooters.Header = new HeaderFooter("Final Header");

        var pages = HeaderFooterPagePlanner.MapPagesToSections(
            document,
            blockPageAssignments: [0, 1, 2, 3],
            pageCount: 4);

        pages[2].SectionIndex.Should().Be(2);
        pages[2].HeadersFooters.Header.Should().BeSameAs(section3.HeadersFooters.Header);
        pages[2].HeadersFooters.Header!.Should().NotBeSameAs(section2.HeadersFooters.Header);
    }

    private static HeaderFooterPageSectionPlan PagePlan(
        int sectionIndex,
        int sectionRelativePage,
        Section section) => new(
        sectionIndex,
        section.HeadersFooters,
        sectionRelativePage,
        section.Page);
}
