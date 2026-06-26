using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// AV-TBL: Splices the paragraph list of a table cell — replaces the paragraphs from
/// <paramref name="firstParaIndex"/> up to <paramref name="firstParaIndex"/> + <paramref name="removeCount"/>
/// with <paramref name="insertParas"/>. Supports paragraph break (split one → two) and merge (two → one).
///
/// Cell addressing uses the same (blockIndex, rowIndex, cellStartCol) tuple as
/// <see cref="ReplaceCellParagraphRunsCommand"/>.
/// </summary>
internal sealed class SpliceCellParagraphsCommand(
    int blockIndex,
    int rowIndex,
    int cellStartCol,
    int firstParaIndex,
    int removeCount,
    IReadOnlyList<Paragraph> insertParas) : IDocumentCommand
{
    private List<Paragraph>? _removed;

    public string Label => "Edit table cell";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetCell(context, out var cell))
            return;
        var paras = cell.Paragraphs;
        var at = Math.Clamp(firstParaIndex, 0, paras.Count);
        var count = Math.Clamp(removeCount, 0, paras.Count - at);
        _removed = paras.GetRange(at, count);
        paras.RemoveRange(at, count);
        paras.InsertRange(at, insertParas);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_removed is null || !TryGetCell(context, out var cell))
            return;
        var paras = cell.Paragraphs;
        var at = Math.Clamp(firstParaIndex, 0, paras.Count);
        var count = Math.Clamp(insertParas.Count, 0, paras.Count - at);
        paras.RemoveRange(at, count);
        paras.InsertRange(at, _removed);
    }

    private bool TryGetCell(IDocumentCommandContext context, out TableCell cell)
    {
        cell = null!;
        if (blockIndex < 0 || blockIndex >= context.Document.Blocks.Count)
            return false;
        if (context.Document.Blocks[blockIndex] is not Table table)
            return false;
        if (rowIndex < 0 || rowIndex >= table.Rows.Count)
            return false;
        var col = 0;
        foreach (var c in table.Rows[rowIndex].Cells)
        {
            if (col == cellStartCol)
            {
                cell = c;
                return true;
            }
            col += Math.Max(1, c.GridSpan);
        }
        return false;
    }
}
