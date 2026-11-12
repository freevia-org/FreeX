using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>What a header/footer editing surface shows for one slot of one section.</summary>
/// <param name="SectionIndex">0-based index of the section being edited.</param>
/// <param name="SectionCount">Number of sections in the document.</param>
/// <param name="Slot">The header/footer slot being edited.</param>
/// <param name="Content">The content the section displays for the slot (its own, or the inherited one).</param>
/// <param name="IsLinkedToPrevious">True when the section inherits the slot from an earlier section.</param>
/// <param name="CanLinkToPrevious">False for the first section, which has nothing to link to.</param>
public sealed record HeaderFooterSectionSlotView(
    int SectionIndex,
    int SectionCount,
    HeaderFooterSlotKind Slot,
    HeaderFooter? Content,
    bool IsLinkedToPrevious,
    bool CanLinkToPrevious)
{
    public bool HasPreviousSection => SectionIndex > 0;
    public bool HasNextSection => SectionIndex < SectionCount - 1;
}

/// <summary>
/// Word's per-section header/footer linkage ("Link to Previous") over the FreeW model, where a section's
/// null slot means exactly what an absent <c>w:headerReference</c>/<c>w:footerReference</c> of that type
/// means in its <c>w:sectPr</c>: the section shows the nearest earlier section's header/footer of that type.
/// Renderers own only the editing surface; link state, navigation targets and the copy-on-unlink policy
/// live here so both shells behave identically.
/// </summary>
public static class HeaderFooterSectionLinkPlanner
{
    /// <summary>Describes the slot of <paramref name="sectionIndex"/> for an editing surface.</summary>
    public static HeaderFooterSectionSlotView Describe(
        IReadOnlyList<Section> sections,
        int sectionIndex,
        HeaderFooterSlotKind slot)
    {
        var index = Clamp(sections, sectionIndex);
        var owner = HeaderFooterPagePlanner.ResolveSlotOwner(sections, index, slot);
        return new HeaderFooterSectionSlotView(
            index,
            sections.Count,
            slot,
            HeaderFooterDialogPlanner.GetSlot(owner, slot),
            IsLinkedToPrevious(sections, index, slot),
            CanLinkToPrevious(sections, index));
    }

    /// <summary>The first section has no previous section, so its Link to Previous is unavailable.</summary>
    public static bool CanLinkToPrevious(IReadOnlyList<Section> sections, int sectionIndex) =>
        Clamp(sections, sectionIndex) > 0;

    /// <summary>
    /// True when the section does not define the slot itself and therefore shows the previous section's.
    /// Always false for the first section (an undefined slot there is simply blank).
    /// </summary>
    public static bool IsLinkedToPrevious(
        IReadOnlyList<Section> sections,
        int sectionIndex,
        HeaderFooterSlotKind slot)
    {
        var index = Clamp(sections, sectionIndex);
        return index > 0 && HeaderFooterDialogPlanner.GetSlot(sections[index].HeadersFooters, slot) is null;
    }

    /// <summary>
    /// Toggles Link to Previous for one slot. Unlinking gives the section its own deep copy of the content
    /// it was inheriting, so it can diverge without touching the earlier section; linking discards the
    /// section's own definition so it follows the previous section again. Returns false when nothing
    /// changed (first section).
    /// </summary>
    public static bool ToggleLinkToPrevious(
        IReadOnlyList<Section> sections,
        int sectionIndex,
        HeaderFooterSlotKind slot) =>
        SetLinkedToPrevious(sections, sectionIndex, slot, !IsLinkedToPrevious(sections, sectionIndex, slot));

    public static bool SetLinkedToPrevious(
        IReadOnlyList<Section> sections,
        int sectionIndex,
        HeaderFooterSlotKind slot,
        bool linked)
    {
        var index = Clamp(sections, sectionIndex);
        if (index == 0 || IsLinkedToPrevious(sections, index, slot) == linked)
            return false;

        var own = sections[index].HeadersFooters;
        if (linked)
        {
            HeaderFooterDialogPlanner.SetSlot(own, slot, null);
            return true;
        }

        var inherited = HeaderFooterDialogPlanner.GetSlot(
            HeaderFooterPagePlanner.ResolveSlotOwner(sections, index - 1, slot),
            slot);
        // An unlinked slot must be non-null even when there is nothing to copy: an own, empty
        // header/footer is what distinguishes "unlinked and blank" from "linked".
        HeaderFooterDialogPlanner.SetSlot(
            own,
            slot,
            DocumentModelCloner.CloneHeaderFooter(inherited, RevisionClonePolicy.Preserve) ?? new HeaderFooter());
        return true;
    }

