using System.Windows;
using System.Windows.Controls;
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
        }

        Background = WorkspaceBrush;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Content = _stack;
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
        var boxes = new List<PageBox>(pageCount);
        for (var i = 0; i < pageCount; i++)
            boxes.Add(new PageBox(i + 1, page, shards[i]));

        // Wire neighbour links for cross-page caret routing.
        for (var i = 0; i < boxes.Count; i++)
        {
            boxes[i].PreviousBox = i > 0 ? boxes[i - 1] : null;
            boxes[i].NextBox = i < boxes.Count - 1 ? boxes[i + 1] : null;
        }

        return new PaginatedEditorPanel(sourceEditor, boxes);
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

    private void OnAnyPageBodyTextChanged(object? sender, System.Windows.Controls.TextChangedEventArgs e)
        => ScheduleRepaginate();

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
        // Unhook TextChanged from old boxes before discarding them.
        foreach (var old in _pageBoxes)
            UnhookTextChanged(old);

        _stack.Children.Clear();
        _pageBoxes.Clear();

        for (var i = 0; i < pageCount; i++)
        {
            var box = new PageBox(i + 1, page, shards[i]);
            _pageBoxes.Add(box);
            _stack.Children.Add(box);
            HookTextChanged(box);
        }

        // Wire neighbour links for cross-page caret routing.
        for (var i = 0; i < _pageBoxes.Count; i++)
        {
            _pageBoxes[i].PreviousBox = i > 0 ? _pageBoxes[i - 1] : null;
            _pageBoxes[i].NextBox = i < _pageBoxes.Count - 1 ? _pageBoxes[i + 1] : null;
        }

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
