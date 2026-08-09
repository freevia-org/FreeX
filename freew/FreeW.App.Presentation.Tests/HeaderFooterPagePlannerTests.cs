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

    private static HeaderFooterPageSectionPlan PagePlan(
        int sectionIndex,
        int sectionRelativePage,
        Section section) => new(
        sectionIndex,
        section.HeadersFooters,
        sectionRelativePage,
        section.Page);
}