    /// <summary>
    /// Writes edited content for the slot shown in <paramref name="sectionIndex"/>. Like Word, editing a
    /// linked header/footer edits the shared one (the owning earlier section's), keeping the link; only
    /// an explicit unlink makes the section diverge. Returns the section index that received the content,
    /// or -1 when there was nothing to store (blank content for a slot nobody defines).
    /// </summary>
    public static int CommitSlotContent(
        IReadOnlyList<Section> sections,
        int sectionIndex,
        HeaderFooterSlotKind slot,
        HeaderFooter content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var index = Clamp(sections, sectionIndex);
        var ownerIndex = ResolveOwnerIndex(sections, index, slot);
        if (ownerIndex < 0)
        {
            if (content.IsEmpty)
                return -1;
            // Nobody defines the slot yet. The content belongs to the start of the link chain, which is
            // what every linked section up to and including this one displays.
            ownerIndex = FirstSectionOfLinkChain(sections, index, slot);
        }

        HeaderFooterDialogPlanner.SetSlot(sections[ownerIndex].HeadersFooters, slot, content);
        return ownerIndex;
    }

    /// <summary>The section a Previous/Next navigation lands on, or null at either end of the document.</summary>
    public static int? PlanAdjacentSection(IReadOnlyList<Section> sections, int sectionIndex, int direction)
    {
        var target = Clamp(sections, sectionIndex) + Math.Sign(direction);
        return target < 0 || target >= sections.Count || direction == 0 ? null : target;
    }

    /// <summary>Go to Header / Go to Footer: the same page type (default/even/first) on the other edge.</summary>
    public static HeaderFooterSlotKind CounterpartSlot(HeaderFooterSlotKind slot, bool header) => slot switch
    {
        HeaderFooterSlotKind.EvenHeader or HeaderFooterSlotKind.EvenFooter =>
            header ? HeaderFooterSlotKind.EvenHeader : HeaderFooterSlotKind.EvenFooter,
        HeaderFooterSlotKind.FirstHeader or HeaderFooterSlotKind.FirstFooter =>
            header ? HeaderFooterSlotKind.FirstHeader : HeaderFooterSlotKind.FirstFooter,
        _ => header ? HeaderFooterSlotKind.Header : HeaderFooterSlotKind.Footer,
    };

    public static bool IsHeader(HeaderFooterSlotKind slot) =>
        slot is HeaderFooterSlotKind.Header or HeaderFooterSlotKind.EvenHeader or HeaderFooterSlotKind.FirstHeader;

    /// <summary>
    /// The slot to show when moving to another section: the same slot when that section's page setup
    /// displays it, otherwise that section's default header/footer on the same edge.
    /// </summary>
    public static HeaderFooterSlotKind ResolveSlotForSection(Section section, HeaderFooterSlotKind slot)
    {
        ArgumentNullException.ThrowIfNull(section);
        return HeaderFooterDialogPlanner.PlanSlotActivation(slot, section.Page).Kind
            == HeaderFooterSlotActivationKind.Active
            ? slot
            : IsHeader(slot) ? HeaderFooterSlotKind.Header : HeaderFooterSlotKind.Footer;
    }

    /// <summary>
    /// Word's header/footer tab text: the slot label, the 1-based section number when the document has
    /// more than one section, and the "Same as Previous" indicator for a linked slot.
    /// </summary>
    public static string BuildEditingLabel(HeaderFooterSectionSlotView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var label = HeaderFooterDialogPlanner.LabelFor(view.Slot);
        if (view.SectionCount > 1)
            label = FreeWUiTextCatalog.HeaderFooterSectionLabel(label, view.SectionIndex + 1);
        return view.IsLinkedToPrevious
            ? FreeWUiTextCatalog.HeaderFooterSameAsPreviousLabel(label)
            : label;
    }

    private static int ResolveOwnerIndex(IReadOnlyList<Section> sections, int index, HeaderFooterSlotKind slot)
    {
        for (var i = index; i >= 0; i--)
        {
            if (HeaderFooterDialogPlanner.GetSlot(sections[i].HeadersFooters, slot) is not null)
                return i;
        }

        return -1;
    }

    private static int FirstSectionOfLinkChain(IReadOnlyList<Section> sections, int index, HeaderFooterSlotKind slot)
    {
        var first = index;
        while (first > 0 && HeaderFooterDialogPlanner.GetSlot(sections[first].HeadersFooters, slot) is null)
            first--;
        return first;
    }

    private static int Clamp(IReadOnlyList<Section> sections, int sectionIndex)
    {
        ArgumentNullException.ThrowIfNull(sections);
        if (sections.Count == 0)
            throw new ArgumentException("At least one section is required.", nameof(sections));
        return Math.Clamp(sectionIndex, 0, sections.Count - 1);
    }
}
