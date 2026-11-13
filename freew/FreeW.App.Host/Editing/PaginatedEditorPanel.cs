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

/// <summary>
/// A caret position in the paged editor expressed independently of pagination: the index of the
/// top-level body block (counted across every page in order) and the symbol offset inside it.
/// </summary>
internal sealed record PagedCaretPosition(int BlockIndex, int Offset);

internal sealed class PaginatedEditorPanel : ScrollViewer, IExternalEditingSurface
{
    // ── workspace background (same grey "desk" as the main editor) ────────────────────────────────
    // Frozen: a static brush is shared by every UI thread that builds a panel (each test, each window).
    private static readonly Brush WorkspaceBrush = CreateWorkspaceBrush();

    private static Brush CreateWorkspaceBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
        brush.Freeze();
        return brush;
    }

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

    // ── model ownership (W7: paged editing as a real editing surface) ─────────────────────────────
    private TextDocument _sourceModel;          // the document these pages were built from
    private bool _hasPendingBodyEdits;          // typed on the pages but not yet in the model
    private bool _rebuilding;                   // page boxes are being replaced; their TextChanged is not an edit
    private int _ownModelMutationDepth;         // > 0 while this panel itself is changing the model
    private bool _attachedAsSurface;
    private bool _externalRebuildQueued;
    private PageBox? _lastCaretBox;             // the page the caret was last placed in
    private readonly HashSet<PageBox> _editedSinceRepagination = new();
    private readonly Dictionary<PageBox, double> _contentBottoms = new();

    // ── W9: incremental re-flow ───────────────────────────────────────────────────────────────────
    // The layout session re-paginates from what changed. Each body page box knows which model blocks
    // it shows (PageBox.ModelStart/ModelBlocks) and the page facts it was built from (PageBox.ModelKey),
    // so a commit replaces only the edited pages' blocks and a re-flow rebuilds only the pages whose
    // blocks or page facts changed. _boxRangesValid is false while that is unknown.
    private readonly PagedLayoutSession _layout;
    private bool _boxRangesValid;
    private readonly HashSet<PageBox> _editedSinceCommit = new();

    /// <summary>The incremental layout session (diagnostics for tests and the probe).</summary>
    internal PagedLayoutSession LayoutSession => _layout;

    /// <summary>Page boxes the last re-flow kept rather than rebuilt (diagnostics).</summary>
    internal int LastRebuildKeptBoxes { get; private set; }

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
        IPlatformClipboard? platformClipboard,
        PagedLayoutSession layout)
    {
        _layout = layout;
        _sourceEditor = sourceEditor;
        _sourceModel = sourceEditor.Model;
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

        ScheduleContentBottomCapture();

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
        IPlatformClipboard? platformClipboard = null,
        List<PaginationEngine.SegmentPagination>? segmentPaginations = null)
    {
        var model = sourceEditor.Model;
        var layout = new PagedLayoutSession(sourceEditor);

        // ── Step 1: engine-driven page assignment ─────────────────────────────────────────────────
        // Explicit breaks, overflow, and footnote-reference positions are honoured; the page count
        // comes from the authoritative paginator. The editing surface only needs each block's page, so
        // it uses the incremental layout session (identical to the layout-only full computation);
        // printing (segmentPaginations requested) keeps the print clone it draws from.
        int[] assignment;
        int pageCount;
        try
        {
            assignment = segmentPaginations is null
                ? layout.ComputeAssignment(out var totalPageCount)
                : PaginationEngine.ComputeBlockPageAssignment(sourceEditor, out totalPageCount, segmentPaginations);
            pageCount = assignment.Length > 0 ? assignment.Max() + 1 : 1;
            // A final block that runs on past the page it starts on still owns those physical pages.
            pageCount = Math.Max(1, Math.Max(pageCount, totalPageCount));
        }
        catch
        {
            // Fall back: all blocks on page 0.
            assignment = new int[model.Blocks.Count];
            pageCount = 1;
        }

        // ── Step 2: render Tag-bearing WPF blocks and shard them by page ──────────────────────────
        // The editing surface shards by render unit (a list run is one WPF List), so every page knows
        // exactly which model blocks it holds; printing keeps its established sharding.
        IReadOnlyList<IReadOnlyList<System.Windows.Documents.Block>> shards;
        PagePlan? pagePlan = segmentPaginations is null ? PlanPages(model, assignment, pageCount) : null;
        if (pagePlan is not null)
        {
            shards = RenderPages(model, pagePlan, _ => true);
        }
        else
        {
            var scratch = new DocumentView();
            scratch.LoadModel(model);
            var allBlocks = scratch.Document.Blocks.ToList();
            scratch.Document.Blocks.Clear(); // detach so they can be re-parented
            shards = ShardByPageAssignment(allBlocks, assignment, pageCount);
        }

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
        var boxRanges = pagePlan is null ? null : new List<(int Start, int Count)>(totalBoxCount);
        var nextModelStart = 0;
        foreach (var physicalPage in physicalPages)
        {
            var pageSection = physicalPage.PageSection;
            if (pagePlan is not null)
            {
                var range = physicalPage.IsParityBlank ? (nextModelStart, 0) : pagePlan.Ranges[physicalPage.BodyPageIndex!.Value];
                boxRanges!.Add(range);
                nextModelStart = range.Item1 + range.Item2;
            }
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
            if (pagePlan is not null)
            {
                box.ModelKey = BoxKey(
                    physicalPage.PhysicalPageIndex, pageSection, slots, pageNumberDisplay[physicalPage.PhysicalPageIndex].Text,
                    totalBoxCount, pageSection.SectionPageCount, model, pagePlan.Ranges[bodyPageIndex],
                    hasEndnotes && !requiresDedicatedEndnotePage && bodyPageIndex == pageCount - 1);
            }
            // freew-cc-3: wire the content-control lock probe so this box's plain RichTextBox can refuse
            // to mutate a locked control (see PageBox.ContentControlLockProbe's doc comment).
            box.ContentControlLockProbe = sourceEditor.TryEvaluateContentControlLock;
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
        {
            boxes.Add(BuildDedicatedEndnotePage(
                model, physicalBodyPageCount, physicalAssignments, physicalPageSections,
                differentOddEvenPages, endnoteIds));
            boxRanges?.Add((model.Blocks.Count, 0));
        }

        // Wire neighbour links for cross-page caret routing.
        for (var i = 0; i < boxes.Count; i++)
        {
            boxes[i].PreviousBox = i > 0 ? boxes[i - 1] : null;
            boxes[i].NextBox = i < boxes.Count - 1 ? boxes[i + 1] : null;
        }

        var panel = new PaginatedEditorPanel(sourceEditor, boxes, layoutMode, platformClipboard, layout);
        // Only the plain vertical/horizontal page stack is rebuilt page by page; the parity-blank
        // (Multiple Pages) plan keeps whole rebuilds.
        if (boxRanges is not null && !includeParityBlankPages)
            panel.SetBoxModelRanges(boxRanges, model);
        return panel;
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
        box.Body.SelectionChanged += OnAnyPageBodySelectionChanged;
        box.Body.GotKeyboardFocus += OnAnyPageBodyGotKeyboardFocus;
        box.BoundaryEditRequested += OnBoundaryEditRequested;
        box.DocumentNavigationRequested += OnDocumentNavigationRequested;
        if (box.HeaderSubEditor is { } header)
            header.TextChanged += OnHeaderFooterTextChanged;
        if (box.FooterSubEditor is { } footer)
            footer.TextChanged += OnHeaderFooterTextChanged;
    }

    private void UnhookTextChanged(PageBox box)
    {
        box.Body.TextChanged -= OnAnyPageBodyTextChanged;
        box.Body.SelectionChanged -= OnAnyPageBodySelectionChanged;
        box.Body.GotKeyboardFocus -= OnAnyPageBodyGotKeyboardFocus;
        box.BoundaryEditRequested -= OnBoundaryEditRequested;
        box.DocumentNavigationRequested -= OnDocumentNavigationRequested;
        if (box.HeaderSubEditor is { } header)
            header.TextChanged -= OnHeaderFooterTextChanged;
        if (box.FooterSubEditor is { } footer)
            footer.TextChanged -= OnHeaderFooterTextChanged;
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
    {
        if (_rebuilding)
            return;

        _hasPendingBodyEdits = true;
        if (sender is RichTextBox body && _pageBoxes.FirstOrDefault(b => ReferenceEquals(b.Body, body)) is { } box)
        {
            _lastCaretBox = box;
            _editedSinceRepagination.Add(box);
            _editedSinceCommit.Add(box);
        }
        else
        {
            // An edit this panel cannot place on a page: the next commit reads every page.
            _boxRangesValid = false;
        }
        Edited?.Invoke();
        ScheduleRepaginate();
    }

    private void OnHeaderFooterTextChanged(object? sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!_rebuilding)
        {
            // Other pages show the same header/footer: the next re-flow rebuilds every page.
            _headerFooterEditedSinceRebuild = true;
            Edited?.Invoke();
        }
    }

    private bool _headerFooterEditedSinceRebuild;

    private void OnAnyPageBodySelectionChanged(object? sender, RoutedEventArgs e)
    {
        if (!_rebuilding && sender is RichTextBox body
            && _pageBoxes.FirstOrDefault(b => ReferenceEquals(b.Body, body)) is { } box)
        {
            _lastCaretBox = box;
        }
    }

    private void OnAnyPageBodyGotKeyboardFocus(object? sender, KeyboardFocusChangedEventArgs e)
        => OnAnyPageBodySelectionChanged(sender, e);

    // ── W7: the paged editor as the document's editing surface ───────────────────────────────────

    /// <summary>
    /// Raised whenever the user changes the document on the pages (body or header/footer), before
    /// the change reaches the model -- the host marks the document dirty from it, so closing the
    /// window right after typing still prompts to save.
    /// </summary>
    internal event Action? Edited;

    /// <summary>The document these pages currently show and commit into.</summary>
    internal TextDocument SourceModel => _sourceModel;

    /// <summary>True when page text was edited since the last commit into the model.</summary>
    internal bool HasPendingBodyEdits => _hasPendingBodyEdits;

    /// <summary>The page the caret is in (keyboard focus first, else where it was last placed).</summary>
    internal PageBox? ActiveBox =>
        _pageBoxes.FirstOrDefault(b => b.Body.IsKeyboardFocusWithin)
        ?? (_lastCaretBox is not null && _pageBoxes.Contains(_lastCaretBox) ? _lastCaretBox : null);

    internal void MarkBodyCommitted() => _hasPendingBodyEdits = false;

    internal void MarkBodyPending() => _hasPendingBodyEdits = true;

    /// <summary>True while this panel is itself writing the model (commit, undo, redo).</summary>
    internal bool IsMutatingModel => _ownModelMutationDepth > 0;

    /// <summary>
    /// Runs a model change made by this panel itself (commit, undo, redo) so the source editor's
    /// change notification is not mistaken for an external edit that requires a rebuild.
    /// </summary>
    internal void RunOwnModelMutation(Action mutation)
    {
        _ownModelMutationDepth++;
        try
        {
            mutation();
        }
        finally
        {
            _ownModelMutationDepth--;
        }
    }

    /// <summary>
    /// Makes this panel the source editor's live editing surface: commits, Save/AutoSave/Print and
    /// Undo/Redo on the (hidden) source editor are routed here, and model changes made by commands
    /// elsewhere rebuild the pages.
    /// </summary>
    internal void AttachAsActiveSurface()
    {
        if (_attachedAsSurface)
            return;
        _attachedAsSurface = true;
        _sourceEditor.ExternalEditingSurface = this;
        // The hidden source editor then refreshes only the blocks a page commit replaced.
        _sourceEditor.TrackBodyUnits = true;
        _sourceEditor.EditingSessionChanged += OnSourceEditingSessionChanged;
    }

    /// <summary>
    /// Releases the source editor (call after the final commit, when leaving the paged view): stops
    /// pending re-pagination so nothing is written into the model after the view is gone.
    /// </summary>
    internal void Detach()
    {
        _repaginateTimer?.Stop();
        if (!_attachedAsSurface)
            return;
        _attachedAsSurface = false;
        if (ReferenceEquals(_sourceEditor.ExternalEditingSurface, this))
        {
            _sourceEditor.ExternalEditingSurface = null;
            _sourceEditor.TrackBodyUnits = false;
        }
        _sourceEditor.EditingSessionChanged -= OnSourceEditingSessionChanged;
    }

    private void OnSourceEditingSessionChanged()
    {
        if (_ownModelMutationDepth > 0 || !_attachedAsSurface || _externalRebuildQueued)
            return;

        // A ribbon command, dialog or find/replace changed the model through the bus. Commands commit
        // first (CommitToModel routes here), so the pages hold nothing the model lacks; rebuild once
        // the command has finished.
        _externalRebuildQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _externalRebuildQueued = false;
            if (_attachedAsSurface)
                Rebuild(CaptureCaret());
        }));
    }

    void IExternalEditingSurface.CommitPendingEdits() => CommitPendingEdits();

    /// <summary>Writes pending page edits into the model now (one undo unit).</summary>
    internal void CommitPendingEdits()
    {
        // Called often and incidentally (ribbon state queries commit the editor first), so it must
        // be a no-op when nothing is pending and must never schedule work of its own.
        var reflowWasScheduled = _repaginateTimer?.IsEnabled == true;
        _repaginateTimer?.Stop();
        PaginatedCommitCoordinator.Commit(this, _sourceEditor);
        // The re-pagination the burst already scheduled still has to happen, for the layout.
        if (reflowWasScheduled && _attachedAsSurface && _pageBoxes.Count > 0)
            _repaginateTimer?.Start();
    }

    bool IExternalEditingSurface.CanUndo => _undoCoordinator.CanUndo;

    bool IExternalEditingSurface.CanRedo => _undoCoordinator.CanRedo;

    void IExternalEditingSurface.Undo() => _undoCoordinator.Undo();

    void IExternalEditingSurface.Redo() => _undoCoordinator.Redo();

    void IExternalEditingSurface.OnSourceModelReloaded()
    {
        // A different document was loaded into the source editor: the pages' pending edits belong
        // to the old one and must not be written into the new one.
        _hasPendingBodyEdits = false;
        Rebuild(caret: null);
    }

    /// <summary>Records the caret as a pagination-independent document position.</summary>
    internal PagedCaretPosition? CaptureCaret()
    {
        var active = ActiveBox;
        if (active is null)
            return null;

        var blockBase = 0;
        foreach (var box in _pageBoxes)
        {
            var blocks = box.Body.Document.Blocks;
            if (!ReferenceEquals(box, active))
            {
                blockBase += blocks.Count;
                continue;
            }

            try
            {
                var caret = box.Body.CaretPosition;
                var index = 0;
                foreach (var block in blocks)
                {
                    if (caret.CompareTo(block.ContentEnd) <= 0)
                    {
                        var offset = Math.Max(0, block.ContentStart.GetOffsetToPosition(caret));
                        return new PagedCaretPosition(blockBase + index, offset);
                    }
                    index++;
                }

                return new PagedCaretPosition(blockBase + Math.Max(0, blocks.Count - 1), int.MaxValue);
            }
            catch
            {
                return new PagedCaretPosition(blockBase, 0);
            }
        }

        return null;
    }

    /// <summary>
    /// Places the caret at <paramref name="caret"/> in whichever page now holds that block (clamped
    /// to the document), focuses that page and brings it into view.
    /// </summary>
    internal void RestoreCaret(PagedCaretPosition? caret)
    {
        if (caret is null || _pageBoxes.Count == 0)
            return;

        var remaining = caret.BlockIndex;
        PageBox? target = null;
        System.Windows.Documents.Block? targetBlock = null;
        foreach (var box in _pageBoxes)
        {
            var count = box.Body.Document.Blocks.Count;
            if (remaining < count)
            {
                target = box;
                targetBlock = box.Body.Document.Blocks.ElementAt(remaining);
                break;
            }
            remaining -= count;
        }

        if (target is null)
        {
            // Past the end (blocks were deleted): the end of the last page that has any text.
            target = _pageBoxes.LastOrDefault(b => b.Body.Document.Blocks.Count > 0) ?? _pageBoxes[^1];
            targetBlock = target.Body.Document.Blocks.LastBlock;
        }

        target.Body.Focus();
        _lastCaretBox = target;
        try
        {
            TextPointer? position = null;
            if (targetBlock is not null)
            {
                var length = targetBlock.ContentStart.GetOffsetToPosition(targetBlock.ContentEnd);
                position = targetBlock.ContentStart.GetPositionAtOffset(Math.Min(caret.Offset, length))
                    ?? targetBlock.ContentEnd;
                position = position.GetInsertionPosition(LogicalDirection.Backward) ?? position;
            }
            if (position is not null)
                target.Body.CaretPosition = position;
            target.BringIntoView();
        }
        catch
        {
            // Caret stays at the page's default position.
        }
    }

    // ── W7: editing and navigation across a page boundary ────────────────────────────────────────

    private void OnBoundaryEditRequested(PageBox source, bool backward)
    {
        if (backward)
            MergeAcrossBoundary(source.PreviousBox, source);
        else
            MergeAcrossBoundary(source, source.NextBox);
    }

    /// <summary>
    /// Backspace at the top of a page / Delete at the bottom of one: joins the last paragraph of
    /// <paramref name="upper"/> with the first paragraph of <paramref name="lower"/>, exactly as the
    /// same key joins two paragraphs on one page (the upper paragraph keeps its formatting), and puts
    /// the caret at the join. Returns false (nothing changes) when either side is not a plain
    /// paragraph -- a table or list edge -- or a locked content control is involved.
    /// </summary>
    internal bool MergeAcrossBoundary(PageBox? upper, PageBox? lower)
    {
        if (upper is null || lower is null)
            return false;
        if (upper.IsParitySyntheticPage || lower.IsParitySyntheticPage)
            return false;

        if (upper.Body.Document.Blocks.LastBlock is not System.Windows.Documents.Paragraph upperParagraph
            || lower.Body.Document.Blocks.FirstBlock is not System.Windows.Documents.Paragraph lowerParagraph)
        {
            return false;
        }

        if (RangeTouchesLockedContentControl(upper, upperParagraph.ContentStart, upperParagraph.ContentEnd)
            || RangeTouchesLockedContentControl(lower, lowerParagraph.ContentStart, lowerParagraph.ContentEnd))
        {
            return false;
        }

        try
        {
            var joinOffset = upperParagraph.ContentStart.GetOffsetToPosition(upperParagraph.ContentEnd);
            var moved = lowerParagraph.Inlines.ToList();
            lowerParagraph.Inlines.Clear();
            lower.Body.Document.Blocks.Remove(lowerParagraph);
            foreach (var inline in moved)
                upperParagraph.Inlines.Add(inline);

            upper.Body.Focus();
            _lastCaretBox = upper;
            var join = upperParagraph.ContentStart.GetPositionAtOffset(joinOffset) ?? upperParagraph.ContentEnd;
            upper.Body.CaretPosition = join.GetInsertionPosition(LogicalDirection.Forward) ?? join;
        }
        catch
        {
            return false;
        }

        // Both TextChanged events already flagged the edit; re-flow now rather than after the
        // debounce, so the text pulled up from the next page is laid out where the user looks.
        ScheduleRepaginate();
        return true;
    }

    internal void OnDocumentNavigationRequested(PageBox source, PageNavigationTarget target, bool extendSelection)
    {
        if (_pageBoxes.Count == 0)
            return;

        PageBox destination;
        bool toStart;
        switch (target)
        {
            case PageNavigationTarget.DocumentStart:
                destination = _pageBoxes[0];
                toStart = true;
                break;
            case PageNavigationTarget.DocumentEnd:
                destination = _pageBoxes[^1];
                toStart = false;
                break;
            case PageNavigationTarget.PreviousPage:
                if (source.PreviousBox is null)
                {
                    destination = source;
                    toStart = true;
                    break;
                }
                destination = source.PreviousBox;
                toStart = true;
                break;
            default:
                if (source.NextBox is null)
                {
                    destination = source;
                    toStart = false;
                    break;
                }
                destination = source.NextBox;
                toStart = true;
                break;
        }

        var document = destination.Body.Document;
        var pointer = toStart
            ? document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)
            : document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        if (pointer is null)
            return;

        if (extendSelection)
        {
            if (ReferenceEquals(destination, source))
            {
                source.Body.Selection.Select(source.Body.Selection.Start, pointer);
            }
            else
            {
                if (!_crossPageSelection.HasAnchor)
                    _crossPageSelection.BeginSelection(_pageBoxes, source, source.Body.CaretPosition);
                _crossPageSelection.ExtendSelection(_pageBoxes, destination, pointer);
            }
        }
        else
        {
            _crossPageSelection.Clear(_pageBoxes);
            destination.Body.CaretPosition = pointer;
        }

        destination.Body.Focus();
        _lastCaretBox = destination;
        destination.BringIntoView();
    }

    /// <summary>Ctrl+A in paged view: selects the whole body, across every page.</summary>
    internal void SelectAllPages()
    {
        var first = _pageBoxes.FirstOrDefault(b => !b.IsParitySyntheticPage);
        var last = _pageBoxes.LastOrDefault(b => !b.IsParitySyntheticPage);
        if (first is null || last is null)
            return;

        var start = first.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        var end = last.Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        if (start is null || end is null)
            return;

        if (ReferenceEquals(first, last))
        {
            first.Body.Selection.Select(start, end);
            return;
        }

        _crossPageSelection.Clear(_pageBoxes);
        _crossPageSelection.BeginSelection(_pageBoxes, first, start);
        _crossPageSelection.ExtendSelection(_pageBoxes, last, end);
    }

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
            case Key.A when (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Alt)) == 0
                            && _pageBoxes.Any(b => b.Body.IsKeyboardFocusWithin):
                SelectAllPages();
                e.Handled = true;
                break;

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

        // freew-cc-3: refuse to paste into a locked content control (this box's InsertTextInRun call
        // below mutates the live FlowDocument directly with no lock awareness of its own -- see
        // PageBox.ContentControlLockProbe's doc comment). The native RichTextBox Ctrl+V this method would
        // otherwise defer to (returning false) is separately gated too, so returning false here is safe.
        if (focusedBox.ContentControlLockProbe?.Invoke(focusedBox.Body.Selection.Start) == false)
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
    internal bool DeleteCrossPageSelection()
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

        // freew-cc-3 / shared-drag-drop F1: Word declines the WHOLE deletion outright when it would
        // remove a locked content control, rather than deleting part of it (see the sibling Avalonia
        // TryCommitBodyTextDragMove/SelectionReachesLockedContentControl comment for the canonical
        // semantics this mirrors). Scan every spanned box's own sub-range for a locked run/paragraph
        // BEFORE any mutation runs -- every caller of this method (Ctrl+X, a drag-move's cut, and
        // PasteAtCaret's stale-selection cleanup) must refuse to silently delete protected content the
        // same way the caret-based Enter/Backspace/Delete/Paste/Cut guards already refuse to mutate it.
        for (int i = startBoxIdx; i <= endBoxIdx && i < _pageBoxes.Count; i++)
        {
            var lockBox = _pageBoxes[i];
            try
            {
                TextPointer lockFrom = (i == startBoxIdx)
                    ? startPtr
                    : lockBox.Body.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward) ?? lockBox.Body.Document.ContentStart;
                TextPointer lockTo = (i == endBoxIdx)
                    ? endPtr
                    : lockBox.Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward) ?? lockBox.Body.Document.ContentEnd;

                if (RangeTouchesLockedContentControl(lockBox, lockFrom, lockTo))
                    return false;
            }
            catch
            {
                // Position resolution failed here -- fall through to the real deletion loop below,
                // which has its own pre-existing fail-open handling for exactly this race.
            }
        }

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
    /// Whether any position between <paramref name="from"/> and <paramref name="to"/> (a single page
    /// box's own sub-range of a cross-page selection) sits on a content-control run that
    /// <paramref name="box"/>'s <see cref="PageBox.ContentControlLockProbe"/> would refuse to edit --
    /// used by <see cref="DeleteCrossPageSelection"/> to decline a delete/drag-move outright rather than
    /// silently removing part of a locked control (shared-drag-drop finding F1). Only public
    /// <see cref="TextPointer"/> navigation is used here -- never the private Tag marker types the probe
    /// itself pattern-matches on (see <c>DocumentView.TryEvaluateContentControlLock</c>'s doc comment on
    /// why this file cannot do so directly) -- so this works uniformly across every page's own
    /// FlowDocument. A position strictly inside a run's text is where <c>TextPointer.Parent</c> resolves
    /// to the run itself, the same shape of position every other probe call site in this class already
    /// uses (see <c>PagedEditContentControlLockTests.PositionAfterText</c>), so each Text-context segment
    /// in the range is probed one character in from its start.
    /// </summary>
    internal static bool RangeTouchesLockedContentControl(PageBox box, TextPointer from, TextPointer to)
    {
        if (box.ContentControlLockProbe is not { } probe)
            return false;

        if (probe(from) == false || probe(to) == false)
            return true;

        var pointer = from;
        while (pointer is not null && pointer.CompareTo(to) < 0)
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text
                && probe(pointer.GetPositionAtOffset(1, LogicalDirection.Forward) ?? pointer) == false)
            {
                return true;
            }

            var next = pointer.GetNextContextPosition(LogicalDirection.Forward);
            if (next is null || next.CompareTo(pointer) <= 0)
                break;
            pointer = next;
        }

        return false;
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
                    CommitAndRepaginateIfLayoutChanged();
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
    /// The debounce tick after a typing burst. The burst is always committed (one undo unit); the
    /// expensive part -- re-running the shared paginator over the whole document and rebuilding every
    /// page -- is skipped when no edited page's text got taller or shorter (typing within a line,
    /// overtyping, retyping a word), because then no block can have moved to another page. Any page
    /// whose height cannot be measured (no layout yet) forces the full re-pagination.
    /// </summary>
    internal void CommitAndRepaginateIfLayoutChanged()
    {
        if (_editedSinceRepagination.Count > 0 && _editedSinceRepagination.All(ContentBottomUnchanged))
        {
            _repaginateTimer?.Stop();
            _editedSinceRepagination.Clear();
            PaginatedCommitCoordinator.Commit(this, _sourceEditor);
            return;
        }

        Repaginate();
    }

    private bool ContentBottomUnchanged(PageBox box) =>
        _contentBottoms.TryGetValue(box, out var baseline)
        && MeasureContentBottom(box) is { } current
        && Math.Abs(current - baseline) < 0.5;

    /// <summary>Bottom of the page body's last line, in the body's coordinates; null without layout.</summary>
    internal static double? MeasureContentBottom(PageBox box)
    {
        try
        {
            var end = box.Body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
            var rect = end?.GetCharacterRect(LogicalDirection.Backward) ?? Rect.Empty;
            return rect.IsEmpty || double.IsNaN(rect.Bottom) || double.IsInfinity(rect.Bottom) ? null : rect.Bottom;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Records each page's laid-out content height once layout has run (after a build/rebuild).</summary>
    private void ScheduleContentBottomCapture()
    {
        _contentBottoms.Clear();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(CaptureContentBottoms));
    }

    internal void CaptureContentBottoms()
    {
        _contentBottoms.Clear();
        foreach (var box in _pageBoxes)
        {
            if (MeasureContentBottom(box) is { } bottom)
                _contentBottoms[box] = bottom;
        }
    }

    /// <summary>
    /// Commits all page boxes, re-runs the pagination engine, re-shards, and rebuilds the page
    /// stack. The caret is tracked as a document position (top-level block + offset inside it), not
    /// as a page slot, so when typing pushes the caret's paragraph onto the next page -- or Backspace
    /// pulls it back -- the caret follows the text to whichever page now holds it.
    ///
    /// <para>Must run on the UI/STA thread (called from the DispatcherTimer Tick).</para>
    /// </summary>
    internal void Repaginate()
    {
        _repaginateTimer?.Stop();
        _editedSinceRepagination.Clear();
        var caret = CaptureCaret();

        // ── Commit current page boxes into the model ──────────────────────────────────────────────
        PaginatedCommitCoordinator.Commit(this, _sourceEditor);

        // Only this panel changed the model since the last layout (whole-block replacements), so the
        // layout session re-paginates from the edit and unchanged pages keep their boxes.
        RebuildBoxesFromModel(reuseUnchangedPages: true);
        RestoreCaret(caret);
    }

    // ── Phase 3b-2: cross-page selection accessor ────────────────────────────────────────────────

    /// <summary>The panel-level cross-page selection model.</summary>
    internal CrossPageSelection CrossPageSelection => _crossPageSelection;

    /// <summary>The panel-level cross-page undo coordinator.</summary>
    internal CrossPageUndoCoordinator UndoCoordinator => _undoCoordinator;

    // ── full rebuild (undo/redo, external model changes) ─────────────────────────────────────────

    /// <summary>
    /// Re-shards the panel from the <see cref="_sourceEditor"/>'s current model state, rebuilding
    /// all page boxes, without committing them first (the model is authoritative here: an undo/redo
    /// or an external command has just changed it). The caret moves to the first page.
    /// </summary>
    internal void Rebuild() => Rebuild(caret: null);

    /// <summary>
    /// <see cref="Rebuild()"/>, restoring the caret to <paramref name="caret"/> (clamped to the
    /// rebuilt document) when one is given.
    /// </summary>
    internal void Rebuild(PagedCaretPosition? caret)
    {
        _repaginateTimer?.Stop();
        _hasPendingBodyEdits = false;
        _editedSinceCommit.Clear();
        // The model may have changed in ways the layout session cannot see (an undo of another
        // command, a ribbon command, a reload): lay out and build everything afresh.
        _layout.Invalidate();
        RebuildBoxesFromModel(reuseUnchangedPages: false);
        if (caret is not null)
            RestoreCaret(caret);
        else if (_pageBoxes.Count > 0)
        {
            _pageBoxes[0].Body.Focus();
            _lastCaretBox = _pageBoxes[0];
        }
    }

    private void RebuildBoxesFromModel(bool reuseUnchangedPages)
    {
        var model = _sourceEditor.Model;
        int[] assignment;
        int pageCount;
        try
        {
            assignment = _layout.ComputeAssignment(out var totalPageCount);
            pageCount = assignment.Length > 0 ? assignment.Max() + 1 : 1;
            pageCount = Math.Max(1, Math.Max(pageCount, totalPageCount));
        }
        catch
        {
            assignment = new int[model.Blocks.Count];
            pageCount = 1;
        }

        if (PlanPages(model, assignment, pageCount) is not { } plan || _layoutMode == PaginatedPageLayoutMode.TwoColumnGrid)
        {
            RebuildBoxesFromModelFull(assignment, pageCount);
            return;
        }

        _rebuilding = true;
        try
        {
            _undoCoordinator.OnRepaginationStarting();
            _crossPageSelection.Clear(_pageBoxes);
            _dragPending = false;
            _dragActive = false;
            _dragSourceBox = null;
            _lastCaretBox = null;
            _sourceModel = model;

            var hasEndnotes = model.Endnotes.Count > 0;
            var endnoteIds = model.Endnotes.Keys.OrderBy(k => k).ToList();
            var requiresDedicatedEndnotePage = hasEndnotes && RequiresDedicatedEndnotePage(_sourceEditor);
            var totalBoxCount = requiresDedicatedEndnotePage ? pageCount + 1 : pageCount;
            var pageToSection = HeaderFooterPagePlanner.MapPagesToSections(model, assignment, pageCount);
            var pageNumberDisplay = PageNumberFormatDialogPlanner.BuildDisplayPlans(pageToSection, model, assignment);
            var differentOddEvenPages = HeaderFooterPagePlanner.UsesDifferentOddEvenPages(model);

            var slots = new HeaderFooterSlotPlan[pageCount];
            var keys = new string[pageCount];
            for (var i = 0; i < pageCount; i++)
            {
                var pageSection = pageToSection[i];
                slots[i] = HeaderFooterPagePlanner.ResolveSlots(
                    pageSection.HeadersFooters,
                    pageSection.SectionRelativePageNumber,
                    pageSection.PageSettings,
                    differentOddEvenPages,
                    pageNumberDisplay[i].LogicalPageNumber);
                var sectionPageCount = pageSection.SectionPageCount
                    + (requiresDedicatedEndnotePage && pageSection.SectionIndex == pageToSection[^1].SectionIndex ? 1 : 0);
                keys[i] = BoxKey(
                    i, pageSection, slots[i], pageNumberDisplay[i].Text, totalBoxCount, sectionPageCount, model,
                    plan.Ranges[i], hasEndnotes && !requiresDedicatedEndnotePage && i == pageCount - 1);
            }

            var oldBoxes = _pageBoxes.ToList();
            bool CanKeep(int page)
            {
                if (!reuseUnchangedPages || !_boxRangesValid || _headerFooterEditedSinceRebuild || page >= oldBoxes.Count)
                    return false;
                var old = oldBoxes[page];
                var range = plan.Ranges[page];
                if (old.ModelKey != keys[page] || old.ModelStart != range.Start
                    || old.ModelBlocks is not { } shown || shown.Length != range.Count)
                    return false;
                for (var k = 0; k < shown.Length; k++)
                {
                    if (!ReferenceEquals(shown[k], model.Blocks[range.Start + k]))
                        return false;
                }
                return true;
            }

            var keep = new bool[pageCount];
            for (var i = 0; i < pageCount; i++)
                keep[i] = CanKeep(i);

            var shards = RenderPages(model, plan, page => !keep[page]);
            var newBoxes = new List<PageBox>(totalBoxCount);
            for (var i = 0; i < pageCount; i++)
            {
                if (keep[i])
                {
                    newBoxes.Add(oldBoxes[i]);
                    continue;
                }

                var pageSection = pageToSection[i];
                var (fnIds, _) = DocumentView.CollectNoteIds(shards[i]);
                var box = new PageBox(i + 1, pageSection.PageSettings, shards[i],
                    sourceModel: model,
                    headerSlot: slots[i].Header, headerSlotKind: slots[i].HeaderSlot,
                    footerSlot: slots[i].Footer, footerSlotKind: slots[i].FooterSlot,
                    pageCount: totalBoxCount,
                    pageNumberText: pageNumberDisplay[i].Text,
                    sectionOrdinal: pageSection.SectionIndex + 1,
                    sectionPageCount: pageSection.SectionPageCount
                        + (requiresDedicatedEndnotePage
                            && pageSection.SectionIndex == pageToSection[^1].SectionIndex ? 1 : 0),
                    footnoteIds: fnIds,
                    endnoteIds: hasEndnotes && !requiresDedicatedEndnotePage && i == pageCount - 1
                        ? endnoteIds
                        : null)
                {
                    ModelKey = keys[i],
                    ModelStart = plan.Ranges[i].Start,
                    ModelBlocks = model.Blocks.Skip(plan.Ranges[i].Start).Take(plan.Ranges[i].Count).ToArray(),
                };
                box.ContentControlLockProbe = _sourceEditor.TryEvaluateContentControlLock;
                box.OwnerSectionHf = ResolveOwnerSectionHf(model, pageSection.SectionIndex, slots[i].HeaderSlot);
                box.FooterOwnerSectionHf = ResolveOwnerSectionHf(model, pageSection.SectionIndex, slots[i].FooterSlot);
                newBoxes.Add(box);
            }

            if (requiresDedicatedEndnotePage)
            {
                var endnotePage = BuildDedicatedEndnotePage(
                    model, pageCount, assignment, pageToSection, differentOddEvenPages, endnoteIds);
                endnotePage.ModelStart = model.Blocks.Count;
                endnotePage.ModelBlocks = [];
                newBoxes.Add(endnotePage);
            }

            // Retire the boxes that were replaced; keep the others where they are in the page host.
            foreach (var old in oldBoxes)
            {
                if (newBoxes.Contains(old))
                    continue;
                UnhookTextChanged(old);
                _undoCoordinator.UnhookBox(old);
                UnhookShiftArrow(old);
                UnhookDragDrop(old);
                _contentBottoms.Remove(old);
            }

            var children = _pageHost.Children;
            for (var i = 0; i < newBoxes.Count; i++)
            {
                if (i < children.Count && ReferenceEquals(children[i], newBoxes[i]))
                    continue;
                if (i < children.Count)
                    children.RemoveAt(i);
                children.Insert(i, newBoxes[i]);
            }
            while (children.Count > newBoxes.Count)
                children.RemoveAt(children.Count - 1);

            _pageBoxes.Clear();
            _pageBoxes.AddRange(newBoxes);
            var kept = 0;
            foreach (var box in newBoxes)
            {
                if (oldBoxes.Contains(box))
                {
                    kept++;
                    continue;
                }
                HookTextChanged(box);
                HookShiftArrow(box);
                HookDragDrop(box);
                _undoCoordinator.HookBox(box);
            }
            LastRebuildKeptBoxes = kept;

            for (var i = 0; i < _pageBoxes.Count; i++)
            {
                _pageBoxes[i].PreviousBox = i > 0 ? _pageBoxes[i - 1] : null;
                _pageBoxes[i].NextBox = i < _pageBoxes.Count - 1 ? _pageBoxes[i + 1] : null;
            }

            _boxRangesValid = true;
            _headerFooterEditedSinceRebuild = false;
            _editedSinceRepagination.Clear();
            ScheduleContentBottomCapture();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void RebuildBoxesFromModelFull(int[] assignment, int pageCount)
    {
        _boxRangesValid = false;
        _rebuilding = true;
        try
        {
            _undoCoordinator.OnRepaginationStarting();

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
            _lastCaretBox = null;

            ClearPageHost();
            _pageBoxes.Clear();

            var model = _sourceEditor.Model;
            _sourceModel = model;

            var plan = PlanPages(model, assignment, pageCount);
            IReadOnlyList<IReadOnlyList<System.Windows.Documents.Block>> shards;
            if (plan is not null)
            {
                shards = RenderPages(model, plan, _ => true);
            }
            else
            {
                var scratch = new DocumentView();
                scratch.LoadModel(model);
                var allBlocks = scratch.Document.Blocks.ToList();
                scratch.Document.Blocks.Clear();
                shards = ShardByPageAssignment(allBlocks, assignment, pageCount);
            }

            var pageFootnoteIds = new IReadOnlyList<int>[pageCount];
            for (var i = 0; i < pageCount; i++)
            {
                var (fnIds, _) = DocumentView.CollectNoteIds(shards[i]);
                pageFootnoteIds[i] = fnIds;
            }

            var hasEndnotes = model.Endnotes.Count > 0;
            var endnoteIds = model.Endnotes.Keys.OrderBy(k => k).ToList();
            var requiresDedicatedEndnotePage = hasEndnotes && RequiresDedicatedEndnotePage(_sourceEditor);
            var totalBoxCount = requiresDedicatedEndnotePage ? pageCount + 1 : pageCount;
            var pageToSection = HeaderFooterPagePlanner.MapPagesToSections(model, assignment, pageCount);
            var pageNumberDisplay = PageNumberFormatDialogPlanner.BuildDisplayPlans(pageToSection, model, assignment);
            var differentOddEvenPages = HeaderFooterPagePlanner.UsesDifferentOddEvenPages(model);
            for (var i = 0; i < pageCount; i++)
            {
                // SG: per-section page geometry.
                var pageSection = pageToSection[i];
                var slots = HeaderFooterPagePlanner.ResolveSlots(
                    pageSection.HeadersFooters,
                    pageSection.SectionRelativePageNumber,
                    pageSection.PageSettings,
                    differentOddEvenPages,
                    pageNumberDisplay[i].LogicalPageNumber);
                var box = new PageBox(i + 1, pageSection.PageSettings, shards[i],
                    sourceModel: model,
                    headerSlot: slots.Header, headerSlotKind: slots.HeaderSlot,
                    footerSlot: slots.Footer, footerSlotKind: slots.FooterSlot,
                    pageCount: totalBoxCount,
                    pageNumberText: pageNumberDisplay[i].Text,
                    sectionOrdinal: pageSection.SectionIndex + 1,
                    sectionPageCount: pageSection.SectionPageCount
                        + (requiresDedicatedEndnotePage
                            && pageSection.SectionIndex == pageToSection[^1].SectionIndex ? 1 : 0),
                    footnoteIds: pageFootnoteIds[i],
                    endnoteIds: hasEndnotes && !requiresDedicatedEndnotePage && i == pageCount - 1
                        ? endnoteIds
                        : null);
                // freew-cc-3: see the matching comment in Build() above.
                box.ContentControlLockProbe = _sourceEditor.TryEvaluateContentControlLock;
                // W21: section-aware commit -- owner resolved per slot (see Build's comment above).
                box.OwnerSectionHf = ResolveOwnerSectionHf(model, pageSection.SectionIndex, slots.HeaderSlot);
                box.FooterOwnerSectionHf = ResolveOwnerSectionHf(model, pageSection.SectionIndex, slots.FooterSlot);
                AddRebuiltBox(box);
            }

            if (requiresDedicatedEndnotePage)
            {
                AddRebuiltBox(BuildDedicatedEndnotePage(
                    model, pageCount, assignment, pageToSection,
                    differentOddEvenPages, endnoteIds));
            }

            // Wire neighbour links for cross-page caret routing.
            for (var i = 0; i < _pageBoxes.Count; i++)
            {
                _pageBoxes[i].PreviousBox = i > 0 ? _pageBoxes[i - 1] : null;
                _pageBoxes[i].NextBox = i < _pageBoxes.Count - 1 ? _pageBoxes[i + 1] : null;
            }

            _undoCoordinator.ReAttach(_pageBoxes);
            _editedSinceRepagination.Clear();
            if (plan is not null)
            {
                var ranges = plan.Ranges.ToList();
                if (requiresDedicatedEndnotePage)
                    ranges.Add((model.Blocks.Count, 0));
                SetBoxModelRanges(ranges, model);
            }
            ScheduleContentBottomCapture();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void AddRebuiltBox(PageBox box)
    {
        _pageBoxes.Add(box);
        AddPageBoxToHost(box);
        HookTextChanged(box);
        HookShiftArrow(box);
        HookDragDrop(box);
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
        box.Body.LostMouseCapture           += OnBodyLostMouseCapture;
    }

    private void UnhookDragDrop(PageBox box)
    {
        box.Body.PreviewMouseLeftButtonDown -= OnBodyMouseDown;
        box.Body.PreviewMouseMove           -= OnBodyMouseMove;
        box.Body.PreviewMouseLeftButtonUp   -= OnBodyMouseUp;
        box.Body.LostMouseCapture           -= OnBodyLostMouseCapture;

        // Defensive: page boxes are about to be discarded (repagination). If this box's Body
        // still holds mouse capture from an in-flight drag, release it now -- otherwise capture
        // would remain pinned to a Body that's leaving the visual tree, which can swallow ALL
        // future mouse input app-wide, not just text selection.
        if (ReferenceEquals(Mouse.Captured, box.Body))
        {
            box.Body.ReleaseMouseCapture();
        }
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
        BeginActiveDrag();

        // Suppress the native RichTextBox mouse-move so it doesn't change the selection.
        e.Handled = true;
    }

    /// <summary>
    /// Transitions from a pending drag (mouse-down detected inside the cross-page selection) to
    /// an active one (drag threshold exceeded).
    ///
    /// <para>
    /// Captures the mouse to the drag's source Body so every subsequent Preview mouse event for
    /// this gesture keeps reaching this panel's handlers even once the pointer leaves the source
    /// box's bounds -- e.g. crossing the gray inter-page gap on its way to a different page, or
    /// straying over the scrollbar. Without this capture, none of the Preview handlers (including
    /// <see cref="OnBodyMouseUp"/>) fire again for this gesture once the pointer leaves the
    /// source Body, so releasing the mouse there leaves <c>_dragActive</c> stuck true and native
    /// click-and-drag text selection permanently suppressed until the next repagination.
    /// </para>
    /// </summary>
    private void BeginActiveDrag()
    {
        _dragPending = false;
        _dragActive = true;
        _dragSourceBox?.Body.CaptureMouse();
    }

    /// <summary>
    /// Fires if mouse capture is lost mid-drag for any reason other than the normal MouseUp path
    /// (e.g. the window is deactivated, or another control forcibly steals capture). Defensive
    /// reset so <c>_dragActive</c> can never get stuck true — the same failure mode this whole
    /// capture mechanism exists to prevent.
    /// </summary>
    private void OnBodyLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_dragActive || _dragPending)
        {
            _dragActive = false;
            _dragPending = false;
            _dragSourceBox = null;
        }
    }

    private void OnBodyMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragActive)
        {
            // Not dragging — just clear pending state.
            _dragPending = false;
            return;
        }

        EndActiveDrag();

        // Determine whether this is a copy (Ctrl held) or move.
        bool isCopy = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        // Resolve the drop target and perform the move/copy. Because the mouse is captured to the
        // drag's source Body for the whole gesture, "sender" here is ALWAYS that source Body --
        // even when the pointer is physically over a different page -- so the drop target must be
        // found by hit-testing the real pointer position against every page box's bounds, not by
        // trusting sender.
        if (CompleteDrag(e.GetPosition(_pageHost), isCopy))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Terminates the active drag's state machine unconditionally, before the drop target is even
    /// resolved -- so the drag can never be left stuck active regardless of where (or whether) it
    /// actually drops. Releases the mouse capture <see cref="BeginActiveDrag"/> took, restoring
    /// ordinary click-and-drag text selection everywhere.
    /// </summary>
    private void EndActiveDrag()
    {
        _dragActive = false;
        _dragPending = false;
        _dragSourceBox?.Body.ReleaseMouseCapture();
        _dragSourceBox = null;
    }

    /// <summary>
    /// Finishes an in-flight cross-page drag once the mouse button is released: resolves the page
    /// box under <paramref name="panelPoint"/> (in <c>_pageHost</c> coordinates), and performs the
    /// move/copy. Returns true when the caller's routed event should be marked handled.
    ///
    /// <para>
    /// Deliberately takes a plain <see cref="Point"/> rather than the routed
    /// <see cref="MouseButtonEventArgs"/> so the drop-target resolution and move/copy logic can be
    /// unit-tested with a real point in panel coordinates, without needing a live WPF mouse device
    /// to synthesize <c>MouseButtonEventArgs.GetPosition</c> — the same headless-test constraint
    /// documented on <c>PagedEditCrossPageDragMoveAtomicityTests</c>.
    /// </para>
    /// </summary>
    internal bool CompleteDrag(Point panelPoint, bool isCopy)
    {
        // Find the box under the mouse for the drop target.
        var dropBox = FindPageBoxAtPoint(panelPoint);
        if (dropBox is null)
        {
            // Released outside every page box (e.g. the inter-page gap, a scrollbar, or the
            // margin) — cancel the drop, same as the pre-existing "outside a known box" behaviour.
            return false;
        }

        // Compute the drop TextPointer at the mouse-up position, translated into the drop box's
        // own Body-local coordinate space.
        Point dropPoint;
        try
        {
            var bodyOrigin = dropBox.Body.TranslatePoint(new Point(0, 0), _pageHost);
            dropPoint = new Point(panelPoint.X - bodyOrigin.X, panelPoint.Y - bodyOrigin.Y);
        }
        catch
        {
            return false;
        }

        TextPointer? dropPtr = GetTextPointerAtPoint(dropBox, dropPoint);
        if (dropPtr is null)
            return false;

        // Determine the drop box index.
        int dropBoxIdx = CrossPageSelection.IndexOfBox(_pageBoxes, dropBox);
        if (dropBoxIdx < 0)
            return false;

        // No-op: drop inside the selection range.
        if (IsDropInsideSelection(dropBoxIdx, dropPtr))
            return false;

        // Obtain the text content of the cross-page selection.
        var selectedText = _crossPageSelection.GetSelectedText(_pageBoxes);
        if (selectedText.Length == 0)
            return false;

        // Snapshot the drop pointer before a move-cut so the TextPointer remains valid.
        // WPF TextPointer objects track the document tree; after DeleteCrossPageSelection
        // removes content from different boxes the pointer in the target box (a different box)
        // stays valid.  dropPtr is held as dropPtrRef through the cut.
        TextPointer dropPtrRef = dropPtr;

        // freew-cc-3 / shared-drag-drop F1: refuse to drop onto a locked content control. The
        // InsertTextInRun call below mutates the live FlowDocument directly with no lock awareness
        // of its own -- exactly like PaginatedEditorPanel.PasteAtCaret's own caret insert, which
        // already gates on this same probe before inserting (see its guard for the reasoning). Checked
        // before either branch below runs so a locked drop target aborts the whole gesture without
        // cutting the source selection first.
        if (dropBox.ContentControlLockProbe?.Invoke(dropPtrRef) == false)
        {
            return true; // handled: the drag gesture is consumed, nothing is inserted or removed.
        }

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
                return true;
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
        return true;
    }

    /// <summary>
    /// Returns the page box whose Body contains <paramref name="panelPoint"/> (in
    /// <c>_pageHost</c> coordinates), or null when the point falls outside every page's Body —
    /// e.g. in the inter-page gap, a margin, or the scrollbar. Used to resolve the real drop
    /// target for a cross-page drag release, since mouse capture makes the routed event's
    /// "sender" always the drag's source box regardless of where the pointer actually is.
    /// </summary>
    private PageBox? FindPageBoxAtPoint(Point panelPoint)
    {
        foreach (var box in _pageBoxes)
        {
            try
            {
                var origin = box.Body.TranslatePoint(new Point(0, 0), _pageHost);
                if (panelPoint.X >= origin.X && panelPoint.X <= origin.X + box.Body.ActualWidth &&
                    panelPoint.Y >= origin.Y && panelPoint.Y <= origin.Y + box.Body.ActualHeight)
                {
                    return box;
                }
            }
            catch
            {
                // Not connected to the panel's visual tree right now (e.g. mid-repagination) —
                // skip it.
            }
        }
        return null;
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

    // ── W9: page plans by render unit ─────────────────────────────────────────────────────────────

    /// <summary>Which render units (and so which model blocks) each body page shows.</summary>
    internal sealed record PagePlan(
        PagedLayoutSession.UnitPlan[] Units,
        int[] UnitPage,
        IReadOnlyList<(int Start, int Count)> Ranges);

    /// <summary>
    /// Plans the body pages by render unit: a unit (one block, or a run of list paragraphs rendered as one
    /// WPF List) goes to the page of its first model block. Null when the model cannot be planned this
    /// way (the caller shards a full render instead).
    /// </summary>
    internal static PagePlan? PlanPages(TextDocument model, int[] assignment, int pageCount)
    {
        PagedLayoutSession.UnitPlan[] units;
        try
        {
            units = PagedLayoutSession.BuildUnitPlans(model);
        }
        catch (PagedLayoutSession.UnsupportedException)
        {
            return null;
        }

        pageCount = Math.Max(1, pageCount);
        var unitPage = new int[units.Length];
        for (var u = 0; u < units.Length; u++)
        {
            unitPage[u] = Math.Clamp(units[u].Start < assignment.Length ? assignment[units[u].Start] : 0, 0, pageCount - 1);
            if (u > 0 && unitPage[u] < unitPage[u - 1])
                return null;
        }

        var ranges = new (int Start, int Count)[pageCount];
        var next = 0;
        var unit = 0;
        for (var page = 0; page < pageCount; page++)
        {
            var start = next;
            while (unit < units.Length && unitPage[unit] == page)
            {
                next = units[unit].End;
                unit++;
            }
            ranges[page] = (start, next - start);
        }

        return new PagePlan(units, unitPage, ranges);
    }

    /// <summary>
    /// Renders the editable (Tag-bearing) blocks of the pages <paramref name="renderPage"/> selects, with
    /// the same context and contextual spacing a full render gives them; other pages get no blocks.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<System.Windows.Documents.Block>> RenderPages(
        TextDocument model,
        PagePlan plan,
        Func<int, bool> renderPage)
    {
        var shards = new List<List<System.Windows.Documents.Block>>(plan.Ranges.Count);
        for (var i = 0; i < plan.Ranges.Count; i++)
            shards.Add([]);

        var units = plan.Units;
        DocumentView? scratch = null;
        var u = 0;
        while (u < units.Length)
        {
            if (!renderPage(plan.UnitPage[u]))
            {
                u++;
                continue;
            }

            var end = u + 1;
            while (end < units.Length && renderPage(plan.UnitPage[end]))
                end++;

            if (scratch is null)
            {
                scratch = new DocumentView();
                scratch.BindModelForRangeRender(model);
            }
            var rendered = scratch.RenderBodyUnitRange(units[u].Start, units[end - 1].End);
            for (var k = 0; k < rendered.Count && u + k < end; k++)
            {
                var renderedUnit = rendered[k];
                var unitIndex = u + k;
                if (renderedUnit.Kind == DocumentView.BodyRenderUnitKind.BodyParagraph
                    && renderedUnit.Blocks[0] is System.Windows.Documents.Paragraph paragraph)
                {
                    var margin = paragraph.Margin;
                    var top = PreviousBodyParagraph(units, unitIndex) is { } before
                        && DocumentView.SuppressesContextualSpacingForLayout(before, renderedUnit.BodyParagraph!, model)
                        ? 0
                        : margin.Top;
                    var bottom = NextBodyParagraph(units, unitIndex) is { } after
                        && DocumentView.SuppressesContextualSpacingForLayout(renderedUnit.BodyParagraph!, after, model)
                        ? 0
                        : margin.Bottom;
                    paragraph.Margin = new Thickness(margin.Left, top, margin.Right, bottom);
                }
                shards[plan.UnitPage[unitIndex]].AddRange(renderedUnit.Blocks);
            }
            u = end;
        }

        return shards;
    }

    private static FreeW.Core.Model.Paragraph? PreviousBodyParagraph(PagedLayoutSession.UnitPlan[] units, int index)
    {
        for (var u = index - 1; u >= 0; u--)
        {
            switch (units[u].Kind)
            {
                case DocumentView.BodyRenderUnitKind.Transparent:
                    continue;
                case DocumentView.BodyRenderUnitKind.BodyParagraph:
                    return (FreeW.Core.Model.Paragraph)units[u].Blocks[0];
                default:
                    return null;
            }
        }
        return null;
    }

    private static FreeW.Core.Model.Paragraph? NextBodyParagraph(PagedLayoutSession.UnitPlan[] units, int index)
    {
        for (var u = index + 1; u < units.Length; u++)
        {
            switch (units[u].Kind)
            {
                case DocumentView.BodyRenderUnitKind.Transparent:
                    continue;
                case DocumentView.BodyRenderUnitKind.BodyParagraph:
                    return (FreeW.Core.Model.Paragraph)units[u].Blocks[0];
                default:
                    return null;
            }
        }
        return null;
    }

    /// <summary>The page facts a body page box is built from; equal keys and blocks mean the box can stay.</summary>
    private static string BoxKey(
        int physicalPageIndex,
        HeaderFooterPageSectionPlan pageSection,
        HeaderFooterSlotPlan slots,
        string? pageNumberText,
        int totalBoxCount,
        int sectionPageCount,
        TextDocument model,
        (int Start, int Count) range,
        bool carriesEndnotes)
    {
        var footnoteIds = new List<int>();
        for (var m = range.Start; m < range.Start + range.Count && m < model.Blocks.Count; m++)
        {
            foreach (var id in PaginationEngine.FootnoteIdsInModelBlock(model.Blocks[m]))
            {
                if (!footnoteIds.Contains(id))
                    footnoteIds.Add(id);
            }
        }

        return string.Join(
            "|",
            physicalPageIndex,
            pageSection.SectionIndex,
            pageSection.SectionRelativePageNumber,
            // Page settings and header/footer content only change through commands the panel rebuilds
            // everything for (or through an in-page header/footer edit, which also disables reuse).
            slots.Header is null,
            slots.HeaderSlot,
            slots.Footer is null,
            slots.FooterSlot,
            pageNumberText,
            totalBoxCount,
            sectionPageCount,
            string.Join(",", footnoteIds),
            carriesEndnotes);
    }

    /// <summary>Records which model blocks each box shows (aligned with <see cref="PageBoxes"/>).</summary>
    private void SetBoxModelRanges(IReadOnlyList<(int Start, int Count)> ranges, TextDocument model)
    {
        if (ranges.Count != _pageBoxes.Count)
        {
            _boxRangesValid = false;
            return;
        }

        for (var i = 0; i < ranges.Count; i++)
        {
            _pageBoxes[i].ModelStart = ranges[i].Start;
            _pageBoxes[i].ModelBlocks = model.Blocks.Skip(ranges[i].Start).Take(ranges[i].Count).ToArray();
        }
        _boxRangesValid = true;
    }

    /// <summary>
    /// The contiguous run of page boxes edited since the last commit and the model blocks they show, when
    /// that is known exactly: a commit then replaces only those blocks.
    /// </summary>
    internal bool TryGetEditedSpan(out int firstBox, out int lastBox)
    {
        firstBox = lastBox = -1;
        if (!_boxRangesValid || _editedSinceCommit.Count == 0 || !ReferenceEquals(_sourceModel, _sourceEditor.Model))
            return false;

        for (var i = 0; i < _pageBoxes.Count; i++)
        {
            if (!_editedSinceCommit.Contains(_pageBoxes[i]))
                continue;
            if (firstBox < 0)
                firstBox = i;
            lastBox = i;
        }
        if (firstBox < 0 || _editedSinceCommit.Any(box => !_pageBoxes.Contains(box)))
            return false;

        // The model must still hold exactly what the boxes were built from.
        var blocks = _sourceEditor.Model.Blocks;
        var expectedStart = 0;
        foreach (var box in _pageBoxes)
        {
            if (box.ModelBlocks is null || box.ModelStart != expectedStart)
                return false;
            expectedStart += box.ModelBlocks.Length;
        }
        if (expectedStart != blocks.Count)
            return false;
        for (var i = firstBox; i <= lastBox; i++)
        {
            var box = _pageBoxes[i];
            for (var k = 0; k < box.ModelBlocks!.Length; k++)
            {
                if (!ReferenceEquals(blocks[box.ModelStart + k], box.ModelBlocks[k]))
                    return false;
            }
        }
        return true;
    }

    /// <summary>After a span commit: the span's boxes now show <paramref name="perBox"/>; later boxes shift.</summary>
    internal void OnSpanCommitted(int firstBox, int lastBox, IReadOnlyList<FreeW.Core.Model.Block[]> perBox)
    {
        var position = _pageBoxes[firstBox].ModelStart;
        for (var i = firstBox; i < _pageBoxes.Count; i++)
        {
            var box = _pageBoxes[i];
            box.ModelStart = position;
            if (i <= lastBox)
                box.ModelBlocks = perBox[i - firstBox];
            position += box.ModelBlocks!.Length;
        }
        _editedSinceCommit.Clear();
    }

    /// <summary>A whole-document commit replaced every block: what the boxes show is no longer known.</summary>
    internal void OnWholeBodyCommitted()
    {
        _editedSinceCommit.Clear();
        _boxRangesValid = false;
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
