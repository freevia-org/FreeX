using FreeW.Core.Model;

using ModelBlock = FreeW.Core.Model.Block;
using ModelParagraph = FreeW.Core.Model.Paragraph;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Reassembles the full model <see cref="TextDocument"/> from the per-page
/// <see cref="PageBox"/> body RichTextBoxes when the user exits
/// <see cref="DocumentViewMode.PagedEdit"/>.
///
/// <para>
/// <strong>Strategy:</strong>  The coordinator walks the <see cref="PaginatedEditorPanel.PageBoxes"/>
/// in order, calls <see cref="DocumentView.ReadBlocksInto"/> for each page box's body
/// <see cref="System.Windows.Documents.FlowDocument"/>, and concatenates the resulting model blocks
/// into the source editor's model in document order.  Because the page box bodies were built by
/// <em>moving</em> Tag-bearing WPF Block elements (not re-serialising them), the same Tag payloads
/// that the standard commit path relies on (<c>ParagraphTag</c>, <c>RunMarkers</c>,
/// <c>FootnoteMarker</c>, etc.) are still present and round-trip losslessly.
/// </para>
///
/// <para>
/// After the commit the caller should call <see cref="DocumentView.LoadModel"/> on the continuous
/// editor so it reflects the updated model.  The coordinator itself never calls Render.
/// </para>
/// </summary>
internal static class PaginatedCommitCoordinator
{
    /// <summary>Where the last span commit spent its time (with <see cref="PagedEditDiagnostics.Enabled"/>).</summary>
    internal static string LastTiming { get; private set; } = "";

