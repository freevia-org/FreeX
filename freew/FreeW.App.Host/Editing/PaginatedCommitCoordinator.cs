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
        if (panel.TryGetEditedSpan(out var firstBox, out var lastBox))
        {
            var perBox = new List<ModelBlock[]>();
            var spanBlocks = new List<ModelBlock>();
            for (var i = firstBox; i <= lastBox; i++)
            {
                var read = new List<ModelBlock>();
                targetEditor.ReadBlocksInto(panel.PageBoxes[i].Body.Document, read);
                perBox.Add(read.ToArray());
                spanBlocks.AddRange(read);
            }

            var spanStart = panel.PageBoxes[firstBox].ModelStart;
            var spanCount = 0;
            for (var i = firstBox; i <= lastBox; i++)
                spanCount += panel.PageBoxes[i].ModelBlocks!.Length;

            // Never leave an empty document; the whole-body path below adds the empty paragraph.
            if (spanBlocks.Count > 0 || spanCount < model.Blocks.Count)
            {
                panel.MarkBodyCommitted();
                try
                {
                    targetEditor.HintNextBodyReplacement(spanStart, spanCount, spanBlocks.Count);
                    panel.RunOwnModelMutation(() =>
                        targetEditor.Commands.Execute(new ReplaceBlocksCommand(spanStart, spanCount, spanBlocks)));
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
                return;
            }
        }

        var collected = new List<ModelBlock>();

        foreach (var box in panel.PageBoxes)
            targetEditor.ReadBlocksInto(box.Body.Document, collected);

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
        panel.OnWholeBodyCommitted();
    }
}
