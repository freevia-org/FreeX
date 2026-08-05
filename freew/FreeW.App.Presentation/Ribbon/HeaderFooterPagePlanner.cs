using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record HeaderFooterPageSectionPlan(
    int SectionIndex,
    SectionHeadersFooters HeadersFooters,
    int SectionRelativePageNumber,
    PageSettings PageSettings);

public sealed record HeaderFooterSlotPlan(
    HeaderFooter? Header,
    HeaderFooterSlotKind HeaderSlot,
    string HeaderSlotName,
    HeaderFooter? Footer,
    HeaderFooterSlotKind FooterSlot,
    string FooterSlotName);

public sealed record SectionPhysicalPagePlan(
    int PhysicalPageIndex,
    int? BodyPageIndex,
    HeaderFooterPageSectionPlan PageSection,
    bool IsParityBlank);

public static class HeaderFooterPagePlanner
{
    public const int UnassignedBlockPageIndex = -1;

    public static bool UsesDifferentOddEvenPages(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Page.DifferentOddEvenPages
            || document.Blocks.OfType<Paragraph>().Any(p => p.SectionBreak?.Page.DifferentOddEvenPages == true);
    }

    public static IReadOnlyList<SectionPhysicalPagePlan> BuildPhysicalPagePlan(
        IReadOnlyList<HeaderFooterPageSectionPlan> bodyPages,
        IReadOnlyList<Section> sections)
    {
        ArgumentNullException.ThrowIfNull(bodyPages);
        ArgumentNullException.ThrowIfNull(sections);

        if (bodyPages.Count == 0)
            return Array.Empty<SectionPhysicalPagePlan>();
        if (sections.Count == 0)
            throw new ArgumentException("At least one section is required.", nameof(sections));

        var result = new List<SectionPhysicalPagePlan>(bodyPages.Count + sections.Count - 1);
        for (var bodyPageIndex = 0; bodyPageIndex < bodyPages.Count; bodyPageIndex++)
        {
            var current = bodyPages[bodyPageIndex];
            if (bodyPageIndex > 0)
            {
                var previous = bodyPages[bodyPageIndex - 1];
                if (current.SectionIndex != previous.SectionIndex)
                {
                    var boundarySectionIndex = Math.Clamp(current.SectionIndex - 1, 0, sections.Count - 1);
                    var breakKind = sections[boundarySectionIndex].BreakKind;
                    var candidatePhysicalPageNumber = result.Count + 1;
                    if (RequiresParityBlank(candidatePhysicalPageNumber, breakKind))
                    {
                        var blankSection = previous with
                        {
                            SectionRelativePageNumber = previous.SectionRelativePageNumber + 1
                        };
                        result.Add(new SectionPhysicalPagePlan(
                            result.Count,
                            BodyPageIndex: null,
                            blankSection,
                            IsParityBlank: true));
                    }
                }
            }

            result.Add(new SectionPhysicalPagePlan(
                result.Count,
                bodyPageIndex,
                current,
                IsParityBlank: false));
        }

        return result;
    }

    private static bool RequiresParityBlank(int candidatePhysicalPageNumber, SectionBreakKind breakKind) =>
        breakKind switch
        {
            SectionBreakKind.EvenPage => candidatePhysicalPageNumber % 2 != 0,
            SectionBreakKind.OddPage => candidatePhysicalPageNumber % 2 == 0,
            _ => false
        };

    public static HeaderFooterSlotPlan ResolveSlots(
        SectionHeadersFooters headersFooters,
        int sectionRelativePageNumber,
        PageSettings pageSettings,
        bool differentOddEvenPages,
        int? logicalPageNumber = null)
    {
        ArgumentNullException.ThrowIfNull(headersFooters);
        ArgumentNullException.ThrowIfNull(pageSettings);

        if (sectionRelativePageNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(sectionRelativePageNumber), sectionRelativePageNumber,
                "Section-relative page numbers are 1-based.");

        if (pageSettings.DifferentFirstPage && sectionRelativePageNumber == 1)
            return BuildPlan(headersFooters, HeaderFooterSlotKind.FirstHeader, HeaderFooterSlotKind.FirstFooter);

        var parityPageNumber = logicalPageNumber ?? sectionRelativePageNumber;
        if (differentOddEvenPages && parityPageNumber % 2 == 0)
            return BuildPlan(headersFooters, HeaderFooterSlotKind.EvenHeader, HeaderFooterSlotKind.EvenFooter);

        return BuildPlan(headersFooters, HeaderFooterSlotKind.Header, HeaderFooterSlotKind.Footer);
    }

    public static IReadOnlyList<HeaderFooterPageSectionPlan> MapPagesToSections(
        TextDocument document,
        IReadOnlyList<int> blockPageAssignments,
        int pageCount)
    {
        ArgumentNullException.ThrowIfNull(document);

        return MapPagesToSections(
            document.Blocks,
            blockPageAssignments,
            pageCount,
            document.Sections,
            document.FinalSectionHeadersFooters);
    }

