using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// AV-INSERT: Appends a single object-carrying <see cref="Run"/> (an inline/floating image, shape,
/// text box, etc.) to the end of the body paragraph at <paramref name="paragraphIndex"/>, snapshotting
/// the prior run list for undo.
///
/// <para>
/// Object runs (a run whose <c>Image</c>/<c>Shape</c>/… is set and whose <c>Text</c> is empty) cannot
/// flow through the char-based cell round-trip (<c>ParaCells</c>/<c>SetRuns</c>) that ordinary text
/// edits use — a textless run carries no cells and would be dropped. This command therefore mutates the
/// paragraph's <see cref="Paragraph.Runs"/> list directly, mirroring the WPF host's
/// <c>DocumentView.InsertShape</c>/<c>InsertImage</c> which append the object to the caret paragraph's
/// inlines. Undo restores the exact prior run instances.
/// </para>
/// </summary>
internal sealed class InsertObjectRunCommand(int paragraphIndex, Run run) : IDocumentCommand
{
    private List<Run>? _previous;

    public string Label =>
        run.Image is not null ? "Insert Picture"
        : run.Shape is { Kind: ShapeKind.TextBox } ? "Insert Text Box"
        : run.Shape is not null ? "Insert Shape"
        : "Insert Object";

    public void Apply(IDocumentCommandContext context)
    {
        if (ParagraphAt(context) is not { } paragraph)
            return;
        _previous = [.. paragraph.Runs];
        paragraph.Runs.Add(run);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null || ParagraphAt(context) is not { } paragraph)
            return;
        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(_previous);
        _previous = null;
    }

    private Paragraph? ParagraphAt(IDocumentCommandContext context) =>
        paragraphIndex >= 0 && paragraphIndex < context.Document.Blocks.Count
            ? context.Document.Blocks[paragraphIndex] as Paragraph
            : null;
}

/// <summary>
/// AV-INSERT: Create (enable) the document header or footer region if it is missing or empty, seeding it
/// with a single empty paragraph so it renders in the page-margin region (which the Avalonia renderer
/// already draws — see AV-HF). Snapshots the prior region so undo removes it again.
///
/// <para>
/// This is the model-backed "Insert &gt; Header / Footer" entry point. Interactive in-region header/footer
/// caret editing is a separate, larger UI surface (deferred); this command guarantees the region exists
/// and is ready to be populated, matching the Insert tab's "add a header" affordance.
/// </para>
/// </summary>
internal sealed class EnsureHeaderFooterCommand(bool isFooter) : IDocumentCommand
{
    private HeaderFooter? _previous;
    private bool _applied;

    public string Label => isFooter ? "Insert Footer" : "Insert Header";

    public void Apply(IDocumentCommandContext context)
    {
        var doc = context.Document;
        _previous = isFooter ? doc.Footer : doc.Header;

        // Already present with content → no-op (still snapshot so undo is consistent).
        if (_previous is { IsEmpty: false })
        {
            _applied = false;
            return;
        }

        var region = new HeaderFooter();
        region.Paragraphs.Add(new Paragraph());
        if (isFooter)
            doc.Footer = region;
        else
            doc.Header = region;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied)
            return;
        var doc = context.Document;
        if (isFooter)
            doc.Footer = _previous;
        else
            doc.Header = _previous;
        _applied = false;
    }
}
