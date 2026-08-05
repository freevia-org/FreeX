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
        pages[1].SectionIndex.Should().Be(1);
        pages[1].HeadersFooters.Should().BeSameAs(document.FinalSectionHeadersFooters);
        pages[1].PageSettings.Should().BeSameAs(document.Page);
        pages[1].SectionRelativePageNumber.Should().Be(1);
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
        pages[2].SectionIndex.Should().Be(1);
        pages[2].HeadersFooters.Should().BeSameAs(document.FinalSectionHeadersFooters);
    }
}
