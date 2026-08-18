using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// A <see cref="ScrollViewer"/> whose content is a <see cref="StackPanel"/> of <see cref="PageBox"/>
/// objects — one per page.  Used by the opt-in <see cref="DocumentViewMode.PagedEdit"/> mode.
///
/// <para>
/// <strong>Build strategy (Tag preservation):</strong> the panel works from a
/// <see cref="DocumentView"/> that has already been committed to its model via
/// <see cref="DocumentView.CommitToModel"/>.  It calls <see cref="DocumentView.LoadModel"/> on a
/// throw-away scratch editor to get a fresh WPF FlowDocument whose blocks carry all the correct
/// <c>Tag</c> payloads (ParagraphTag, RunMarkers, …).  It then <em>moves</em> those Block objects
/// — via <c>Blocks.Remove</c> + <c>Blocks.Add</c> into each page's body FlowDocument — so the Tag
/// objects are never touched, cloned or serialised.  <see cref="PaginatedCommitCoordinator"/> later
/// reads those same Tag-bearing blocks back through the standard <c>ReadParagraph / ReadList /
/// ReadTable</c> logic.
/// </para>
///
/// <para>
/// <strong>Engine-driven sharding (Phase 3b-1):</strong> <see cref="PaginationEngine.ComputeBlockPageAssignment"/>
/// is called on the source editor to derive per-page block index ranges. Explicit breaks,
/// overflow-driven boundaries, and footnote-reference positions are resolved by the same WPF
/// paginator. The result is a per-block page assignment array that drives
/// <see cref="ShardByPageAssignment"/>.
/// </para>
///
/// <para>
/// <strong>Live re-pagination (Phase 3b-1):</strong> every page box's body <see cref="System.Windows.Controls.RichTextBox"/>
/// fires <c>TextChanged</c>, which arms a ~300 ms debounce timer (mirroring
/// <c>MainWindow.ScheduleSplitPaneRefresh</c>).  On tick the coordinator commits all page boxes,
/// re-runs the engine, re-shards, and rebuilds the page stack while preserving Tag identity and
/// restoring the caret to the box that now holds the previously-active block.
/// </para>
///
/// <para>
/// Cross-page caret routing and cross-page clipboard / undo are in <see cref="PageBox"/> (caret)
/// and deferred to Phase 3b-2 (clipboard / undo).
/// </para>
/// </summary>
internal enum PaginatedPageLayoutMode
{
    Vertical,
    Horizontal,
    TwoColumnGrid
}

