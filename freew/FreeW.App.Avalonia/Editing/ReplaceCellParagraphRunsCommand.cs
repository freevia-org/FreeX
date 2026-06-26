using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// AV-TBL: Replaces the run list of a single paragraph inside a table cell,
/// snapshotting the prior runs for undo. Analogous to <c>ReplaceParagraphRunsCommand</c>
/// but addresses a paragraph inside a cell rather than a top-level block.
///
/// Cell addressing uses (blockIndex → table block, rowIndex → row in table,
/// cellStartCol → the column-start of the cell as stored in <c>_cellHits</c>,
/// paraIndex → paragraph within the cell). The <paramref name="rebuild"/> action
/// mutates the paragraph in-place (same contract as <c>ReplaceParagraphRunsCommand</c>).
/// </summary>
internal sealed class ReplaceCellParagraphRunsCommand(
    int blockIndex,
    int rowIndex,
    int cellStartCol,
    int paraIndex,
    Action<Paragraph> rebuild) : IDocumentCommand
{
    private List<Run>? _previous;

    public string Label => "Edit table cell";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetParagraph(context, out var para))
            return;
        _previous = [.. para.Runs];
        rebuild(para);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null || !TryGetParagraph(context, out var para))
            return;
        para.Runs.Clear();
        para.Runs.AddRange(_previous);
    }

    private bool TryGetParagraph(IDocumentCommandContext context, out Paragraph para)
    {
        para = null!;
        if (blockIndex < 0 || blockIndex >= context.Document.Blocks.Count)
            return false;
        if (context.Document.Blocks[blockIndex] is not Table table)
            return false;
        if (rowIndex < 0 || rowIndex >= table.Rows.Count)
            return false;
        // Walk cells by column-start to find the right cell (handles merged cells).
        var col = 0;
        foreach (var cell in table.Rows[rowIndex].Cells)
        {
            if (col == cellStartCol)
            {
                if (paraIndex < 0 || paraIndex >= cell.Paragraphs.Count)
                    return false;
                para = cell.Paragraphs[paraIndex];
                return true;
            }
            col += Math.Max(1, cell.GridSpan);
        }
        return false;
    }
}
