using System.Windows;
using FreeW.App.Localization;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Per-section header/footer editing for the docked Header/Footer pane: the pane targets the section
/// containing the caret, Previous/Next walk the sections, and Link to Previous toggles whether the
/// section inherits the previous section's header/footer. Linkage policy lives in the shared
/// <see cref="HeaderFooterSectionLinkPlanner"/>; this file only loads/commits the WPF sub-editor.
/// </summary>
public sealed partial class MainWindow
{
    private int _hfActiveSectionIndex;
    private bool _hfEditPending;
    private bool _hfEditorLoading;

    // Called once from BuildHeaderFooterPane: only content the user actually changed is written back, so
    // merely opening or walking through headers never rewrites (or re-owns) a section's header/footer.
    private void InitializeHeaderFooterPaneEditing()
    {
        _hfSubEditor.TextChanged += (_, _) =>
        {
            if (!_hfEditorLoading)
                _hfEditPending = true;
        };
    }

    /// <summary>
    /// Loads one section's slot into the pane. A linked slot shows the inherited content and is labelled
    /// "Same as Previous"; typing into it edits the shared header/footer, exactly as in Word.
    /// </summary>
    private void ShowHeaderFooterSlot(int sectionIndex, HeaderFooterSlotKind slot)
    {
        _editor.CommitToModel();
        var view = HeaderFooterSectionLinkPlanner.Describe(_editor.Model.Sections, sectionIndex, slot);
        _hfActiveSlot = slot;
        _hfActiveSectionIndex = view.SectionIndex;
        _hfSlotLabel.Text = UiText.Format(
            "HeaderFooter_Editing_Status_Format",
            HeaderFooterSectionLinkPlanner.BuildEditingLabel(view));

        // Wrapper document — seeded with the main doc's DefaultRun so fonts match. Paragraphs are cloned
        // so unapplied edits never leak into a header/footer other sections may be sharing.
        var wrapper = TextDocument.CreateEmpty();
        wrapper.DefaultRun = _editor.Model.DefaultRun;
        wrapper.DefaultParagraph = _editor.Model.DefaultParagraph;
        wrapper.Blocks.Clear();
        if (view.Content is not null)
        {
            foreach (var paragraph in view.Content.Paragraphs)
                wrapper.Blocks.Add(DocumentMerge.CloneBlock(paragraph));
        }
        if (wrapper.Blocks.Count == 0)
            wrapper.Blocks.Add(new Paragraph());

        _hfEditorLoading = true;
        try
        {
            ConfigureFieldEvaluationContext(_hfSubEditor);
            _hfSubEditor.LoadModel(wrapper);
        }
        finally
        {
            _hfEditorLoading = false;
            _hfEditPending = false;
        }

        _hfPane.Visibility = Visibility.Visible;
        SyncHeaderFooterSectionRibbonState();
    }

    /// <summary>
    /// Writes the pane's unsaved edits to the header/footer its section displays (the shared owner when
    /// the slot is linked). Returns true when something was written.
    /// </summary>
    private bool CommitHeaderFooterPaneEdits()
    {
        if (_hfActiveSlot is not { } slot || !_hfEditPending)
            return false;

        _hfEditPending = false;
        _hfSubEditor.CommitToModel();
        var content = new HeaderFooter();
        foreach (var paragraph in _hfSubEditor.Model.Blocks.OfType<Paragraph>())
            content.Paragraphs.Add((Paragraph)DocumentMerge.CloneBlock(paragraph));

        _editor.CommitToModel();
        HeaderFooterSectionLinkPlanner.CommitSlotContent(
            _editor.Model.Sections,
            _hfActiveSectionIndex,
            slot,
            content);

        // Re-render the main editor (no-op page-settings commit triggers CommitToModel + Render inside
        // ApplyPageSettings without changing any setting).
        _editor.ApplyPageSettings(_ => { });
        MarkDirtyOutsideBodyUndo();
        return true;
    }

    /// <summary>The section + slot the Header/Footer ribbon commands act on right now.</summary>
    private HeaderFooterSectionSlotView? CurrentHeaderFooterSectionSlot()
    {
        var model = _editor.Model;
        if (_pagedEditMode)
        {
            // Print Layout: the header/footer region being edited on a page (none while the body is).
            return _pagedEditPanel?.ActiveHeaderFooterRegion is { } region
                ? HeaderFooterSectionLinkPlanner.Describe(model.Sections, region.SectionIndex, region.Slot)
                : null;
        }
        return _hfActiveSlot is { } slot
            ? HeaderFooterSectionLinkPlanner.Describe(model.Sections, _hfActiveSectionIndex, slot)
            : HeaderFooterSectionLinkPlanner.Describe(
                model.Sections,
                _editor.CurrentSectionIndex(),
                HeaderFooterSlotKind.Header);
    }

