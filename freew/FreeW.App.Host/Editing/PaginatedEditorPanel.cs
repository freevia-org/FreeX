using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
/// Cross-page caret routing, cross-page selection, live re-pagination on edit, and shared undo are
/// deferred to Phase 3b.  Within a single page box, normal RichTextBox editing works today.
/// </para>
/// </summary>
internal sealed class PaginatedEditorPanel : ScrollViewer
{
    // ── workspace background (same grey "desk" as the main editor) ────────────────────────────────
    private static readonly Brush WorkspaceBrush =
        new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));

    // ── public state ──────────────────────────────────────────────────────────────────────────────
    /// <summary>Ordered list of page boxes, one per page.</summary>
    internal IReadOnlyList<PageBox> PageBoxes { get; }

    // ── construction ─────────────────────────────────────────────────────────────────────────────

    private PaginatedEditorPanel(IReadOnlyList<PageBox> boxes)
    {
        PageBoxes = boxes;

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        foreach (var box in boxes)
            stack.Children.Add(box);
        // Add a trailing gap after the last page for visual breathing room.
        stack.Margin = new Thickness(0, 0, 0, 20);

        Background = WorkspaceBrush;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Content = stack;
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
    ///   <item>Calls <see cref="PaginationEngine.Compute"/> on the <paramref name="sourceEditor"/>
    ///   to determine how many pages there are.</item>
    ///   <item>Shards the scratch FlowDocument's top-level Block list into per-page sets using
    ///   the break Ys.  For Phase 3a the sharding uses an even distribution (one chunk per page)
    ///   because reading the per-block Y in an off-screen editor is unreliable without layout;
    ///   <em>blocks are never split across pages</em>.  Live re-pagination on edit is deferred to
    ///   Phase 3b.</item>
    ///   <item>Moves each page's blocks into its <see cref="PageBox"/> body FlowDocument.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal static PaginatedEditorPanel Build(DocumentView sourceEditor)
    {
        // The source editor must be committed before we read its model.
        var model = sourceEditor.Model;
        var page = model.Page;

        // ── Step 1: render the model into a scratch editor to obtain Tag-bearing WPF blocks ──────
        // We use a scratch DocumentView (not the live editor) so we can steal its blocks without
        // disturbing the live editing surface.
        var scratch = new DocumentView();
        scratch.LoadModel(model);

        // Collect all top-level WPF blocks in document order.  We detach them from the scratch
        // FlowDocument before moving them into per-page bodies; a Block can only belong to one
        // FlowDocument at a time.
        var allBlocks = scratch.Document.Blocks.ToList();
        scratch.Document.Blocks.Clear(); // detach so they can be re-parented

        // ── Step 2: determine page count ─────────────────────────────────────────────────────────
        // Use PaginationEngine on the source editor (which has a valid layout) to get the real
        // page count.  If pagination is not available fall back to 1.
        int pageCount;
        try
        {
            var pagination = PaginationEngine.Compute(sourceEditor);
            pageCount = Math.Max(1, pagination.PageCount);
        }
        catch
        {
            pageCount = 1;
        }

        // ── Step 3: shard blocks across pages ────────────────────────────────────────────────────
        // Phase 3a sharding: distribute the top-level block list evenly across page slots.  This
        // avoids requiring a laid-out off-screen tree (which would need the scratch editor to be
        // in a visible window).  Live re-pagination per-block boundary is deferred to Phase 3b.
        var shards = ShardBlocks(allBlocks, pageCount);

        // ── Step 4: create one PageBox per page ───────────────────────────────────────────────────
        var boxes = new List<PageBox>(pageCount);
        for (var i = 0; i < pageCount; i++)
            boxes.Add(new PageBox(i + 1, page, shards[i]));

        return new PaginatedEditorPanel(boxes);
    }

    // ── sharding ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Distributes <paramref name="blocks"/> across <paramref name="pageCount"/> page slots as
    /// evenly as possible (round-robin assignment).  Each slot gets at least one block when there
    /// are more blocks than pages; sparse pages (more pages than blocks) receive an empty list.
    /// An empty block set always returns one page with zero blocks (degenerate case).
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<System.Windows.Documents.Block>> ShardBlocks(
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

        // Distribute: first [blocks.Count % pageCount] pages get one extra block so every block
        // is assigned and nothing is silently dropped.
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