    /// <summary>
    /// Reads all page boxes back into <paramref name="targetEditor"/>'s model.
    ///
    /// <list type="number">
    ///   <item>Collects model blocks from each page box body in order via
    ///   <see cref="DocumentView.ReadBlocksInto"/>.</item>
    ///   <item>Clears <c>targetEditor.Model.Blocks</c> and replaces with the collected blocks.</item>
    ///   <item>Guarantees the model is non-empty (adds an empty paragraph when all pages are
    ///   empty, mirroring <see cref="DocumentView.CommitToModel"/> behaviour).</item>
    /// </list>
    ///
    /// <para>
    /// Must be called on the UI/STA thread because it accesses WPF elements.
    /// </para>
    /// </summary>
    internal static void Commit(PaginatedEditorPanel panel, DocumentView targetEditor)
    {
        var model = targetEditor.Model;

        // The page boxes were built from one specific document. If the editor has since loaded a
        // different one (File > Open, mail-merge preview), writing these pages into it would replace
        // the new document's body with the old one's.
        if (!ReferenceEquals(panel.SourceModel, model))
            return;

        // Re-entrant call: applying the commit re-renders the source editor, whose chrome refresh
        // updates ribbon state, and several state queries commit the editor first. The model is being
        // written right now; committing again would recurse until the stack overflows.
        if (panel.IsMutatingModel)
            return;

        // ── Phase 4: commit in-page header/footer sub-editors to their model slots first ─────────
        // This mirrors CloseHeaderFooterPane in MainWindow: sub-editors → wrapper model → slot.
        // Must happen before the body-block reassembly so a subsequent Render/LoadModel picks up
        // the updated header/footer paragraphs.
        panel.CommitHeaderFooterSlots(targetEditor);

        // Nothing typed on the pages since the last commit: the model is already authoritative, and
        // re-reading untouched pages would only push an empty undo unit (and run every block through
        // the read path for no reason).
        if (!panel.HasPendingBodyEdits)
            return;

        // W9: when the panel knows which model blocks each page shows, only the pages edited since the
        // last commit are read back, and only their blocks are replaced (the source editor refreshes
        // just those blocks too). Untouched blocks keep their instances, which is what lets the layout
        // session reuse their render and their pages.
        var clock = PagedEditDiagnostics.StartClock();
        var perBox = TryReadBoundedSpan(panel, targetEditor, out var firstBox, out var lastBox);
        if (perBox is null && panel.TryGetEditedSpan(out firstBox, out lastBox))
            perBox = ReadWholeSpan(panel, targetEditor, firstBox, lastBox);
        if (perBox is not null)
        {
            var spanBlocks = perBox.SelectMany(blocks => blocks).ToList();

            var spanStart = panel.PageBoxes[firstBox].ModelStart;
            var spanCount = 0;
            for (var i = firstBox; i <= lastBox; i++)
                spanCount += panel.PageBoxes[i].ModelBlocks!.Length;

            // Never leave an empty document; the whole-body path below adds the empty paragraph.
            if (spanBlocks.Count > 0 || spanCount < model.Blocks.Count)
            {
                var read = clock?.Elapsed.TotalMilliseconds ?? 0;
                panel.MarkBodyCommitted();
                try
                {
                    // Replace only what changed: the blocks at either end that are the same instances stay.
                    var prefix = 0;
                    while (prefix < spanCount && prefix < spanBlocks.Count
                        && ReferenceEquals(model.Blocks[spanStart + prefix], spanBlocks[prefix]))
                        prefix++;
                    var suffix = 0;
                    while (suffix < spanCount - prefix && suffix < spanBlocks.Count - prefix
                        && ReferenceEquals(model.Blocks[spanStart + spanCount - 1 - suffix], spanBlocks[spanBlocks.Count - 1 - suffix]))
                        suffix++;
                    var replaceStart = spanStart + prefix;
                    var replaceCount = spanCount - prefix - suffix;
                    var replacement = spanBlocks.Skip(prefix).Take(spanBlocks.Count - prefix - suffix).ToList();
                    if (replaceCount > 0 || replacement.Count > 0)
                    {
                        targetEditor.HintNextBodyReplacement(replaceStart, replaceCount, replacement.Count);
                        panel.RunOwnModelMutation(() =>
                            targetEditor.Commands.Execute(new ReplaceBlocksCommand(replaceStart, replaceCount, replacement)));
                    }
                }
                catch
                {
                    panel.MarkBodyPending();
                    throw;
                }
                finally
                {
                    targetEditor.ClearBodyReplacementHint();
                }
                panel.OnSpanCommitted(firstBox, lastBox, perBox);
                if (clock is not null)
                    LastTiming = $"span read {read:F0} ms, execute {clock.Elapsed.TotalMilliseconds - read:F0} ms, "
                        + $"pages {firstBox}..{lastBox} of {panel.PageBoxes.Count}, {spanBlocks.Count} blocks read";
                return;
            }
        }

        var collected = new List<ModelBlock>();
        targetEditor.ReadPagesInto(
            panel.PageBoxes.Select(box => box.Body.Document).ToList(),
            collected,
            new int[panel.PageBoxes.Count]);

        // W10: blocks the background pagination has not put on a page yet are not on any page box; they
        // were not edited, so they follow the pages unchanged (the same instances).
        var tailStart = -1;
        if (panel.TailStart >= 0 && panel.TailStart <= model.Blocks.Count)
        {
            tailStart = collected.Count;
            for (var m = panel.TailStart; m < model.Blocks.Count; m++)
                collected.Add(model.Blocks[m]);
        }

        // Mirror CommitToModel: never leave an empty block list.
        if (collected.Count == 0)
            collected.Add(new ModelParagraph());

        // One undo unit per edit burst, on the same DocumentCommandBus history the continuous editor
        // uses: undo restores the exact pre-burst block instances, so the document is exactly equal
        // to what it was, whichever pages the burst touched.
        // Cleared before applying so anything the apply triggers sees a clean surface; restored if
        // the command throws so the edits are not silently dropped.
        panel.MarkBodyCommitted();
        try
        {
            panel.RunOwnModelMutation(() =>
                targetEditor.Commands.Execute(new ReplaceBlocksCommand(0, model.Blocks.Count, collected)));
        }
        catch
        {
            panel.MarkBodyPending();
            throw;
        }
        panel.OnWholeBodyCommitted(tailStart);
    }

