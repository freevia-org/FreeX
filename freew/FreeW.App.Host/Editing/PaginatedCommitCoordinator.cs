using FreeW.Core.Model;

using ModelBlock = FreeW.Core.Model.Block;
using ModelParagraph = FreeW.Core.Model.Paragraph;

namespace FreeW.App.Host.Editing;

#if DEBUG

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
        var collected = new List<ModelBlock>();

        foreach (var box in panel.PageBoxes)
            targetEditor.ReadBlocksInto(box.Body.Document, collected);

        model.Blocks.Clear();
        foreach (var block in collected)
            model.Blocks.Add(block);

        // Mirror CommitToModel: never leave an empty block list.
        if (model.Blocks.Count == 0)
            model.Blocks.Add(new ModelParagraph());
    }
}

#endif