    public static IReadOnlyList<HeaderFooterPageSectionPlan> MapPagesToSections(
        IReadOnlyList<Block> blocks,
        IReadOnlyList<int> blockPageAssignments,
        int pageCount,
        IReadOnlyList<Section> sections,
        SectionHeadersFooters finalSectionHeadersFooters)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(blockPageAssignments);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(finalSectionHeadersFooters);

        if (pageCount < 0)
            throw new ArgumentOutOfRangeException(nameof(pageCount), pageCount, "Page count cannot be negative.");
        if (pageCount == 0)
            return Array.Empty<HeaderFooterPageSectionPlan>();
        if (sections.Count == 0)
            throw new ArgumentException("At least one section is required.", nameof(sections));

        var blockSection = MapBlocksToSections(blocks, sections.Count);
        var pageSectionIdx = MapPagesToSectionIndices(blockSection, blockPageAssignments, pageCount);
        var sectionFirstPage = MapSectionFirstPages(pageSectionIdx, sections.Count);

        var result = new HeaderFooterPageSectionPlan[pageCount];
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var sectionIndex = Math.Clamp(pageSectionIdx[pageIndex], 0, sections.Count - 1);
            var section = sections[sectionIndex];
            var headersFooters = section.HeadersFooters.IsEmpty
                ? finalSectionHeadersFooters
                : section.HeadersFooters;
            var firstPage = sectionFirstPage[sectionIndex] >= 0
                ? sectionFirstPage[sectionIndex]
                : pageIndex;
            var sectionRelativePageNumber = pageIndex - firstPage + 1;

            result[pageIndex] = new HeaderFooterPageSectionPlan(
                sectionIndex,
                headersFooters,
                sectionRelativePageNumber,
                section.Page);
        }

        return result;
    }

    private static HeaderFooterSlotPlan BuildPlan(
        SectionHeadersFooters headersFooters,
        HeaderFooterSlotKind headerSlot,
        HeaderFooterSlotKind footerSlot)
    {
        return new HeaderFooterSlotPlan(
            HeaderFooterDialogPlanner.GetSlot(headersFooters, headerSlot),
            headerSlot,
            HeaderFooterDialogPlanner.SlotNameFor(headerSlot),
            HeaderFooterDialogPlanner.GetSlot(headersFooters, footerSlot),
            footerSlot,
            HeaderFooterDialogPlanner.SlotNameFor(footerSlot));
    }

    private static int[] MapBlocksToSections(IReadOnlyList<Block> blocks, int sectionCount)
    {
        var blockSection = new int[blocks.Count];
        var sectionIndex = 0;

        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            blockSection[blockIndex] = sectionIndex;
            if (blocks[blockIndex] is Paragraph { SectionBreak: { } }
                && sectionIndex < sectionCount - 1)
            {
                sectionIndex++;
            }
        }

        return blockSection;
    }

    private static int[] MapPagesToSectionIndices(
        IReadOnlyList<int> blockSection,
        IReadOnlyList<int> blockPageAssignments,
        int pageCount)
    {
        var pageSectionIdx = new int[pageCount];
        var pageAssigned = new bool[pageCount];

        for (var blockIndex = 0; blockIndex < blockSection.Count; blockIndex++)
        {
            var pageIndex = blockIndex < blockPageAssignments.Count
                ? blockPageAssignments[blockIndex]
                : UnassignedBlockPageIndex;

            if (pageIndex < 0)
                continue;

            pageIndex = Math.Clamp(pageIndex, 0, pageCount - 1);
            if (pageAssigned[pageIndex])
                continue;

            pageSectionIdx[pageIndex] = blockSection[blockIndex];
            pageAssigned[pageIndex] = true;
        }

        if (!pageAssigned[0])
            pageAssigned[0] = true;

        for (var pageIndex = 1; pageIndex < pageCount; pageIndex++)
        {
            if (pageAssigned[pageIndex])
                continue;

            pageSectionIdx[pageIndex] = pageSectionIdx[pageIndex - 1];
            pageAssigned[pageIndex] = true;
        }

        return pageSectionIdx;
    }

    private static int[] MapSectionFirstPages(IReadOnlyList<int> pageSectionIdx, int sectionCount)
    {
        var sectionFirstPage = Enumerable.Repeat(-1, sectionCount).ToArray();
        for (var pageIndex = 0; pageIndex < pageSectionIdx.Count; pageIndex++)
        {
            var sectionIndex = Math.Clamp(pageSectionIdx[pageIndex], 0, sectionCount - 1);
            if (sectionFirstPage[sectionIndex] < 0
                || pageIndex == 0
                || pageSectionIdx[pageIndex - 1] != sectionIndex)
            {
                sectionFirstPage[sectionIndex] = pageIndex;
            }
        }

        return sectionFirstPage;
    }
}
