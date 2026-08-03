namespace FreeW.Core.Model;

/// <summary>
/// Inserts a computed table formula at a character offset inside one table-cell paragraph. The prior
/// run list is restored exactly on undo, while redo reuses the original formula field and cached result.
/// </summary>
public sealed class InsertTableCellFormulaCommand(
    int tableBlockIndex,
    int rowIndex,
    int cellIndex,
    int paragraphIndex,
    int textOffset,
    TableFormulaField formula) : IDocumentCommand
{
    private Run[]? _previousRuns;
    private Run? _formulaRun;
    private bool _applied;

    public string Label => "Insert Formula";

    public int InsertedTextLength => _formulaRun?.Text.Length ?? 0;

    public void Apply(IDocumentCommandContext context)
    {
        if (_applied || !TryGetTarget(context.Document, out var table, out var paragraph))
            return;

        _previousRuns = [.. paragraph.Runs];
        _formulaRun ??= TableLayoutOperations.BuildFormulaRun(table, rowIndex, cellIndex, formula);
        RevisionEditPlanner.InsertRunAtOffset(paragraph, textOffset, _formulaRun);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || _previousRuns is null || !TryGetTarget(context.Document, out _, out var paragraph))
            return;

        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(_previousRuns);
        _previousRuns = null;
        _applied = false;
    }

    private bool TryGetTarget(TextDocument document, out Table table, out Paragraph paragraph)
    {
        table = null!;
        paragraph = null!;
        if (tableBlockIndex < 0
            || tableBlockIndex >= document.Blocks.Count
            || document.Blocks[tableBlockIndex] is not Table candidate
            || rowIndex < 0
            || rowIndex >= candidate.Rows.Count
            || cellIndex < 0
            || cellIndex >= candidate.Rows[rowIndex].Cells.Count)
        {
            return false;
        }

        var paragraphs = candidate.Rows[rowIndex].Cells[cellIndex].Paragraphs;
        if (paragraphIndex < 0 || paragraphIndex >= paragraphs.Count)
            return false;

        table = candidate;
        paragraph = paragraphs[paragraphIndex];
        return true;
    }
}