    /// <summary>
    /// The edited span's pages read in one pass, so a block split across two of them is rejoined; each block
    /// belongs to the page it starts on. The span reaches every page a split block is on, whole.
    /// </summary>
    private static List<ModelBlock[]> ReadWholeSpan(PaginatedEditorPanel panel, DocumentView targetEditor, int firstBox, int lastBox)
    {
        var spanBlocks = new List<ModelBlock>();
        var pageStarts = new int[lastBox - firstBox + 1];
        targetEditor.ReadPagesInto(
            panel.PageBoxes.Skip(firstBox).Take(lastBox - firstBox + 1).Select(box => box.Body.Document).ToList(),
            spanBlocks,
            pageStarts);
        var perBox = new List<ModelBlock[]>();
        for (var i = 0; i < pageStarts.Length; i++)
        {
            var end = i + 1 < pageStarts.Length ? pageStarts[i + 1] : spanBlocks.Count;
            perBox.Add(spanBlocks.Skip(pageStarts[i]).Take(end - pageStarts[i]).ToArray());
        }

        // A page read only because it shares a split block with an edited page keeps its other
        // blocks' instances (they are what it shows): only the split block is replaced there.
        for (var i = 0; i < perBox.Count; i++)
        {
            var box = panel.PageBoxes[firstBox + i];
            if (panel.WasEditedSinceCommit(box) || box.ModelBlocks is not { } shown || shown.Length != perBox[i].Length)
                continue;
            var lastContinues = firstBox + i + 1 <= lastBox && panel.PageBoxes[firstBox + i + 1].ContinuesPrevious;
            for (var k = 0; k < shown.Length; k++)
            {
                if (k == shown.Length - 1 && lastContinues)
                    continue;
                perBox[i][k] = shown[k];
            }
        }
        return perBox;
    }

    /// <summary>
    /// W14: the edited span read with its boundary boxes cut down to the one block each shares with the
    /// edited pages. The box before the first edited page supplies only its last block (the head of the block
    /// the edited page continues), the boxes after the last edited page only their first block (the rest of
    /// the edited page's last block); every other block on them is untouched and keeps its instance. In a
    /// document where every page ends in a split paragraph the span is then a few pages, not the whole
    /// chain. Null (the caller reads the span whole) when a boundary piece is not a plain paragraph or
    /// table that merges into its neighbour, or the model does not line up.
    /// </summary>
    private static List<ModelBlock[]>? TryReadBoundedSpan(
        PaginatedEditorPanel panel, DocumentView targetEditor, out int firstBox, out int lastBox)
    {
        if (!panel.TryGetEditedSpan(out firstBox, out lastBox, out var editedFirst, out var editedLast, bounded: true))
            return null;

        // One entry per box of the span: a slice of the box's blocks (all of them for the edited range).
        var slices = new List<IReadOnlyList<System.Windows.Documents.Block>>();
        for (var b = firstBox; b <= lastBox; b++)
        {
            var blocks = DocumentView.ReadableBlocks(panel.PageBoxes[b].Body.Document);
            if (b >= editedFirst && b <= editedLast)
            {
                slices.Add(blocks);
                continue;
            }
            if (blocks.Count == 0)
                return null;
            var piece = b < editedFirst ? blocks[^1] : blocks[0];
            if (piece is System.Windows.Documents.List)
                return null;
            slices.Add([piece]);
        }

        var spanBlocks = new List<ModelBlock>();
        var sliceStarts = new int[slices.Count];
        targetEditor.ReadPageSlicesInto(slices, spanBlocks, sliceStarts);
        var perBox = new List<ModelBlock[]>();
        for (var i = 0; i < slices.Count; i++)
        {
            var b = firstBox + i;
            var end = i + 1 < sliceStarts.Length ? sliceStarts[i + 1] : spanBlocks.Count;
            var read = spanBlocks.Skip(sliceStarts[i]).Take(end - sliceStarts[i]).ToArray();
            var box = panel.PageBoxes[b];
            if (box.ModelBlocks is not { } shown)
                return null;

            if (b >= editedFirst && b <= editedLast)
            {
                // As ReadWholeSpan: an untouched page between edited ones keeps its instances but for the
                // block that continues onto the next page.
                if (!panel.WasEditedSinceCommit(box) && shown.Length == read.Length)
                {
                    var continuesOn = panel.PageBoxes[b + 1].ContinuesPrevious;
                    for (var k = 0; k < shown.Length; k++)
                    {
                        if (k == shown.Length - 1 && continuesOn)
                            continue;
                        read[k] = shown[k];
                    }
                }
                perBox.Add(read);
            }
            else if (b < editedFirst)
            {
                // The head block of what the edited page continues is read again (merged with the rest);
                // a box that is only a middle piece of it contributes nothing but its rest.
                var contributesHead = shown.Length > 0 && b == firstBox;
                if (read.Length != (contributesHead ? 1 : 0))
                    return null;
                if (contributesHead)
                {
                    var kept = shown.ToArray();
                    kept[^1] = read[0];
                    perBox.Add(kept);
                }
                else
                {
                    perBox.Add(shown);
                }
            }
            else
            {
                // The rest of the edited page's last block merged into it; the box keeps everything it shows.
                if (read.Length != 0)
                    return null;
                perBox.Add(shown);
            }
        }
        return perBox;
    }
}
