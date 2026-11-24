using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    /// <summary>
    /// The section + header/footer slot the Header &amp; Footer ribbon commands act on: the slot holding the
    /// in-place header/footer caret when there is one, otherwise the default header of the section
    /// containing the body caret. Link state comes from the shared <see cref="HeaderFooterSectionLinkPlanner"/>.
    /// </summary>
    public HeaderFooterSectionSlotView ActiveHeaderFooterSectionSlot
    {
        get
        {
            var sections = _doc.Sections;
            if (_hfCaret is { } caret)
            {
                var sectionIndex = caret.Target.DisplaySectionIndex >= 0
                    ? caret.Target.DisplaySectionIndex
                    : caret.Target.UseFinalSectionStore
                        || caret.Target.SectionIndex < 0
                        || caret.Target.SectionIndex >= sections.Count
                            ? sections.Count - 1
                            : caret.Target.SectionIndex;
                return HeaderFooterSectionLinkPlanner.Describe(sections, sectionIndex, caret.Target.Slot);
            }

            return HeaderFooterSectionLinkPlanner.Describe(
                sections,
                CurrentPageSettingsSectionIndex(),
                HeaderFooterSlotKind.Header);
        }
    }

    /// <summary>
    /// Toggles Link to Previous for <see cref="ActiveHeaderFooterSectionSlot"/>. Unlinking gives the
    /// section its own copy of the inherited header/footer; linking makes it follow the previous section
    /// again. Any in-place header/footer caret is released first because the paragraph it addressed may
    /// no longer belong to this section afterwards.
    /// </summary>
    public void ToggleHeaderFooterLinkToPrevious()
    {
        if (IsEditingLocked)
            return;

        var view = ActiveHeaderFooterSectionSlot;
        if (!view.CanLinkToPrevious)
            return;

        var wasEditing = IsHeaderFooterCaretActive;
        var next = view.IsLinkedToPrevious
            ? FreeW.Core.Model.DocumentModelCloner.CloneHeaderFooter(
                view.Content, FreeW.Core.Model.RevisionClonePolicy.Preserve) ?? new FreeW.Core.Model.HeaderFooter()
            : null;
        ExitHeaderFooterCaret();
        _bus.Execute(new ReplaceHeaderFooterSlotCommand(
            view.SectionIndex, view.Slot, next, "Link to Previous"));
        if (wasEditing)
            ActivateHeaderFooterSlot(view.Slot, view.SectionIndex);
    }
}
