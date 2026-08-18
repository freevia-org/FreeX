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
/// Replaces a whole run span belonging to one content control — the runs a w:sdt wraps — with new runs,
/// as a form-field edit. Editing a field's text is not always 1:1 on runs: mixed formatting inside a
/// field is several runs already, and a tracked edit adds an inserted or struck run beside the original.
/// The <see cref="DocumentCommandMutationKind.FormField"/> classification keeps undo/redo permitted while
/// "Filling in Forms" protection locks body editing.
/// </summary>
public sealed class ReplaceContentControlRunSpanCommand(
    int blockIndex,
    int runIndex,
    int runCount,
    IReadOnlyList<Run> replacement) : IDocumentCommand
{
    private List<Run>? _previous;

    public string Label => "Edit Form Field";

    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.FormField;

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetParagraph(context.Document, out var paragraph)
            || !ContentControlRunSpan.IsReplaceable(paragraph, runIndex, runCount, replacement))
        {
            return;
        }

        _previous = ContentControlRunSpan.Replace(paragraph, runIndex, runCount, replacement);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null || !TryGetParagraph(context.Document, out var paragraph))
            return;

        ContentControlRunSpan.Replace(paragraph, runIndex, replacement.Count, _previous);
        _previous = null;
    }

    private bool TryGetParagraph(TextDocument document, out Paragraph paragraph)
    {
        paragraph = null!;
        if (blockIndex < 0
            || blockIndex >= document.Blocks.Count
            || document.Blocks[blockIndex] is not Paragraph candidate)
        {
            return false;
        }

        paragraph = candidate;
        return true;
    }
}

/// <summary>
/// The table-cell counterpart of <see cref="ReplaceContentControlRunSpanCommand"/> — forms commonly lay
/// their fields out in a table, and those edits must record the same form-field mutation.
/// </summary>
public sealed class ReplaceCellContentControlRunSpanCommand(
    int blockIndex,
    int rowIndex,
    int cellStartColumn,
    int paragraphIndex,
    int runIndex,
    int runCount,
    IReadOnlyList<Run> replacement) : IDocumentCommand
{
    private List<Run>? _previous;

    public string Label => "Edit Form Field";

    public DocumentCommandMutationKind MutationKind => DocumentCommandMutationKind.FormField;

    public void Apply(IDocumentCommandContext context)
    {
        if (!TableCellCommandAddress.TryGetParagraph(
                context.Document,
                blockIndex,
                rowIndex,
                cellStartColumn,
                paragraphIndex,
                out var paragraph)
            || !ContentControlRunSpan.IsReplaceable(paragraph, runIndex, runCount, replacement))
        {
            return;
        }

        _previous = ContentControlRunSpan.Replace(paragraph, runIndex, runCount, replacement);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null
            || !TableCellCommandAddress.TryGetParagraph(
                context.Document,
                blockIndex,
                rowIndex,
                cellStartColumn,
                paragraphIndex,
                out var paragraph))
        {
            return;
        }

        ContentControlRunSpan.Replace(paragraph, runIndex, replacement.Count, _previous);
        _previous = null;
    }
}

internal static class ContentControlRunSpan
{
    /// <summary>
    /// Both the runs being replaced and their replacements must be content-control runs: these commands
    /// exist to edit a field's own content, never to swap body text in or out of one.
    /// </summary>
    internal static bool IsReplaceable(
        Paragraph paragraph,
        int runIndex,
        int runCount,
        IReadOnlyList<Run> replacement)
    {
        if (runIndex < 0 || runCount <= 0 || runIndex + runCount > paragraph.Runs.Count)
            return false;

        for (var index = runIndex; index < runIndex + runCount; index++)
        {
            if (paragraph.Runs[index].Control is null)
                return false;
        }

        return replacement.Count > 0 && replacement.All(run => run.Control is not null);
    }

    internal static List<Run> Replace(
        Paragraph paragraph,
        int runIndex,
        int runCount,
        IReadOnlyList<Run> replacement)
    {
        var removed = paragraph.Runs.GetRange(runIndex, runCount);
        paragraph.Runs.RemoveRange(runIndex, runCount);
        paragraph.Runs.InsertRange(runIndex, replacement);
        return removed;
    }
}