internal sealed class PaginatedEditorPanel : ScrollViewer
{
    // ── workspace background (same grey "desk" as the main editor) ────────────────────────────────
    private static readonly Brush WorkspaceBrush =
        new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));

    // ── public state ──────────────────────────────────────────────────────────────────────────────
    /// <summary>Ordered list of page boxes, one per page.</summary>
    internal IReadOnlyList<PageBox> PageBoxes => _pageBoxes;

    internal PaginatedPageLayoutMode LayoutMode => _layoutMode;

    // ── private state ─────────────────────────────────────────────────────────────────────────────
    private List<PageBox> _pageBoxes;
    private readonly Panel _pageHost;
    private readonly DocumentView _sourceEditor;  // kept for repagination
    private readonly PaginatedPageLayoutMode _layoutMode;
    private readonly IPlatformClipboard _platformClipboard;
    private DispatcherTimer? _repaginateTimer;

    // ── Phase 3b-2: cross-page selection and undo ─────────────────────────────────────────────────
    private readonly CrossPageSelection _crossPageSelection = new();
    private readonly CrossPageUndoCoordinator _undoCoordinator = new();

    // ── W18: drag-drop state for cross-page selection drag ────────────────────────────────────────
    //
    // WPF drag-drop for cross-page selection is implemented as a manual mouse-tracking state
    // machine (identical in spirit to the approach Word-WPF editors use): on MouseDown inside an
    // active cross-page selection we record the origin; on MouseMove we detect the drag threshold;
    // on MouseUp we perform the move-or-copy.  Native within-box drag-drop is unaffected: we only
    // intercept when _crossPageSelection.IsActive and the down-point is inside the selection.
    //
    private bool _dragPending;        // down inside cross-page selection; waiting for threshold
    private bool _dragActive;         // threshold exceeded; drag is in flight
    private Point _dragStartPoint;    // screen-space point where the left button went down
    private PageBox? _dragSourceBox;  // box that received the MouseDown event

    // ── construction ─────────────────────────────────────────────────────────────────────────────

    private PaginatedEditorPanel(
        DocumentView sourceEditor,
        List<PageBox> boxes,
        PaginatedPageLayoutMode layoutMode,
        IPlatformClipboard? platformClipboard)
    {
        _sourceEditor = sourceEditor;
        _pageBoxes = boxes;
        _layoutMode = layoutMode;
        _platformClipboard = platformClipboard ?? new WpfPlatformClipboard(
            sourceEditor.Dispatcher,
            new WpfPlatformClipboardOptions(
                MaxWriteAttempts: 1,
                FlushAfterWrite: false));
        _pageHost = BuildPageHost(layoutMode);
        foreach (var box in boxes)
        {
            AddPageBoxToHost(box);
            HookTextChanged(box);
            HookShiftArrow(box);
            HookDragDrop(box);
        }

        Background = WorkspaceBrush;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Content = _pageHost;

        // Attach undo coordinator after all boxes are wired.
        _undoCoordinator.Attach(this, sourceEditor);

        // Panel-level Ctrl+C / Ctrl+X / Ctrl+V for cross-page clipboard.
        PreviewKeyDown += OnPanelPreviewKeyDown;

        // Panel-level drag-drop hooks wired per-box below; see HookDragDrop.
    }

    // ── factory ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the panel from a committed <paramref name="sourceEditor"/>.
    ///
    /// <para>
    /// Steps:
    /// <list type="number">
    ///   <item>Clone-renders the model into a scratch <see cref="DocumentView"/> to get a fresh
    ///   FlowDocument whose blocks carry all Tag payloads.</item>
    ///   <item>Calls <see cref="PaginationEngine.ComputeBlockPageAssignment"/> on the
    ///   <paramref name="sourceEditor"/> to derive a per-block page assignment driven by the
    ///   WPF paginator (explicit <c>BreakPageBefore</c> / section breaks honoured).</item>
    ///   <item>Shards the scratch FlowDocument's top-level Block list into per-page sets using
    ///   <see cref="ShardByPageAssignment"/>.</item>
    ///   <item>Moves each page's blocks into its <see cref="PageBox"/> body FlowDocument.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal static PaginatedEditorPanel Build(
        DocumentView sourceEditor,
        PaginatedPageLayoutMode layoutMode = PaginatedPageLayoutMode.Vertical,
        bool includeParityBlankPages = false,
        IPlatformClipboard? platformClipboard = null)
    {
        var model = sourceEditor.Model;

        // ── Step 1: render the model into a scratch editor to obtain Tag-bearing WPF blocks ──────
        var scratch = new DocumentView();
        scratch.LoadModel(model);

        var allBlocks = scratch.Document.Blocks.ToList();
        scratch.Document.Blocks.Clear(); // detach so they can be re-parented

        // ── Step 2: engine-driven page assignment ─────────────────────────────────────────────────
        // PaginationEngine.ComputeBlockPageAssignment walks the WPF paginator's scratch clone and
        // returns a per-block page index array. Explicit breaks, overflow, and footnote-reference
        // positions are honoured; the page count comes from the authoritative print paginator.
        int[] assignment;
        int pageCount;
        try
        {
            assignment = PaginationEngine.ComputeBlockPageAssignment(sourceEditor);
            pageCount = assignment.Length > 0 ? assignment.Max() + 1 : 1;
            pageCount = Math.Max(1, pageCount);
        }
        catch
        {
            // Fall back: all blocks on page 0.
            assignment = new int[allBlocks.Count];
            pageCount = 1;
        }

        // ── Step 3: shard blocks by assignment ────────────────────────────────────────────────────
        var shards = ShardByPageAssignment(allBlocks, assignment, pageCount);

        // ── Step 3b: collect footnote IDs per page shard ─────────────────────────────────────────
        // Scan each page's body blocks for FootnoteMarker / EndnoteMarker tags (via DocumentView
        // helper) to determine which footnote entries belong at the bottom of each page.
        var pageFootnoteIds = new IReadOnlyList<int>[pageCount];
        for (var i = 0; i < pageCount; i++)
        {
            var (fnIds, _) = DocumentView.CollectNoteIds(shards[i]);
            pageFootnoteIds[i] = fnIds;
        }

        // ── Step 4: create one PageBox per page ───────────────────────────────────────────────────
        // Phase 4 + W18: resolve header/footer slots per page, routing to the correct section's
        // SectionHeadersFooters (per-section tracking) and passing pageCount for live page numbers.
        var pageToSection = HeaderFooterPagePlanner.MapPagesToSections(model, assignment, pageCount);
        var physicalPages = includeParityBlankPages
            ? HeaderFooterPagePlanner.BuildPhysicalPagePlan(pageToSection, model.Sections)
            : pageToSection.Select((pageSection, bodyPageIndex) => new SectionPhysicalPagePlan(
                bodyPageIndex,
                bodyPageIndex,
                pageSection,
                IsParityBlank: false)).ToList();
        var bodyToPhysicalPage = physicalPages
            .Where(page => page.BodyPageIndex is not null)
            .ToDictionary(page => page.BodyPageIndex!.Value, page => page.PhysicalPageIndex);
        var physicalAssignments = assignment
            .Select(bodyPageIndex => bodyPageIndex >= 0
                && bodyToPhysicalPage.TryGetValue(bodyPageIndex, out var physicalPageIndex)
                    ? physicalPageIndex
                    : HeaderFooterPagePlanner.UnassignedBlockPageIndex)
            .ToArray();
        var physicalPageSections = physicalPages.Select(page => page.PageSection).ToList();
        var pageNumberDisplay = PageNumberFormatDialogPlanner.BuildDisplayPlans(
            physicalPageSections,
            model,
            physicalAssignments);
        var differentOddEvenPages = HeaderFooterPagePlanner.UsesDifferentOddEvenPages(model);
        var hasEndnotes = model.Endnotes.Count > 0;
        var endnoteIds = model.Endnotes.Keys.OrderBy(k => k).ToList();
        var requiresDedicatedEndnotePage = hasEndnotes && RequiresDedicatedEndnotePage(sourceEditor);
        var physicalBodyPageCount = physicalPages.Count;
        var totalBoxCount = requiresDedicatedEndnotePage
            ? physicalBodyPageCount + 1
            : physicalBodyPageCount;
        var boxes = new List<PageBox>(totalBoxCount);
        foreach (var physicalPage in physicalPages)
        {
            var pageSection = physicalPage.PageSection;
            if (physicalPage.IsParityBlank)
            {
                boxes.Add(new PageBox(
                    physicalPage.PhysicalPageIndex + 1,
                    pageSection.PageSettings,
                    Array.Empty<System.Windows.Documents.Block>(),
                    pageCount: totalBoxCount,
                    isParitySyntheticPage: true));
                continue;
            }

            var bodyPageIndex = physicalPage.BodyPageIndex!.Value;
            // SG: use this page's section's own PageSettings for geometry (width, height, margins).
            // This makes portrait → landscape section breaks render each page at the correct size.
            var slots = HeaderFooterPagePlanner.ResolveSlots(
                pageSection.HeadersFooters,
                pageSection.SectionRelativePageNumber,
                pageSection.PageSettings,
                differentOddEvenPages,
                pageNumberDisplay[physicalPage.PhysicalPageIndex].LogicalPageNumber);
            var box = new PageBox(
                physicalPage.PhysicalPageIndex + 1,
                pageSection.PageSettings,
                shards[bodyPageIndex],
                sourceModel: model,
                headerSlot: slots.Header, headerSlotKind: slots.HeaderSlot,
                footerSlot: slots.Footer, footerSlotKind: slots.FooterSlot,
                pageCount: totalBoxCount,
                pageNumberText: pageNumberDisplay[physicalPage.PhysicalPageIndex].Text,
                sectionOrdinal: pageSection.SectionIndex + 1,
                sectionPageCount: pageSection.SectionPageCount
                    + (requiresDedicatedEndnotePage
                        && pageSection.SectionIndex == physicalPages[^1].PageSection.SectionIndex ? 1 : 0),
                footnoteIds: pageFootnoteIds[bodyPageIndex],
                endnoteIds: hasEndnotes && !requiresDedicatedEndnotePage && bodyPageIndex == pageCount - 1
                    ? endnoteIds
                    : null);
            // W21: record which section OWNS each slot (nearest preceding definer, per slot, not
            // necessarily the section being viewed) so CommitHeaderFooterSlots writes edits back to
            // the real, retained model object that a subsequent Render pass actually reads -- not a
            // throwaway synthesized display merge, and not a brand-new local definition that would
            // silently break "link to previous".
            box.OwnerSectionHf = ResolveOwnerSectionHf(model, pageSection.SectionIndex, slots.HeaderSlot);
            box.FooterOwnerSectionHf = ResolveOwnerSectionHf(model, pageSection.SectionIndex, slots.FooterSlot);
            boxes.Add(box);
        }

        // ── Endnotes page (synthetic last page) ───────────────────────────────────────────────────
        // All endnote entries are collected into one page at the end of the document, mirroring
        // Word's behaviour. It has no body blocks but retains the final section's header/footer.
        // Use the final section's page settings for the endnotes page.
        if (requiresDedicatedEndnotePage)
            boxes.Add(BuildDedicatedEndnotePage(
                model, physicalBodyPageCount, physicalAssignments, physicalPageSections,
                differentOddEvenPages, endnoteIds));

        // Wire neighbour links for cross-page caret routing.
        for (var i = 0; i < boxes.Count; i++)
        {
            boxes[i].PreviousBox = i > 0 ? boxes[i - 1] : null;
            boxes[i].NextBox = i < boxes.Count - 1 ? boxes[i + 1] : null;
        }

        return new PaginatedEditorPanel(sourceEditor, boxes, layoutMode, platformClipboard);
    }

    private static Panel BuildPageHost(PaginatedPageLayoutMode layoutMode)
    {
        if (layoutMode == PaginatedPageLayoutMode.TwoColumnGrid)
        {
            var grid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(12, 8, 12, 20)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            return grid;
        }

        return new StackPanel
        {
            Orientation = layoutMode == PaginatedPageLayoutMode.Horizontal
                ? Orientation.Horizontal
                : Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = layoutMode == PaginatedPageLayoutMode.Horizontal
                ? new Thickness(20, 0, 20, 20)
                : new Thickness(0, 0, 0, 20)
        };
    }

    private void AddPageBoxToHost(PageBox box)
    {
        if (_pageHost is Grid grid)
        {
            var pageIndex = grid.Children.Count;
            var row = pageIndex / 2;
            while (grid.RowDefinitions.Count <= row)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            box.Margin = new Thickness(12);
            Grid.SetColumn(box, pageIndex % 2);
            Grid.SetRow(box, row);
        }

        _pageHost.Children.Add(box);
    }

    private void ClearPageHost()
    {
        _pageHost.Children.Clear();
        if (_pageHost is Grid grid)
            grid.RowDefinitions.Clear();
    }

    /// <summary>Scrolls the editable surface to the requested 1-based page.</summary>
    internal void ScrollToPage(int pageNumber)
    {
        var page = _pageBoxes.FirstOrDefault(box => box.PageNumber == pageNumber);
        page?.BringIntoView();
    }


    private void HookTextChanged(PageBox box)
    {
        box.Body.TextChanged += OnAnyPageBodyTextChanged;
    }

    private void UnhookTextChanged(PageBox box)
    {
        box.Body.TextChanged -= OnAnyPageBodyTextChanged;
    }

    private void HookShiftArrow(PageBox box)
    {
        box.ShiftArrowBoundaryReached += OnShiftArrowBoundaryReached;
    }

    private void UnhookShiftArrow(PageBox box)
    {
        box.ShiftArrowBoundaryReached -= OnShiftArrowBoundaryReached;
    }

    private void OnAnyPageBodyTextChanged(object? sender, System.Windows.Controls.TextChangedEventArgs e)
        => ScheduleRepaginate();

    // ── Phase 3b-2: cross-page selection keyboard handler ────────────────────────────────────────

    /// <summary>
    /// Called by a <see cref="PageBox"/> when Shift+Down/Right is pressed at the end of the box,
    /// or Shift+Up/Left at the start.  Extends the cross-page selection into the adjacent box.
    /// </summary>
    private void OnShiftArrowBoundaryReached(PageBox source, bool movingForward)
    {
        var targetBox = movingForward ? source.NextBox : source.PreviousBox;
        if (targetBox is null)
            return;

        // Ensure we have an anchor.
        if (!_crossPageSelection.HasAnchor)
        {
            // Anchor at the current caret end of source.
            var anchorPtr = movingForward
                ? source.Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)
                : source.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            if (anchorPtr is null)
                return;
            _crossPageSelection.BeginSelection(_pageBoxes, source, anchorPtr);
        }

        // Active end is at the start/end of the target box.
        var targetPtr = movingForward
            ? targetBox.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)
            : targetBox.Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        if (targetPtr is null)
            return;

        _crossPageSelection.ExtendSelection(_pageBoxes, targetBox, targetPtr);

        // Move keyboard focus to the target box without clearing selection.
        targetBox.Body.Focus();
    }

    // ── Phase 3b-2: panel-level keyboard handler (Ctrl+C/X/V) ───────────────────────────────────

    private void OnPanelPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;

        switch (e.Key)
        {
            case Key.C when _crossPageSelection.IsActive:
                CopySelection();
                e.Handled = true;
                break;

            case Key.X when _crossPageSelection.IsActive:
                CutSelection();
                e.Handled = true;
                break;

            // Ctrl+V: paste at caret (handled only when we have something on the panel clipboard).
            // When there is no cross-page payload the native RichTextBox Ctrl+V fires.
            case Key.V:
                if (PasteAtCaret())
                    e.Handled = true;
                break;
        }
    }

    // ── Phase 3b-2: cross-page clipboard ─────────────────────────────────────────────────────────

    // Panel-level clipboard payload: plain-text representation of the most recent cross-page copy/cut.
    // This is a simple in-process clipboard — the system clipboard also receives the text.
    private string? _panelClipboard;

    /// <summary>
    /// The in-process copy of the most recently copied/cut cross-page selection text.
    /// Always set on a successful CopySelection; independent of the OS clipboard availability so
    /// callers (and tests) can verify the copy payload even when the system clipboard is locked.
    /// </summary>
    internal string? LastCopiedText => _panelClipboard;

    /// <summary>
    /// Copies the current cross-page selection to the clipboard as plain text.
    /// Within-box copy is handled natively by the RichTextBox.
    /// </summary>
    internal void CopySelection()
    {
        if (!_crossPageSelection.IsActive)
            return;

        var text = _crossPageSelection.GetSelectedText(_pageBoxes);
        if (text.Length == 0)
            return;

        _panelClipboard = text;
        SetClipboardTextWithRetry(text);
    }

    /// <summary>
    /// Sets the system clipboard text, retrying a few times with a short delay to handle the transient
    /// COM error (CLIPBRD_E_CANT_OPEN / 0x800401D0) that WPF's clipboard raises under contention.
    /// Swallows the failure gracefully after all retries are exhausted so a locked clipboard never
    /// crashes the editor — the panel clipboard (<see cref="LastCopiedText"/>) is always set regardless.
    /// </summary>
    private void SetClipboardTextWithRetry(string text)
    {
        const int MaxAttempts = 3;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                var result = _platformClipboard.WriteAsync(new PlatformClipboardContent(Text: text))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                if (!result.IsSuccess)
                    throw new ExternalException(result.ErrorMessage);
                return; // success
            }
            catch when (attempt < MaxAttempts - 1)
            {
                System.Threading.Thread.Sleep(10); // brief yield before retry
            }
            catch
            {
                // Final attempt failed — clipboard contention; panel clipboard is still set.
            }
        }
    }

    /// <summary>
    /// Cuts the current cross-page selection: copies it to the clipboard, then deletes the
    /// selected content from all spanned boxes and triggers re-pagination.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when every spanned box's content was fully removed;
    /// <see langword="false"/> when at least one spanned box's delete failed, in which case
    /// callers that would otherwise re-insert the cut text (e.g. a drag-move) MUST NOT do so —
    /// re-inserting text that was only partially removed from its source duplicates content.
    /// </returns>
    internal bool CutSelection()
    {
        if (!_crossPageSelection.IsActive)
            return false;

        CopySelection();
        return DeleteCrossPageSelection();
    }

    /// <summary>
    /// Pastes the panel clipboard (or system clipboard text) at the current caret.
    /// Returns true when paste was handled; false to let native RichTextBox handle it.
    /// </summary>
    internal bool PasteAtCaret()
    {
        // Only handle when there is text on the system clipboard.
        var read = _platformClipboard.ReadTextAsync().AsTask().GetAwaiter().GetResult();
        if (read.Status != PlatformClipboardReadStatus.Success || read.Value is not { } text)
            return false;

        if (text.Length == 0)
            return false;

        // Find the focused box.
        var focusedBox = _pageBoxes.FirstOrDefault(b => b.Body.IsKeyboardFocusWithin);
        if (focusedBox is null)
            return false;

        // Clear any cross-page selection first.
        if (_crossPageSelection.IsActive)
            DeleteCrossPageSelection();

        // Insert text at the caret inside the focused box.
        var caret = focusedBox.Body.CaretPosition;
        try
        {
            caret.InsertTextInRun(text);
        }
        catch { return false; }

        // Re-paginate.
        ScheduleRepaginate();
        return true;
    }

    /// <summary>
    /// Deletes the content spanned by the current cross-page selection from all boxes.
    /// After deletion, clears the selection and triggers re-pagination.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when every spanned box's range was successfully cleared;
    /// <see langword="false"/> when at least one spanned box's delete threw and therefore still
    /// holds some or all of its original content. A caller that re-inserts the pre-captured
    /// selection text elsewhere (a move) MUST check this return value first — inserting the full
    /// text while a source box's delete failed would duplicate that box's content.
    /// </returns>
    private bool DeleteCrossPageSelection()
    {
        if (!_crossPageSelection.IsActive)
            return false;

        // Get normalized range.
        var startBoxIdx = _crossPageSelection.AnchorBoxIndex < _crossPageSelection.ActiveBoxIndex
            ? _crossPageSelection.AnchorBoxIndex
            : _crossPageSelection.ActiveBoxIndex;
        var endBoxIdx = _crossPageSelection.AnchorBoxIndex < _crossPageSelection.ActiveBoxIndex
            ? _crossPageSelection.ActiveBoxIndex
            : _crossPageSelection.AnchorBoxIndex;

        var startPtr = _crossPageSelection.AnchorBoxIndex <= _crossPageSelection.ActiveBoxIndex
            ? _crossPageSelection.AnchorPointer
            : _crossPageSelection.ActivePointer;
        var endPtr = _crossPageSelection.AnchorBoxIndex <= _crossPageSelection.ActiveBoxIndex
            ? _crossPageSelection.ActivePointer
            : _crossPageSelection.AnchorPointer;

        if (startPtr is null || endPtr is null)
            return false;

        // Delete from each spanned box. Track whether every box's delete actually succeeded —
        // a per-box try/catch below is deliberately fail-open (a later box's position can become
        // invalid once an earlier box's content changes), but the caller must still learn when a
        // box was left un-deleted so it can refuse to re-insert the selection's captured text
        // elsewhere and duplicate that box's surviving content.
        bool allDeleted = true;
        for (int i = startBoxIdx; i <= endBoxIdx && i < _pageBoxes.Count; i++)
        {
            var box = _pageBoxes[i];
            try
            {
                TextPointer from = (i == startBoxIdx)
                    ? startPtr
                    : box.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward) ?? box.Body.Document.ContentStart;
                TextPointer to = (i == endBoxIdx)
                    ? endPtr
                    : box.Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward) ?? box.Body.Document.ContentEnd;

                var range = new TextRange(from, to);
                range.Text = string.Empty;
            }
            catch
            {
                // Skip — position may be invalid after earlier deletions, or this box's content
                // (e.g. a table/field/anchored object at the range boundary) rejected the delete.
                allDeleted = false;
            }
        }

        _crossPageSelection.Clear(_pageBoxes);
        ScheduleRepaginate();
        return allDeleted;
    }

    /// <summary>
    /// Arms a one-shot ~300 ms timer to commit and re-paginate. Resets on every TextChanged so rapid
    /// keystrokes collapse into a single rebuild at the end of the burst.
    /// Mirrors <c>MainWindow.ScheduleSplitPaneRefresh</c>.
    /// </summary>
    internal void ScheduleRepaginate()
    {
        if (_repaginateTimer is null)
        {
            _repaginateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _repaginateTimer.Tick += (_, _) =>
            {
                _repaginateTimer.Stop();
                try
                {
                    Repaginate();
                }
                catch (Exception)
                {
                    // This tick is armed by every TextChanged burst, so an exception escaping it is
                    // both fatal (an unhandled dispatcher exception) and recurring — it would fire
                    // again on the next keystroke, making the document impossible to keep editing.
                    // Keep the previously built page boxes and let the user carry on typing; the
                    // pagination engine's own inner guard already falls back per-block.
                }
            };
        }
        else
        {
            _repaginateTimer.Stop();
        }
        _repaginateTimer.Start();
    }

    /// <summary>
    /// Commits all page boxes, re-runs the pagination engine, re-shards, and rebuilds the page
    /// stack.  Preserves the caret by tracking which page box index has keyboard focus, then
    /// restoring focus to the page box that now occupies that slot (or the nearest one).
    ///
    /// <para>Must run on the UI/STA thread (called from the DispatcherTimer Tick).</para>
    /// </summary>
    internal void Repaginate()
    {
        // ── Track active box ──────────────────────────────────────────────────────────────────────
        // Record which page-box index currently has focus and the caret offset within it so we can
        // restore focus to the same position (or nearest) after rebuild.
        int focusedBoxIndex = -1;
        int caretCharOffset = 0;
        for (int i = 0; i < _pageBoxes.Count; i++)
        {
            if (_pageBoxes[i].Body.IsKeyboardFocusWithin)
            {
                focusedBoxIndex = i;
                // Best-effort caret offset within the page-box body.
                var tp = _pageBoxes[i].Body.CaretPosition;
                if (tp != null)
                {
                    try
                    {
                        caretCharOffset = _pageBoxes[i].Body.Document.ContentStart
                            .GetOffsetToPosition(tp);
                    }
                    catch { caretCharOffset = 0; }
                }
                break;
            }
        }

        // ── Commit current page boxes into the model ──────────────────────────────────────────────
        PaginatedCommitCoordinator.Commit(this, _sourceEditor);

        // ── Re-shard ──────────────────────────────────────────────────────────────────────────────
        var model = _sourceEditor.Model;

        var scratch = new DocumentView();
        scratch.LoadModel(model);
        var allBlocks = scratch.Document.Blocks.ToList();
        scratch.Document.Blocks.Clear();

        int[] assignment;
        int pageCount;
        try
        {
            assignment = PaginationEngine.ComputeBlockPageAssignment(_sourceEditor);
            pageCount = assignment.Length > 0 ? assignment.Max() + 1 : 1;
            pageCount = Math.Max(1, pageCount);
        }
        catch
        {
            assignment = new int[allBlocks.Count];
            pageCount = 1;
        }

        var shards = ShardByPageAssignment(allBlocks, assignment, pageCount);

        // Collect footnote IDs per page shard for the repaginated layout.
        var pageFootnoteIdsRep = new IReadOnlyList<int>[pageCount];
        for (var i = 0; i < pageCount; i++)
        {
            var (fnIds, _) = DocumentView.CollectNoteIds(shards[i]);
            pageFootnoteIdsRep[i] = fnIds;
        }

        // ── Rebuild page boxes ────────────────────────────────────────────────────────────────────
        // Notify undo coordinator that a repagination is starting (resets burst flag).
        _undoCoordinator.OnRepaginationStarting();

        // Unhook TextChanged, ShiftArrow, and DragDrop from old boxes before discarding them.
        foreach (var old in _pageBoxes)
        {
            UnhookTextChanged(old);
            _undoCoordinator.UnhookBox(old);
            UnhookShiftArrow(old);
            UnhookDragDrop(old);
        }

        // Clear any cross-page selection since page boxes are being rebuilt.
        _crossPageSelection.Clear(_pageBoxes);
        _dragPending = false;
        _dragActive = false;
        _dragSourceBox = null;

        ClearPageHost();
        _pageBoxes.Clear();

        var hasEndnotesRep = model.Endnotes.Count > 0;
        var endnoteIdsRep = model.Endnotes.Keys.OrderBy(k => k).ToList();
        var requiresDedicatedEndnotePageRep = hasEndnotesRep && RequiresDedicatedEndnotePage(_sourceEditor);
        var totalBoxCountRep = requiresDedicatedEndnotePageRep ? pageCount + 1 : pageCount;
        var pageToSectionRep = HeaderFooterPagePlanner.MapPagesToSections(model, assignment, pageCount);
        var pageNumberDisplayRep = PageNumberFormatDialogPlanner.BuildDisplayPlans(pageToSectionRep, model, assignment);
        var differentOddEvenPagesRep = HeaderFooterPagePlanner.UsesDifferentOddEvenPages(model);
        for (var i = 0; i < pageCount; i++)
        {
            // SG: per-section page geometry.
            var pageSection = pageToSectionRep[i];
            var slots = HeaderFooterPagePlanner.ResolveSlots(
                pageSection.HeadersFooters,
                pageSection.SectionRelativePageNumber,
                pageSection.PageSettings,
                differentOddEvenPagesRep,
                pageNumberDisplayRep[i].LogicalPageNumber);
            var box = new PageBox(i + 1, pageSection.PageSettings, shards[i],
                sourceModel: model,
                headerSlot: slots.Header, headerSlotKind: slots.HeaderSlot,
                footerSlot: slots.Footer, footerSlotKind: slots.FooterSlot,
                pageCount: totalBoxCountRep,
                pageNumberText: pageNumberDisplayRep[i].Text,
                sectionOrdinal: pageSection.SectionIndex + 1,
                sectionPageCount: pageSection.SectionPageCount
                    + (requiresDedicatedEndnotePageRep
                        && pageSection.SectionIndex == pageToSectionRep[^1].SectionIndex ? 1 : 0),
                footnoteIds: pageFootnoteIdsRep[i],
                endnoteIds: hasEndnotesRep && !requiresDedicatedEndnotePageRep && i == pageCount - 1
                    ? endnoteIdsRep
                    : null);
            // W21: section-aware commit -- owner resolved per slot (see Build's comment above).
            box.OwnerSectionHf = ResolveOwnerSectionHf(model, pageSection.SectionIndex, slots.HeaderSlot);
            box.FooterOwnerSectionHf = ResolveOwnerSectionHf(model, pageSection.SectionIndex, slots.FooterSlot);
            _pageBoxes.Add(box);
            AddPageBoxToHost(box);
            HookTextChanged(box);
            HookShiftArrow(box);
            HookDragDrop(box);
        }

        if (requiresDedicatedEndnotePageRep)
        {
            var endnotePage = BuildDedicatedEndnotePage(
                model, pageCount, assignment, pageToSectionRep,
                differentOddEvenPagesRep, endnoteIdsRep);
            _pageBoxes.Add(endnotePage);
            AddPageBoxToHost(endnotePage);
            HookTextChanged(endnotePage);
            HookShiftArrow(endnotePage);
            HookDragDrop(endnotePage);
        }

        // Wire neighbour links for cross-page caret routing.
        for (var i = 0; i < _pageBoxes.Count; i++)
        {
            _pageBoxes[i].PreviousBox = i > 0 ? _pageBoxes[i - 1] : null;
            _pageBoxes[i].NextBox = i < _pageBoxes.Count - 1 ? _pageBoxes[i + 1] : null;
        }

        // Re-attach undo coordinator to the new boxes.
        _undoCoordinator.ReAttach(_pageBoxes);

        // ── Restore focus ─────────────────────────────────────────────────────────────────────────
        if (focusedBoxIndex >= 0 && _pageBoxes.Count > 0)
        {
            int restoreIndex = Math.Min(focusedBoxIndex, _pageBoxes.Count - 1);
            var targetBox = _pageBoxes[restoreIndex];
            targetBox.Body.Focus();
            // Restore caret to the recorded offset (best effort).
            try
            {
                var doc = targetBox.Body.Document;
                var tp = doc.ContentStart.GetPositionAtOffset(caretCharOffset);
                if (tp != null)
                    targetBox.Body.CaretPosition = tp;
            }
            catch { /* ignore — caret lands at start */ }
        }
    }

    // ── Phase 3b-2: cross-page selection accessor ────────────────────────────────────────────────

    /// <summary>The panel-level cross-page selection model.</summary>
    internal CrossPageSelection CrossPageSelection => _crossPageSelection;

    /// <summary>The panel-level cross-page undo coordinator.</summary>
    internal CrossPageUndoCoordinator UndoCoordinator => _undoCoordinator;

    // ── Phase 3b-2: full rebuild (used by undo coordinator to restore a snapshot) ───────────────

    /// <summary>
    /// Re-shards the panel from the <see cref="_sourceEditor"/>'s current model state, rebuilding
    /// all page boxes.  Called by <see cref="CrossPageUndoCoordinator"/> after restoring a snapshot.
    /// The caret is moved to the first box.
    /// </summary>
    internal void Rebuild()
    {
        // Re-run the full Repaginate logic but without caret tracking (we lost the old boxes).
        _undoCoordinator.OnRepaginationStarting();

        foreach (var old in _pageBoxes)
        {
            UnhookTextChanged(old);
            _undoCoordinator.UnhookBox(old);
            UnhookShiftArrow(old);
            UnhookDragDrop(old);
        }
        _crossPageSelection.Clear(_pageBoxes);
        _dragPending = false;
        _dragActive = false;
        _dragSourceBox = null;
        ClearPageHost();
        _pageBoxes.Clear();

        var model = _sourceEditor.Model;

        var scratch = new DocumentView();
        scratch.LoadModel(model);
        var allBlocks = scratch.Document.Blocks.ToList();
        scratch.Document.Blocks.Clear();

        int[] assignment;
        int pageCount;
        try
        {
            assignment = PaginationEngine.ComputeBlockPageAssignment(_sourceEditor);
            pageCount = assignment.Length > 0 ? assignment.Max() + 1 : 1;
            pageCount = Math.Max(1, pageCount);
        }
        catch
        {
            assignment = new int[allBlocks.Count];
            pageCount = 1;
        }

        var shards = ShardByPageAssignment(allBlocks, assignment, pageCount);

        var pageFootnoteIdsReb = new IReadOnlyList<int>[pageCount];
        for (var i = 0; i < pageCount; i++)
        {
            var (fnIds, _) = DocumentView.CollectNoteIds(shards[i]);
            pageFootnoteIdsReb[i] = fnIds;
        }

        var hasEndnotesReb = model.Endnotes.Count > 0;
        var endnoteIdsReb = model.Endnotes.Keys.OrderBy(k => k).ToList();
        var requiresDedicatedEndnotePageReb = hasEndnotesReb && RequiresDedicatedEndnotePage(_sourceEditor);
        var totalBoxCountReb = requiresDedicatedEndnotePageReb ? pageCount + 1 : pageCount;
        var pageToSectionReb = HeaderFooterPagePlanner.MapPagesToSections(model, assignment, pageCount);
        var pageNumberDisplayReb = PageNumberFormatDialogPlanner.BuildDisplayPlans(pageToSectionReb, model, assignment);
        var differentOddEvenPagesReb = HeaderFooterPagePlanner.UsesDifferentOddEvenPages(model);
        for (var i = 0; i < pageCount; i++)
        {
            // SG: per-section page geometry.
            var pageSection = pageToSectionReb[i];
            var slots = HeaderFooterPagePlanner.ResolveSlots(
                pageSection.HeadersFooters,
                pageSection.SectionRelativePageNumber,
                pageSection.PageSettings,
                differentOddEvenPagesReb,
                pageNumberDisplayReb[i].LogicalPageNumber);
            var box = new PageBox(i + 1, pageSection.PageSettings, shards[i],
                sourceModel: model,
                headerSlot: slots.Header, headerSlotKind: slots.HeaderSlot,
                footerSlot: slots.Footer, footerSlotKind: slots.FooterSlot,
                pageCount: totalBoxCountReb,
                pageNumberText: pageNumberDisplayReb[i].Text,
                sectionOrdinal: pageSection.SectionIndex + 1,
                sectionPageCount: pageSection.SectionPageCount
                    + (requiresDedicatedEndnotePageReb
                        && pageSection.SectionIndex == pageToSectionReb[^1].SectionIndex ? 1 : 0),
                footnoteIds: pageFootnoteIdsReb[i],
                endnoteIds: hasEndnotesReb && !requiresDedicatedEndnotePageReb && i == pageCount - 1
                    ? endnoteIdsReb
                    : null);
            // W21: section-aware commit -- owner resolved per slot (see Build's comment above).
            box.OwnerSectionHf = ResolveOwnerSectionHf(model, pageSection.SectionIndex, slots.HeaderSlot);
            box.FooterOwnerSectionHf = ResolveOwnerSectionHf(model, pageSection.SectionIndex, slots.FooterSlot);
            _pageBoxes.Add(box);
            AddPageBoxToHost(box);
            HookTextChanged(box);
            HookShiftArrow(box);
            HookDragDrop(box);
        }

        if (requiresDedicatedEndnotePageReb)
        {
            var endnotePage = BuildDedicatedEndnotePage(
                model, pageCount, assignment, pageToSectionReb,
                differentOddEvenPagesReb, endnoteIdsReb);
            _pageBoxes.Add(endnotePage);
            AddPageBoxToHost(endnotePage);
            HookTextChanged(endnotePage);
            HookShiftArrow(endnotePage);
            HookDragDrop(endnotePage);
        }

        for (var i = 0; i < _pageBoxes.Count; i++)
        {
            _pageBoxes[i].PreviousBox = i > 0 ? _pageBoxes[i - 1] : null;
            _pageBoxes[i].NextBox = i < _pageBoxes.Count - 1 ? _pageBoxes[i + 1] : null;
        }

        _undoCoordinator.ReAttach(_pageBoxes);

        if (_pageBoxes.Count > 0)
            _pageBoxes[0].Body.Focus();
    }

    private static bool RequiresDedicatedEndnotePage(DocumentView editor)
    {
        if (editor.Model.Endnotes.Count == 0)
            return false;

        try
        {
            var page = editor.Model.Page;
            var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);
            var flow = PrintLayout.BuildPaginatedDocument(editor);
            PaginationEngine.ApplySectionBreakFlags(editor, flow);
            var inner = ((IDocumentPaginatorSource)flow).DocumentPaginator;
            inner.PageSize = new Size(pageWidth, pageHeight);
            var paginator = new HeaderFooterPaginator(inner, editor.Model, page);
            paginator.ComputePageCount();
            return paginator.RequiresDedicatedEndnotePage;
        }
        catch
        {
            return true;
        }
    }

    private static PageBox BuildDedicatedEndnotePage(
        TextDocument model,
        int bodyPageCount,
        IReadOnlyList<int> blockPageAssignments,
        IReadOnlyList<HeaderFooterPageSectionPlan> bodyPageSections,
        bool differentOddEvenPages,
        IReadOnlyList<int> endnoteIds)
    {
        var finalBodySection = bodyPageSections[^1];
        var endnoteSection = finalBodySection with
        {
            SectionRelativePageNumber = finalBodySection.SectionRelativePageNumber + 1,
            SectionPageCount = finalBodySection.SectionPageCount + 1
        };
        var displaySections = bodyPageSections.Concat([endnoteSection]).ToList();
        var pageNumberDisplay = PageNumberFormatDialogPlanner.BuildDisplayPlans(
            displaySections,
            model,
            blockPageAssignments)[^1];
        var slots = HeaderFooterPagePlanner.ResolveSlots(
            endnoteSection.HeadersFooters,
            endnoteSection.SectionRelativePageNumber,
            endnoteSection.PageSettings,
            differentOddEvenPages,
            pageNumberDisplay.LogicalPageNumber);
        var endnotePage = new PageBox(
            bodyPageCount + 1,
            endnoteSection.PageSettings,
            Array.Empty<System.Windows.Documents.Block>(),
            sourceModel: model,
            headerSlot: slots.Header,
            headerSlotKind: slots.HeaderSlot,
            footerSlot: slots.Footer,
            footerSlotKind: slots.FooterSlot,
            pageCount: bodyPageCount + 1,
            pageNumberText: pageNumberDisplay.Text,
            sectionOrdinal: endnoteSection.SectionIndex + 1,
            sectionPageCount: endnoteSection.SectionPageCount,
            endnoteIds: endnoteIds,
            isEndnoteSyntheticPage: true);
        endnotePage.OwnerSectionHf = ResolveOwnerSectionHf(model, endnoteSection.SectionIndex, slots.HeaderSlot);
        endnotePage.FooterOwnerSectionHf = ResolveOwnerSectionHf(model, endnoteSection.SectionIndex, slots.FooterSlot);
        return endnotePage;
    }

    /// <summary>
    /// Resolves the real, persisted <see cref="SectionHeadersFooters"/> instance that an edit to
    /// <paramref name="slot"/> on a page in section <paramref name="sectionIndex"/> should be
    /// committed back to -- the nearest preceding section (including this one) that actually OWNS
    /// that slot, exactly like <see cref="HeaderFooterPageSectionPlan.HeadersFooters"/>'s per-slot
    /// walk-backward DISPLAY resolution, except this returns the real retained container instead of
    /// (possibly) a freshly synthesized display-only merge.
    ///
    /// <para>
    /// Two failure modes this must avoid: (1) using the synthesized display object as the commit
    /// target would silently discard the edit (it writes into an object nothing else ever reads);
    /// (2) always committing to the section being VIEWED instead of the section that OWNS the slot
    /// would silently create a brand-new local definition on a section that was only ever inheriting
    /// the content, breaking "link to previous" the instant inherited content is edited. Delegates to
    /// <see cref="HeaderFooterPagePlanner.ResolveSlotOwner"/> (shared with the read-path per-slot
    /// walk-backward logic) to avoid both.
    /// </para>
    /// </summary>
    private static SectionHeadersFooters ResolveOwnerSectionHf(
        TextDocument model, int sectionIndex, HeaderFooterSlotKind slot) =>
        HeaderFooterPagePlanner.ResolveSlotOwner(model.Sections, sectionIndex, slot);

    // ── Phase 4: header/footer slot commit ───────────────────────────────────────────────────────

    /// <summary>
    /// Commits all in-page header/footer sub-editors back to the correct
    /// <see cref="SectionHeadersFooters"/> slots on the source model.
    ///
    /// <para>
    /// Each distinct slot name is committed only once (from the first page box that owns it) so
    /// that editing a header on page 2 overwrites the shared "header" slot exactly once — the same
    /// result as the Wave 11 docked pane.
    /// </para>
    ///
    /// <para>
    /// Called by <see cref="PaginatedCommitCoordinator.Commit"/> before rebuilding the model
    /// blocks, so the updated header/footer paragraphs are already in the slot when the next
    /// Render pass picks them up.
    /// </para>
    /// </summary>
    /// <summary>
    /// Commits the in-page header/footer sub-editors back to the model slots they own.
    ///
    /// <para>
    /// <strong>W21 — section-aware:</strong> each page box carries an
    /// <see cref="PageBox.OwnerSectionHf"/> / <see cref="PageBox.FooterOwnerSectionHf"/> pair (set by
    /// <c>ComputePageSectionMap</c> during Build/Repaginate) identifying the real, retained
    /// <see cref="SectionHeadersFooters"/> the box's header and footer sub-editors should each write
    /// back to -- the nearest preceding section that actually OWNS that slot, which for a fully-linked
    /// page can differ from the section the page is displayed in, and can differ between the header
    /// and footer slots independently. For single-section documents this is always the document-level
    /// <see cref="TextDocument.FinalSectionHeadersFooters"/>.
    /// </para>
    ///
    /// <para>Deduplication key is <c>(owning HF instance identity, shared slot identity)</c>, tracked separately
    /// for header and footer, so that each distinct section+slot pair is committed exactly once even
    /// if multiple page boxes share the same slot (e.g. all non-first pages of section 2 share the
    /// "header" slot for that section).</para>
    /// </summary>
    internal void CommitHeaderFooterSlots(DocumentView helperEditor)
    {
        // Fallback HF used when a box has no owner set (should not happen after Build/Repaginate).
        var docLevelHf = _sourceEditor.Model.FinalSectionHeadersFooters;

    // Deduplication key: (section HF instance identity, shared slot identity).
    var committedSlots = new HashSet<(SectionHeadersFooters hf, HeaderFooterSlotKind slot)>();

        foreach (var box in _pageBoxes)
        {
            var headerHf = box.OwnerSectionHf ?? docLevelHf;
            var footerHf = box.FooterOwnerSectionHf ?? docLevelHf;

            // Commit header slot (once per owning-section+slot pair).
            if (box.HeaderSlot is { } headerSlot && committedSlots.Add((headerHf, headerSlot)))
                box.CommitHeaderSlot(helperEditor, headerHf);

            // Commit footer slot independently -- it may own to a different section than the header.
            if (box.FooterSlot is { } footerSlot && committedSlots.Add((footerHf, footerSlot)))
                box.CommitFooterSlot(helperEditor, footerHf);
        }
    }

    /// <summary>
    /// Focuses the in-page header or footer region for a given shared slot. Used to route the
    /// <c>freew.hf-edit-*</c> ribbon commands to the in-page sub-editor when PagedEdit is active,
    /// instead of opening the docked pane.
    ///
    /// <para>Returns <c>true</c> when a matching sub-editor was found and focused; <c>false</c>
    /// when the slot is not currently visible (e.g. first-header but DifferentFirstPage is off).</para>
    /// </summary>
    internal bool FocusInPageHfRegion(HeaderFooterSlotKind slot)
    {
        // Find the first page box whose header or footer sub-editor matches the slot.
        foreach (var box in _pageBoxes)
        {
            if (box.HeaderSlot == slot && box.HeaderSubEditor is { } hSub)
            {
                hSub.Focus();
                return true;
            }
            if (box.FooterSlot == slot && box.FooterSubEditor is { } fSub)
            {
                fSub.Focus();
                return true;
            }
        }
        return false;
    }

    // ── W18: drag-drop of cross-page selection ───────────────────────────────────────────────────
    //
    // Approach: manual mouse-tracking state machine.
    //   MouseDown  → if cross-page selection is active and hit-test says the down-point is inside
    //                the selection range, record a pending drag.
    //   MouseMove  → if pending and past the system drag threshold, set dragActive, capture mouse.
    //   MouseUp    → if dragActive, compute drop TextPointer, check it is outside the selection
    //                (no-op if inside), then move (or ctrl-copy) the content.
    //
    // Within-box native drag-drop is unaffected: we only intercept when both
    // _crossPageSelection.IsActive AND the down-point is inside the cross-page selection range.

    private void HookDragDrop(PageBox box)
    {
        box.Body.PreviewMouseLeftButtonDown += OnBodyMouseDown;
        box.Body.PreviewMouseMove           += OnBodyMouseMove;
        box.Body.PreviewMouseLeftButtonUp   += OnBodyMouseUp;
    }

    private void UnhookDragDrop(PageBox box)
    {
        box.Body.PreviewMouseLeftButtonDown -= OnBodyMouseDown;
        box.Body.PreviewMouseMove           -= OnBodyMouseMove;
        box.Body.PreviewMouseLeftButtonUp   -= OnBodyMouseUp;
    }

    private void OnBodyMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Only intercept when a cross-page selection exists.
        if (!_crossPageSelection.IsActive)
            return;

        // Find which box received the event.
        var box = _pageBoxes.FirstOrDefault(b => ReferenceEquals(b.Body, sender));
        if (box is null)
            return;

        // Check whether the click position is inside the selection range.
        if (!IsPointInsideCrossPageSelection(box, e.GetPosition(box.Body)))
            return;

        // Record pending drag; do NOT suppress the event (let the native RTB handle mouse-down
        // for click-to-place-caret; if the user actually drags we intercept at MouseMove).
        _dragPending = true;
        _dragActive = false;
        _dragStartPoint = e.GetPosition(this);
        _dragSourceBox = box;
    }

    private void OnBodyMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragPending || e.LeftButton != MouseButtonState.Pressed)
        {
            if (_dragActive)
            {
                // Drag is live — suppress native text selection in the body boxes.
                e.Handled = true;
            }
            return;
        }

        // Check system drag threshold.
        var current = e.GetPosition(this);
        var dx = current.X - _dragStartPoint.X;
        var dy = current.Y - _dragStartPoint.Y;
        var threshold = SystemParameters.MinimumHorizontalDragDistance;
        if (Math.Abs(dx) < threshold && Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // Threshold exceeded — start drag.
        _dragPending = false;
        _dragActive = true;

        // Suppress the native RichTextBox mouse-move so it doesn't change the selection.
        e.Handled = true;
    }

    private void OnBodyMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragActive)
        {
            // Not dragging — just clear pending state.
            _dragPending = false;
            return;
        }

        _dragActive = false;
        _dragPending = false;

        // Determine whether this is a copy (Ctrl held) or move.
        bool isCopy = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        // Find the box under the mouse for the drop target.
        var dropBox = _pageBoxes.FirstOrDefault(b => ReferenceEquals(b.Body, sender));
        if (dropBox is null)
        {
            // Mouse up outside a known box — cancel.
            return;
        }

        // Compute the drop TextPointer at the mouse-up position.
        var dropPoint = e.GetPosition(dropBox.Body);
        TextPointer? dropPtr = GetTextPointerAtPoint(dropBox, dropPoint);
        if (dropPtr is null)
            return;

        // Determine the drop box index.
        int dropBoxIdx = CrossPageSelection.IndexOfBox(_pageBoxes, dropBox);
        if (dropBoxIdx < 0)
            return;

        // No-op: drop inside the selection range.
        if (IsDropInsideSelection(dropBoxIdx, dropPtr))
            return;

        // Obtain the text content of the cross-page selection.
        var selectedText = _crossPageSelection.GetSelectedText(_pageBoxes);
        if (selectedText.Length == 0)
            return;

        // Snapshot the drop pointer before a move-cut so the TextPointer remains valid.
        // WPF TextPointer objects track the document tree; after DeleteCrossPageSelection
        // removes content from different boxes the pointer in the target box (a different box)
        // stays valid.  dropPtr is held as dropPtrRef through the cut.
        TextPointer dropPtrRef = dropPtr;

        if (!isCopy)
        {
            // Cut (delete) the selection — reuses the existing DeleteCrossPageSelection path.
            // The IsDropInsideSelection guard above ensures the drop target is outside the
            // selection, so dropPtrRef (pointing into a different box or past the selection end)
            // remains valid after the cut.
            //
            // Atomicity: if any spanned box's delete failed (CutSelection returns false), that
            // box still holds some or all of its original content, so re-inserting the full
            // pre-captured selectedText at the drop target would duplicate it. Abort the move
            // here instead — the selection is already cleared/re-paginated by CutSelection.
            if (!CutSelection())
            {
                e.Handled = true;
                return;
            }
        }

        // Insert the text at the drop position.
        try
        {
            dropPtrRef.InsertTextInRun(selectedText);
        }
        catch
        {
            // InsertTextInRun fails if not inside a Run — fall back to creating a new paragraph.
            try
            {
                var range = new TextRange(dropPtrRef, dropPtrRef);
                range.Text = selectedText;
            }
            catch { /* ignore — best-effort */ }
        }

        ScheduleRepaginate();
        e.Handled = true;
    }

    /// <summary>
    /// Returns true when <paramref name="dropPtr"/> in <paramref name="dropBoxIdx"/> falls inside
    /// the current cross-page selection (i.e. drop is a no-op).
    /// </summary>
    private bool IsDropInsideSelection(int dropBoxIdx, TextPointer dropPtr)
    {
        if (!_crossPageSelection.IsActive)
            return false;

        int startBox = Math.Min(_crossPageSelection.AnchorBoxIndex, _crossPageSelection.ActiveBoxIndex);
        int endBox   = Math.Max(_crossPageSelection.AnchorBoxIndex, _crossPageSelection.ActiveBoxIndex);
        var startPtr = _crossPageSelection.AnchorBoxIndex <= _crossPageSelection.ActiveBoxIndex
            ? _crossPageSelection.AnchorPointer!
            : _crossPageSelection.ActivePointer!;
        var endPtr = _crossPageSelection.AnchorBoxIndex <= _crossPageSelection.ActiveBoxIndex
            ? _crossPageSelection.ActivePointer!
            : _crossPageSelection.AnchorPointer!;

        // Drop is before the selection start box or after the selection end box — not inside.
        if (dropBoxIdx < startBox || dropBoxIdx > endBox)
            return false;

        // Drop is in the start box: check pointer is after startPtr.
        if (dropBoxIdx == startBox)
        {
            try { return dropPtr.CompareTo(startPtr) >= 0; } catch { return false; }
        }

        // Drop is in the end box: check pointer is before endPtr.
        if (dropBoxIdx == endBox)
        {
            try { return dropPtr.CompareTo(endPtr) <= 0; } catch { return false; }
        }

        // Drop is in a fully-covered intermediate box.
        return true;
    }

    /// <summary>
    /// Returns true when <paramref name="point"/> (in box-local coordinates) lies within the
    /// current cross-page selection in <paramref name="box"/>.
    /// </summary>
    private bool IsPointInsideCrossPageSelection(PageBox box, Point point)
    {
        if (!_crossPageSelection.IsActive)
            return false;

        int boxIdx = CrossPageSelection.IndexOfBox(_pageBoxes, box);
        if (boxIdx < 0)
            return false;

        int startBox = Math.Min(_crossPageSelection.AnchorBoxIndex, _crossPageSelection.ActiveBoxIndex);
        int endBox   = Math.Max(_crossPageSelection.AnchorBoxIndex, _crossPageSelection.ActiveBoxIndex);

        // Box is outside the selected range entirely.
        if (boxIdx < startBox || boxIdx > endBox)
            return false;

        // For intermediate boxes the entire box is selected — any point is inside.
        if (boxIdx > startBox && boxIdx < endBox)
            return true;

        // For the start/end box, hit-test the point against the selection bounds.
        // Use GetPositionFromPoint to get the TextPointer at the mouse position and then
        // delegate to IsDropInsideSelection for the boundary check.
        try
        {
            var hitPtr = box.Body.GetPositionFromPoint(point, snapToText: true);
            if (hitPtr is null)
                return false;

            return IsDropInsideSelection(boxIdx, hitPtr);
        }
        catch { return false; }
    }

    /// <summary>
    /// Returns the <see cref="TextPointer"/> closest to <paramref name="point"/> (box-local
    /// coordinates) inside <paramref name="box"/>'s body RichTextBox.
    /// </summary>
    private static TextPointer? GetTextPointerAtPoint(PageBox box, Point point)
    {
        try
        {
            return box.Body.GetPositionFromPoint(point, snapToText: true);
        }
        catch { return null; }
    }

    // ── sharding ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Distributes <paramref name="blocks"/> into per-page lists according to the per-block
    /// <paramref name="pageAssignment"/> array produced by
    /// <see cref="PaginationEngine.ComputeBlockPageAssignment"/>.
    ///
    /// <para>
    /// Each entry in <paramref name="pageAssignment"/> is a 0-based page index for the
    /// corresponding block.  Blocks are appended to their assigned page's list in document order.
    /// Pages with no blocks receive an empty list.  An empty block set always produces one page
    /// with zero blocks (degenerate case).
    /// </para>
    ///
    /// <para>
    /// This replaces the Phase 3a even-distribution sharding with engine-driven assignment so that
    /// explicit page breaks (<c>BreakPageBefore</c> / section breaks) land the first post-break
    /// paragraph in the correct page box.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<System.Windows.Documents.Block>> ShardByPageAssignment(
        IReadOnlyList<System.Windows.Documents.Block> blocks,
        int[] pageAssignment,
        int pageCount)
    {
        pageCount = Math.Max(1, pageCount);
        var result = new List<List<System.Windows.Documents.Block>>(pageCount);
        for (var i = 0; i < pageCount; i++)
            result.Add([]);

        if (blocks.Count == 0)
            return result;

        for (int i = 0; i < blocks.Count; i++)
        {
            var pg = (i < pageAssignment.Length) ? pageAssignment[i] : 0;
            pg = Math.Clamp(pg, 0, pageCount - 1);
            result[pg].Add(blocks[i]);
        }

        return result;
    }

        // ── inter-page gap geometry ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the Y range (top, bottom) of the inter-page gap between
    /// <paramref name="pageIndex"/> and <paramref name="pageIndex"/>+1 in the panel's
    /// coordinate space. Used to draw selection highlight bands over the gap when a
    /// cross-page selection spans it.
    /// Returns null when <paramref name="pageIndex"/> is out of range or no gap exists.
    /// </summary>
    internal (double Top, double Bottom)? GetInterPageGapRect(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pageBoxes.Count - 1)
            return null;

        var thisBox = _pageBoxes[pageIndex];
        var nextBox = _pageBoxes[pageIndex + 1];

        // Get the bottom of thisBox and top of nextBox in panel coordinates.
        try
        {
            var thisPos = thisBox.TranslatePoint(new Point(0, thisBox.ActualHeight), _pageHost);
            var nextPos = nextBox.TranslatePoint(new Point(0, 0), _pageHost);
            if (thisPos.Y >= nextPos.Y)
                return null; // no gap (or overlap)
            return (thisPos.Y, nextPos.Y);
        }
        catch
        {
            return null;
        }
    }
}
