using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

#if DEBUG

/// <summary>
/// A <see cref="ScrollViewer"/> whose content is a <see cref="StackPanel"/> of <see cref="PageBox"/>
/// objects — one per page.  Used by the DEV-ONLY <see cref="DocumentViewMode.PagedEdit"/> mode.
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
/// is called on the source editor to derive per-page block index ranges.  Blocks with an explicit
/// <c>BreakPageBefore</c> (set by the paginator's section-break post-processing) open new page slots;
/// overflow-driven page boundaries are honoured by the same paginator page count.  The result is a
/// per-block page assignment array that drives <see cref="ShardByPageAssignment"/>.
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
internal sealed class PaginatedEditorPanel : ScrollViewer
{
    // ── workspace background (same grey "desk" as the main editor) ────────────────────────────────
    private static readonly Brush WorkspaceBrush =
        new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));

    // ── public state ──────────────────────────────────────────────────────────────────────────────
    /// <summary>Ordered list of page boxes, one per page.</summary>
    internal IReadOnlyList<PageBox> PageBoxes => _pageBoxes;

    // ── private state ─────────────────────────────────────────────────────────────────────────────
    private List<PageBox> _pageBoxes;
    private readonly StackPanel _stack;
    private readonly DocumentView _sourceEditor;  // kept for repagination
    private DispatcherTimer? _repaginateTimer;

    // ── Phase 3b-2: cross-page selection and undo ─────────────────────────────────────────────────
    private readonly CrossPageSelection _crossPageSelection = new();
    private readonly CrossPageUndoCoordinator _undoCoordinator = new();

    // ── construction ─────────────────────────────────────────────────────────────────────────────

    private PaginatedEditorPanel(DocumentView sourceEditor, List<PageBox> boxes)
    {
        _sourceEditor = sourceEditor;
        _pageBoxes = boxes;

        _stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20)
        };
        foreach (var box in boxes)
        {
            _stack.Children.Add(box);
            HookTextChanged(box);
            HookShiftArrow(box);
        }

        Background = WorkspaceBrush;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Content = _stack;

        // Attach undo coordinator after all boxes are wired.
        _undoCoordinator.Attach(this, sourceEditor);

        // Panel-level Ctrl+C / Ctrl+X / Ctrl+V for cross-page clipboard.
        PreviewKeyDown += OnPanelPreviewKeyDown;
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
    internal static PaginatedEditorPanel Build(DocumentView sourceEditor)
    {
        var model = sourceEditor.Model;
        var page = model.Page;

        // ── Step 1: render the model into a scratch editor to obtain Tag-bearing WPF blocks ──────
        var scratch = new DocumentView();
        scratch.LoadModel(model);

        var allBlocks = scratch.Document.Blocks.ToList();
        scratch.Document.Blocks.Clear(); // detach so they can be re-parented

        // ── Step 2: engine-driven page assignment ─────────────────────────────────────────────────
        // PaginationEngine.ComputeBlockPageAssignment walks the WPF paginator's scratch clone and
        // returns a per-block page index array.  Explicit breaks (BreakPageBefore / section breaks)
        // are honoured; the page count comes from the authoritative print paginator.
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

        // ── Step 4: create one PageBox per page ───────────────────────────────────────────────────
        // Phase 4: resolve header/footer slots per page.
        var boxes = new List<PageBox>(pageCount);
        for (var i = 0; i < pageCount; i++)
        {
            var (hSlot, hName, fSlot, fName) = ResolveHfSlots(model, pageNumber: i + 1, pageCount);
            boxes.Add(new PageBox(i + 1, page, shards[i],
                sourceModel: model,
                headerSlot: hSlot, headerSlotName: hName,
                footerSlot: fSlot, footerSlotName: fName));
        }

        // Wire neighbour links for cross-page caret routing.
        for (var i = 0; i < boxes.Count; i++)
        {
            boxes[i].PreviousBox = i > 0 ? boxes[i - 1] : null;
            boxes[i].NextBox = i < boxes.Count - 1 ? boxes[i + 1] : null;
        }

        return new PaginatedEditorPanel(sourceEditor, boxes);
    }

    // ── Phase 4: slot resolution ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Selects the header and footer <see cref="HeaderFooter"/> slots for a given 1-based
    /// <paramref name="pageNumber"/>, applying Word's DifferentFirstPage / DifferentOddEvenPages
    /// rules.  Returns the slot object (may be null when the slot is empty) and its canonical name.
    ///
    /// <list type="bullet">
    ///   <item>Page 1 with <c>DifferentFirstPage</c> → first-header / first-footer.</item>
    ///   <item>Even pages with <c>DifferentOddEvenPages</c> → even-header / even-footer.</item>
    ///   <item>Otherwise → default header / footer.</item>
    /// </list>
    /// </summary>
    private static (HeaderFooter? hSlot, string hName,
                    HeaderFooter? fSlot, string fName)
        ResolveHfSlots(TextDocument model, int pageNumber, int pageCount)
    {
        var hf      = model.FinalSectionHeadersFooters;
        var diffFirst   = model.Page.DifferentFirstPage;
        var diffOddEven = model.Page.DifferentOddEvenPages;

        if (diffFirst && pageNumber == 1)
        {
            return (hf.FirstHeader, "first-header",
                    hf.FirstFooter, "first-footer");
        }

        if (diffOddEven && pageNumber % 2 == 0)
        {
            return (hf.EvenHeader, "even-header",
                    hf.EvenFooter, "even-footer");
        }

        return (hf.Header, "header",
                hf.Footer, "footer");
    }

    // ── live repagination ─────────────────────────────────────────────────────────────────────────

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
        try { System.Windows.Clipboard.SetText(text); } catch { /* clipboard locked */ }
    }

    /// <summary>
    /// Cuts the current cross-page selection: copies it to the clipboard, then deletes the
    /// selected content from all spanned boxes and triggers re-pagination.
    /// </summary>
    internal void CutSelection()
    {
        if (!_crossPageSelection.IsActive)
            return;

        CopySelection();
        DeleteCrossPageSelection();
    }

    /// <summary>
    /// Pastes the panel clipboard (or system clipboard text) at the current caret.
    /// Returns true when paste was handled; false to let native RichTextBox handle it.
    /// </summary>
    internal bool PasteAtCaret()
    {
        // Only handle when there is text on the system clipboard.
        string text;
        try
        {
            if (!System.Windows.Clipboard.ContainsText())
                return false;
            text = System.Windows.Clipboard.GetText();
        }
        catch { return false; }

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
    private void DeleteCrossPageSelection()
    {
        if (!_crossPageSelection.IsActive)
            return;

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
            return;

        // Delete from each spanned box.
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
            catch { /* skip — position may be invalid after earlier deletions */ }
        }

        _crossPageSelection.Clear(_pageBoxes);
        ScheduleRepaginate();
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
                Repaginate();
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
        var page = model.Page;

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

        // ── Rebuild page boxes ────────────────────────────────────────────────────────────────────
        // Notify undo coordinator that a repagination is starting (resets burst flag).
        _undoCoordinator.OnRepaginationStarting();

        // Unhook TextChanged and ShiftArrow from old boxes before discarding them.
        foreach (var old in _pageBoxes)
        {
            UnhookTextChanged(old);
            _undoCoordinator.UnhookBox(old);
            UnhookShiftArrow(old);
        }

        // Clear any cross-page selection since page boxes are being rebuilt.
        _crossPageSelection.Clear(_pageBoxes);

        _stack.Children.Clear();
        _pageBoxes.Clear();

        for (var i = 0; i < pageCount; i++)
        {
            var (hSlot, hName, fSlot, fName) = ResolveHfSlots(model, pageNumber: i + 1, pageCount);
            var box = new PageBox(i + 1, page, shards[i],
                sourceModel: model,
                headerSlot: hSlot, headerSlotName: hName,
                footerSlot: fSlot, footerSlotName: fName);
            _pageBoxes.Add(box);
            _stack.Children.Add(box);
            HookTextChanged(box);
            HookShiftArrow(box);
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
        }
        _crossPageSelection.Clear(_pageBoxes);
        _stack.Children.Clear();
        _pageBoxes.Clear();

        var model = _sourceEditor.Model;
        var page = model.Page;

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

        for (var i = 0; i < pageCount; i++)
        {
            var (hSlot, hName, fSlot, fName) = ResolveHfSlots(model, pageNumber: i + 1, pageCount);
            var box = new PageBox(i + 1, page, shards[i],
                sourceModel: model,
                headerSlot: hSlot, headerSlotName: hName,
                footerSlot: fSlot, footerSlotName: fName);
            _pageBoxes.Add(box);
            _stack.Children.Add(box);
            HookTextChanged(box);
            HookShiftArrow(box);
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
    internal void CommitHeaderFooterSlots(DocumentView helperEditor)
    {
        var hf          = _sourceEditor.Model.FinalSectionHeadersFooters;
        var committedSlots = new HashSet<string>(StringComparer.Ordinal);

        foreach (var box in _pageBoxes)
        {
            // Commit header slot (once per distinct slot name).
            if (box.HeaderSlotName is { } hName && committedSlots.Add(hName))
                box.CommitHfSlots(helperEditor, hf);
            // CommitHfSlots commits BOTH header AND footer for this box in one call.
            // If the footer has a different slot name from the header (shouldn't happen normally)
            // record it too so we don't commit it again from another box.
            if (box.FooterSlotName is { } fName)
                committedSlots.Add(fName);
        }
    }

    /// <summary>
    /// Focuses the in-page header or footer region for a given slot name.  Used to route the
    /// <c>freew.hf-edit-*</c> ribbon commands to the in-page sub-editor when PagedEdit is active,
    /// instead of opening the docked pane.
    ///
    /// <para>Returns <c>true</c> when a matching sub-editor was found and focused; <c>false</c>
    /// when the slot is not currently visible (e.g. first-header but DifferentFirstPage is off).</para>
    /// </summary>
    internal bool FocusInPageHfRegion(string slotName)
    {
        // Find the first page box whose header or footer sub-editor matches the slot name.
        foreach (var box in _pageBoxes)
        {
            if (box.HeaderSlotName == slotName && box.HeaderSubEditor is { } hSub)
            {
                hSub.Focus();
                return true;
            }
            if (box.FooterSlotName == slotName && box.FooterSubEditor is { } fSub)
            {
                fSub.Focus();
                return true;
            }
        }
        return false;
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

    // ── Phase 3a even-distribution sharding (kept for reference; no longer called) ────────────────

    /// <summary>
    /// Distributes <paramref name="blocks"/> across <paramref name="pageCount"/> page slots as
    /// evenly as possible (round-robin assignment).  Superseded by <see cref="ShardByPageAssignment"/>
    /// in Phase 3b-1; retained here for reference only.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<System.Windows.Documents.Block>> ShardBlocks(
        IReadOnlyList<System.Windows.Documents.Block> blocks,
        int pageCount)
    {
        var result = new List<List<System.Windows.Documents.Block>>(pageCount);
        for (var i = 0; i < pageCount; i++)
            result.Add([]);

        if (blocks.Count == 0 || pageCount <= 0)
        {
            if (result.Count == 0)
                result.Add([]);
            return result;
        }

        var baseCount = blocks.Count / pageCount;
        var remainder = blocks.Count % pageCount;
        var blockIndex = 0;
        for (var p = 0; p < pageCount; p++)
        {
            var count = baseCount + (p < remainder ? 1 : 0);
            for (var j = 0; j < count && blockIndex < blocks.Count; j++, blockIndex++)
                result[p].Add(blocks[blockIndex]);
        }

        return result;
    }
}

#endif
