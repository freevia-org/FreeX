using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record HeaderFooterPageSectionPlan(
    int SectionIndex,
    SectionHeadersFooters HeadersFooters,
    int SectionRelativePageNumber,
    PageSettings PageSettings,
    int SectionPageCount = 1);

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

        var sectionPageCounts = result
            .GroupBy(page => page.PageSection.SectionIndex)
            .ToDictionary(group => group.Key, group => group.Count());
        return result
            .Select(page => page with
            {
                PageSection = page.PageSection with
                {
                    SectionPageCount = sectionPageCounts[page.PageSection.SectionIndex]
                }
            })
            .ToList();
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
        var sectionPageCounts = new int[sections.Count];
        foreach (var sectionIndex in pageSectionIdx)
            sectionPageCounts[Math.Clamp(sectionIndex, 0, sections.Count - 1)]++;

        var effectiveHeadersFooters = new SectionHeadersFooters[sections.Count];
        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
            effectiveHeadersFooters[sectionIndex] =
                ResolveEffectiveHeadersFooters(sections, sectionIndex, finalSectionHeadersFooters);

        var result = new HeaderFooterPageSectionPlan[pageCount];
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var sectionIndex = Math.Clamp(pageSectionIdx[pageIndex], 0, sections.Count - 1);
            var section = sections[sectionIndex];
            var headersFooters = effectiveHeadersFooters[sectionIndex];
            var firstPage = sectionFirstPage[sectionIndex] >= 0
                ? sectionFirstPage[sectionIndex]
                : pageIndex;
            var sectionRelativePageNumber = pageIndex - firstPage + 1;

            result[pageIndex] = new HeaderFooterPageSectionPlan(
                sectionIndex,
                headersFooters,
                sectionRelativePageNumber,
                section.Page,
                sectionPageCounts[sectionIndex]);
        }

        return result;
    }

    /// <summary>
    /// Resolves the effective header/footer set for a section, honoring "link to previous"
    /// per slot type (default/even/first, header and footer independently) rather than treating a
    /// section's header/footer set as one all-or-nothing unit. When a section does not define a given
    /// slot itself, this walks BACKWARD through preceding sections (nearest first, including this one)
    /// looking for the closest one that defines it. If NO section from this one back to the start of
    /// the document defines the slot, there is no earlier definition to link to -- exactly like Word,
    /// which renders a blank header/footer for a leading section with no headerReference/footerReference
    /// of its own, rather than reaching past it into a LATER section (e.g. the document's final section)
    /// that happens to define one. A section can therefore define its own even-page header while still
    /// inheriting the default header from an earlier section.
    /// </summary>
    private static SectionHeadersFooters ResolveEffectiveHeadersFooters(
        IReadOnlyList<Section> sections,
        int sectionIndex,
        SectionHeadersFooters finalSectionHeadersFooters)
    {
        var header = ResolveSlot(sections, sectionIndex, hf => hf.Header);
        var footer = ResolveSlot(sections, sectionIndex, hf => hf.Footer);
        var evenHeader = ResolveSlot(sections, sectionIndex, hf => hf.EvenHeader);
        var evenFooter = ResolveSlot(sections, sectionIndex, hf => hf.EvenFooter);
        var firstHeader = ResolveSlot(sections, sectionIndex, hf => hf.FirstHeader);
        var firstFooter = ResolveSlot(sections, sectionIndex, hf => hf.FirstFooter);

        var own = sections[sectionIndex].HeadersFooters;
        if (MatchesResolvedSlots(own, header, footer, evenHeader, evenFooter, firstHeader, firstFooter))
            return own;
        if (!ReferenceEquals(own, finalSectionHeadersFooters)
            && MatchesResolvedSlots(finalSectionHeadersFooters, header, footer, evenHeader, evenFooter, firstHeader, firstFooter))
            return finalSectionHeadersFooters;

        return new SectionHeadersFooters
        {
            Header = header,
            Footer = footer,
            EvenHeader = evenHeader,
            EvenFooter = evenFooter,
            FirstHeader = firstHeader,
            FirstFooter = firstFooter
        };
    }

    private static HeaderFooter? ResolveSlot(
        IReadOnlyList<Section> sections,
        int sectionIndex,
        Func<SectionHeadersFooters, HeaderFooter?> selector)
    {
        for (var i = sectionIndex; i >= 0; i--)
        {
            var value = selector(sections[i].HeadersFooters);
            if (value is not null)
                return value;
        }

        // No section from sectionIndex back to the start of the document defines this slot -- there is
        // no earlier section to link to, so (matching Word) the slot is blank. Do NOT fall back to the
        // document's final section: that section is not "earlier" for a leading section and reaching
        // into it here would make an early section without its own header render a LATER section's
        // running header.
        return null;
    }

    /// <summary>
    /// Resolves the real, persisted <see cref="SectionHeadersFooters"/> instance that OWNS a given
    /// header/footer slot for <paramref name="sectionIndex"/> -- the nearest section, walking
    /// backward and including <paramref name="sectionIndex"/> itself, whose own
    /// <see cref="Section.HeadersFooters"/> defines that slot. This is the same per-slot walk-backward
    /// traversal as <see cref="ResolveSlot"/>'s loop, but returns the CONTAINING instance instead of
    /// the resolved <see cref="HeaderFooter"/> value, so a caller that needs to COMMIT an edit (rather
    /// than just display inherited content) knows which real, retained model object to write into.
    ///
    /// <para>
    /// Committing to the section being <em>viewed</em> instead of this real owner would silently
    /// create a brand-new local definition on a section that was only ever inheriting the slot,
    /// breaking the "link to previous" the instant the inherited content is edited -- Word instead
    /// writes the edit into the same header/footer part the linked section is displaying, preserving
    /// the link until the user explicitly unlinks it.
    /// </para>
    ///
    /// <para>
    /// <strong>Fallback differs from <see cref="ResolveSlot"/>'s:</strong> when NO section from
    /// <paramref name="sectionIndex"/> back to the start defines the slot, there is no existing link to
    /// preserve, so this returns <paramref name="sectionIndex"/>'s own instance (a brand-new definition
    /// belongs on the section actually being edited) rather than <see cref="ResolveSlot"/>'s
    /// document-level final-section fallback. Reusing that display-path fallback here would make some
    /// other, unrelated, unedited page's routine commit spuriously plant an empty header/footer on the
    /// whole document's final section merely because nothing anywhere defined the slot yet.
    /// </para>
    /// </summary>
    public static SectionHeadersFooters ResolveSlotOwner(
        IReadOnlyList<Section> sections,
        int sectionIndex,
        HeaderFooterSlotKind slot)
    {
        ArgumentNullException.ThrowIfNull(sections);
        if (sections.Count == 0)
            throw new ArgumentException("At least one section is required.", nameof(sections));

        var clampedIndex = Math.Clamp(sectionIndex, 0, sections.Count - 1);
        for (var i = clampedIndex; i >= 0; i--)
        {
            if (HeaderFooterDialogPlanner.GetSlot(sections[i].HeadersFooters, slot) is not null)
                return sections[i].HeadersFooters;
        }

        return sections[clampedIndex].HeadersFooters;
    }

    private static bool MatchesResolvedSlots(
        SectionHeadersFooters candidate,
        HeaderFooter? header,
        HeaderFooter? footer,
        HeaderFooter? evenHeader,
        HeaderFooter? evenFooter,
        HeaderFooter? firstHeader,
        HeaderFooter? firstFooter) =>
        ReferenceEquals(candidate.Header, header)
        && ReferenceEquals(candidate.Footer, footer)
        && ReferenceEquals(candidate.EvenHeader, evenHeader)
        && ReferenceEquals(candidate.EvenFooter, evenFooter)
        && ReferenceEquals(candidate.FirstHeader, firstHeader)
        && ReferenceEquals(candidate.FirstFooter, firstFooter);

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

    /// <summary>
    /// Maps each block index to the (0-based) index of the section it belongs to, by walking the
    /// blocks in order and advancing past each <see cref="Paragraph.SectionBreak"/> marker. Public so
    /// callers that need to resolve a section from a caret/selection block index (e.g. Layout ribbon
    /// and Page Setup dialog commands, which must target the section containing the caret rather than
    /// always the document's final section) can reuse this same walk instead of re-deriving it.
    /// </summary>
    public static int[] MapBlocksToSections(IReadOnlyList<Block> blocks, int sectionCount)
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
