namespace FreeW.Core.Model;

/// <summary>
/// Replaces an existing content-control run in the document body as a form-field edit.
/// </summary>
public sealed class ReplaceContentControlRunCommand(int blockIndex, int runIndex, Run replacement)
    : IDocumentCommand
{
    private Run? _previous;
    private bool _applied;

    public string Label => "Edit Form Field";

    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.FormField;

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetRun(context.Document, out var paragraph)
            || paragraph.Runs[runIndex].Control is null
            || replacement.Control is null)
        {
            return;
        }

        _previous = paragraph.Runs[runIndex];
        paragraph.Runs[runIndex] = replacement;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || _previous is null || !TryGetRun(context.Document, out var paragraph))
            return;

        paragraph.Runs[runIndex] = _previous;
        _previous = null;
        _applied = false;
    }

    private bool TryGetRun(TextDocument document, out Paragraph paragraph)
    {
        paragraph = null!;
        if (blockIndex < 0
            || blockIndex >= document.Blocks.Count
            || document.Blocks[blockIndex] is not Paragraph candidate
            || runIndex < 0
            || runIndex >= candidate.Runs.Count)
        {
            return false;
        }

        paragraph = candidate;
        return true;
    }
}

/// <summary>
/// Replaces an existing content-control run inside a table cell paragraph as a form-field edit. The
/// body-level <see cref="ReplaceContentControlRunCommand"/> only addresses top-level paragraphs; forms
/// commonly lay their fields out in a table, and both must record a
/// <see cref="DocumentCommandMutationKind.FormField"/> mutation so undo/redo stays permitted while
/// "Filling in Forms" protection locks body editing.
/// </summary>
public sealed class ReplaceCellContentControlRunCommand(
    int blockIndex,
    int rowIndex,
    int cellStartColumn,
    int paragraphIndex,
    int runIndex,
    Run replacement) : IDocumentCommand
{
    private Run? _previous;
    private bool _applied;

    public string Label => "Edit Form Field";

    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.FormField;

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetRun(context.Document, out var paragraph)
            || paragraph.Runs[runIndex].Control is null
            || replacement.Control is null)
        {
            return;
        }

        _previous = paragraph.Runs[runIndex];
        paragraph.Runs[runIndex] = replacement;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || _previous is null || !TryGetRun(context.Document, out var paragraph))
            return;

        paragraph.Runs[runIndex] = _previous;
        _previous = null;
        _applied = false;
    }

    private bool TryGetRun(TextDocument document, out Paragraph paragraph)
    {
        paragraph = null!;
        if (!TableCellCommandAddress.TryGetParagraph(
                document,
                blockIndex,
                rowIndex,
                cellStartColumn,
                paragraphIndex,
                out var candidate)
            || runIndex < 0
            || runIndex >= candidate.Runs.Count)
        {
            return false;
        }

        paragraph = candidate;
        return true;
    }
}
