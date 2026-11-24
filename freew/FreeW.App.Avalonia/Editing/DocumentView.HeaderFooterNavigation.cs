using FreeW.App.Presentation.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    private (int SectionIndex, HeaderFooterSlotKind Slot)? _emptyHeaderFooterKey;
    private SectionHeadersFooters? _emptyHeaderFooterStore;
    private HeaderFooter? _emptyHeaderFooterSource;

    /// <summary>Moves the active story to the neighbouring section without unlinking inherited content.</summary>
    public void MoveHeaderFooterSection(int direction)
    {
        var current = ActiveHeaderFooterSectionSlot;
        if (HeaderFooterSectionLinkPlanner.PlanAdjacentSection(_doc.Sections, current.SectionIndex, direction)
            is not { } adjacent)
            return;

        var slot = HeaderFooterSectionLinkPlanner.ResolveSlotForSection(_doc.Sections[adjacent], current.Slot);
        ActivateHeaderFooterSlot(slot, adjacent);
    }

    /// <summary>Go to Header/Footer preserves first-page or even-page variants on the same section.</summary>
    public void GoToHeaderFooterSlot(HeaderFooterSlotKind slot)
    {
        var target = _hfCaret is not null
            ? HeaderFooterSectionLinkPlanner.CounterpartSlot(
                ActiveHeaderFooterSectionSlot.Slot, HeaderFooterSectionLinkPlanner.IsHeader(slot))
            : slot;
        ActivateHeaderFooterSlot(target, ActiveHeaderFooterSectionSlot.SectionIndex);
    }

    private void ActivateHeaderFooterSlot(HeaderFooterSlotKind slot, int sectionIndex)
    {
        sectionIndex = Math.Clamp(sectionIndex, 0, _doc.Sections.Count - 1);
        if (HeaderFooterDialogPlanner.PlanSlotActivation(slot, _doc.Sections[sectionIndex].Page).Kind
            != HeaderFooterSlotActivationKind.Active)
            return;

        var ownerIndex = ResolveHeaderFooterOwnerIndex(sectionIndex, slot);
        var store = _doc.Sections[ownerIndex].HeadersFooters;

        if (PageSettingsSectionResolver.ResolveSectionIndex(_doc, _caret.Block) != sectionIndex)
        {
            for (var block = 0; block < _doc.Blocks.Count; block++)
            {
                if (PageSettingsSectionResolver.ResolveSectionIndex(_doc, block) != sectionIndex)
                    continue;
                _caret = new DocPosition(block, 0);
                break;
            }
        }

        var target = MakeHfTarget(store, slot, 0) with { DisplaySectionIndex = sectionIndex };
        PlaceCaretInHeaderFooter(target, 0);
        Relayout(_laidOutWidth > 0 ? _laidOutWidth : FallbackWidth);
        InvalidateVisual();
        Focus();
    }

    private int ResolveHeaderFooterOwnerIndex(int sectionIndex, HeaderFooterSlotKind slot)
    {
        for (var owner = sectionIndex; owner >= 0; owner--)
        {
            if (HeaderFooterDialogPlanner.GetSlot(_doc.Sections[owner].HeadersFooters, slot) is not null)
                return owner;
        }
        return 0;
    }

    private void RebindHeaderFooterCaretOwner()
    {
        if (_hfCaret is not { } caret || caret.Target.DisplaySectionIndex < 0)
            return;
        var display = Math.Clamp(caret.Target.DisplaySectionIndex, 0, _doc.Sections.Count - 1);
        var owner = ResolveHeaderFooterOwnerIndex(display, caret.Target.Slot);
        var store = _doc.Sections[owner].HeadersFooters;
        var target = MakeHfTarget(store, caret.Target.Slot, caret.Target.ParaIdx) with { DisplaySectionIndex = display };
        var story = HeaderFooterDialogPlanner.GetSlot(ResolveHfStore(target)!, caret.Target.Slot)!;
        var paragraph = Math.Clamp(caret.Target.ParaIdx, 0, story.Paragraphs.Count - 1);
        target = target with { ParaIdx = paragraph };
        if (!SameHfStory(target, caret.Target))
            _hfSelectionAnchor = null;
        _hfCaret = (target, Math.Clamp(caret.Offset, 0, HfModelPlainText(story.Paragraphs[paragraph]).Length));
    }

    private SectionHeadersFooters ResolveEmptyHeaderFooterStore(HfTarget target, SectionHeadersFooters actual)
    {
        if (HeaderFooterDialogPlanner.GetSlot(actual, target.Slot) is { Paragraphs.Count: > 0 })
            return actual;
        var section = target.UseFinalSectionStore ? _doc.Sections.Count - 1 : target.SectionIndex;
        var key = (section, target.Slot);
        var current = HeaderFooterDialogPlanner.GetSlot(actual, target.Slot);
        if (_emptyHeaderFooterKey != key || _emptyHeaderFooterStore is null
            || !ReferenceEquals(_emptyHeaderFooterSource, current))
        {
            _emptyHeaderFooterKey = key;
            _emptyHeaderFooterSource = current;
            _emptyHeaderFooterStore = new SectionHeadersFooters();
            var placeholder = DocumentModelCloner.CloneHeaderFooter(
                HeaderFooterDialogPlanner.GetSlot(actual, target.Slot), RevisionClonePolicy.Preserve) ?? new HeaderFooter();
            placeholder.Paragraphs.Add(new Paragraph());
            HeaderFooterDialogPlanner.SetSlot(_emptyHeaderFooterStore, target.Slot, placeholder);
        }
        return _emptyHeaderFooterStore;
    }

    private bool IsEmptyHeaderFooterPlaceholder(HfTarget target)
    {
        var section = target.UseFinalSectionStore ? _doc.Sections.Count - 1 : target.SectionIndex;
        return HeaderFooterDialogPlanner.GetSlot(_doc.Sections[section].HeadersFooters, target.Slot)
            is not { Paragraphs.Count: > 0 };
    }

    private void MaterializeEmptyHeaderFooterStory(HfTarget target)
    {
        if (!IsEmptyHeaderFooterPlaceholder(target))
            return;
        var section = target.UseFinalSectionStore ? _doc.Sections.Count - 1 : target.SectionIndex;
        var placeholder = HeaderFooterDialogPlanner.GetSlot(ResolveHfStore(target)!, target.Slot);
        _bus.Execute(new ReplaceHeaderFooterSlotCommand(section, target.Slot,
            DocumentModelCloner.CloneHeaderFooter(placeholder, RevisionClonePolicy.Preserve)));
    }

    private HfTarget HeaderFooterRenderTarget(int sectionIndex, HeaderFooterSlotKind slot, int paragraphIndex) =>
        MakeHfTarget(_doc.Sections[ResolveHeaderFooterOwnerIndex(sectionIndex, slot)].HeadersFooters, slot, paragraphIndex)
            with { DisplaySectionIndex = sectionIndex };

    private bool IsActiveHeaderFooterSlot(int sectionIndex, HeaderFooterSlotKind slot) =>
        _hfCaret is { } caret && caret.Target.Slot == slot
        && (caret.Target.DisplaySectionIndex >= 0 ? caret.Target.DisplaySectionIndex
            : caret.Target.UseFinalSectionStore ? _doc.Sections.Count - 1 : caret.Target.SectionIndex) == sectionIndex;

    private HeaderFooter? ActiveEmptyHeaderFooterStory(int sectionIndex, HeaderFooterSlotKind slot) =>
        IsActiveHeaderFooterSlot(sectionIndex, slot) && _hfCaret is { } caret
            ? HeaderFooterDialogPlanner.GetSlot(ResolveHfStore(caret.Target)!, slot)
            : null;

    private void InsertActiveHeaderFooterField(string instruction)
    {
        if (IsEditingLocked)
            return;
        if (_hfCaret is null)
            ActivateHeaderFooterSlot(HeaderFooterSlotKind.Header, CurrentPageSettingsSectionIndex());
        InsertComplexField(instruction);
        Focus();
    }

    private void InsertSectionHeaderFooterPageNumber(bool footer)
    {
        if (IsEditingLocked)
            return;
        var current = ActiveHeaderFooterSectionSlot;
        var slot = _hfCaret is not null
            ? HeaderFooterSectionLinkPlanner.CounterpartSlot(current.Slot, header: !footer)
            : footer ? HeaderFooterSlotKind.Footer : HeaderFooterSlotKind.Header;
        var owner = ResolveHeaderFooterOwnerIndex(current.SectionIndex, slot);
        var previous = HeaderFooterDialogPlanner.GetSlot(_doc.Sections[owner].HeadersFooters, slot);
        if (HeaderFooterDialogPlanner.HasPageNumber(previous))
            return;
        var next = HeaderFooterDialogPlanner.AddPageNumberToSlot(
            DocumentModelCloner.CloneHeaderFooter(previous, RevisionClonePolicy.Preserve));
        _bus.Execute(new ReplaceHeaderFooterSlotCommand(owner, slot, next, "Insert Page Number"));
        Focus();
    }

    private bool TryHandleHeaderFooterNavigation(DocumentTextNavigationIntent intent, bool extend)
    {
        if (_hfCaret is not { } caret
            || ResolveHfStore(caret.Target) is not { } store
            || HeaderFooterDialogPlanner.GetSlot(store, caret.Target.Slot) is not { } story)
            return false;

        if (!extend && NormalizedHfSelection() is { } selection
            && intent is DocumentTextNavigationIntent.PreviousCharacter or DocumentTextNavigationIntent.NextCharacter)
        {
            var edge = intent == DocumentTextNavigationIntent.PreviousCharacter ? selection.Start : selection.End;
            PlaceCaretInHeaderFooter(caret.Target with { ParaIdx = edge.ParagraphIndex }, edge.Offset);
            return true;
        }

        var next = DocumentTextNavigationPlanner.Move(
            story.Paragraphs.Select(HfModelPlainText).ToArray(),
            new DocumentTextNavigationPosition(caret.Target.ParaIdx, caret.Offset),
            intent);
        if (intent is DocumentTextNavigationIntent.LineStart or DocumentTextNavigationIntent.LineEnd)
        {
            if (_laidOutWidth < 0)
                Relayout(FallbackWidth);
            bool IsCurrentParagraph(HfRenderItem item) => item.Target is { } target
                && SameHfStory(target, caret.Target) && target.ParaIdx == caret.Target.ParaIdx
                && (caret.Target.DisplaySectionIndex < 0
                    || target.DisplaySectionIndex == caret.Target.DisplaySectionIndex);
            var line = _headerFooterItems.FirstOrDefault(item => IsCurrentParagraph(item)
                && caret.Offset >= item.ModelStartOffset
                && caret.Offset <= item.ModelStartOffset + item.Text.Length);
            if (line is not null)
            {
                var segments = _headerFooterItems.Where(item => IsCurrentParagraph(item)
                    && Math.Abs(item.Y - line.Y) < 0.5);
                next = next with
                {
                    Offset = intent == DocumentTextNavigationIntent.LineStart
                        ? segments.Min(item => item.ModelStartOffset)
                        : segments.Max(item => item.ModelStartOffset + item.Text.Length),
                };
            }
        }

        PlaceCaretInHeaderFooter(caret.Target with { ParaIdx = next.ParagraphIndex }, next.Offset, extend);
        return true;
    }
}