    // Header & Footer > Navigation > Previous / Next: show the neighbouring section's header/footer.
    private void MoveHeaderFooterSection(int direction)
    {
        if (CurrentHeaderFooterSectionSlot() is not { } current)
            return;

        if (_pagedEditMode && _pagedEditPanel is { } pages && pages.ActiveHeaderFooterRegion is { } region)
        {
            // The pages' header/footer edits land first (one undo unit), then the neighbouring section's
            // first page shows its header/footer region with the caret in it, as Word does.
            pages.CommitPendingEdits();
            if (HeaderFooterSectionLinkPlanner.PlanAdjacentSection(_editor.Model.Sections, current.SectionIndex, direction)
                is { } adjacent)
            {
                pages.FocusSectionHeaderFooter(adjacent, region.IsHeader);
            }
            SyncHeaderFooterSectionRibbonState();
            return;
        }

        CommitHeaderFooterPaneEdits();
        var sections = _editor.Model.Sections;
        if (HeaderFooterSectionLinkPlanner.PlanAdjacentSection(sections, current.SectionIndex, direction)
            is not { } target)
        {
            return;
        }

        // Keep the body caret in the section being edited so the section-scoped options on the same
        // tab (Different First Page, Different Odd & Even Pages, distances) act on that section too.
        _editor.MoveCaretToSectionStart(target);
        ShowHeaderFooterSlot(
            target,
            HeaderFooterSectionLinkPlanner.ResolveSlotForSection(sections[target], current.Slot));
        _hfSubEditor.Focus();
    }

    // Header & Footer > Navigation > Link to Previous.
    private void ToggleHeaderFooterLinkToPrevious()
    {
        if (CurrentHeaderFooterSectionSlot() is not { CanLinkToPrevious: true } current)
            return;

        if (_pagedEditMode && _pagedEditPanel is { } pages && pages.ActiveHeaderFooterRegion is { } region)
        {
            // Unlinking copies what the section currently shows, so the pages' unsaved typing lands first.
            pages.CommitPendingEdits();
            if (!HeaderFooterSectionLinkPlanner.ToggleLinkToPrevious(_editor.Model.Sections, current.SectionIndex, current.Slot))
                return;
            // Re-render: the pages rebuild from the model (they show the section's own or inherited
            // header/footer now); then the region being edited gets the caret back.
            _editor.ApplyPageSettings(_ => { });
            MarkDirtyOutsideBodyUndo();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (_pagedEditMode && ReferenceEquals(_pagedEditPanel, pages))
                    pages.FocusSectionHeaderFooter(region.SectionIndex, region.IsHeader);
                SyncHeaderFooterSectionRibbonState();
            }));
            SyncHeaderFooterSectionRibbonState();
            return;
        }

        // Unlinking copies what the section currently shows, so unsaved typing must land first.
        CommitHeaderFooterPaneEdits();
        _editor.CommitToModel();
        if (!HeaderFooterSectionLinkPlanner.ToggleLinkToPrevious(
                _editor.Model.Sections,
                current.SectionIndex,
                current.Slot))
        {
            return;
        }

        _editor.ApplyPageSettings(_ => { });
        MarkDirtyOutsideBodyUndo();
        if (_hfActiveSlot is not null)
            ShowHeaderFooterSlot(current.SectionIndex, current.Slot);
        else
            SyncHeaderFooterSectionRibbonState();
    }

    private void SyncHeaderFooterSectionRibbonState()
    {
        var view = CurrentHeaderFooterSectionSlot();
        _stateStore.SetChecked("freew.hf-link-to-previous", view?.IsLinkedToPrevious == true);
        _stateStore.SetEnabled("freew.hf-link-to-previous", view?.CanLinkToPrevious == true);
        _stateStore.SetEnabled("freew.hf-previous-section", view?.HasPreviousSection == true);
        _stateStore.SetEnabled("freew.hf-next-section", view?.HasNextSection == true);
    }
}
