using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// Replaces a table cell's text with a single paragraph, preserving the cell's first run formatting.
/// Implemented in the app (not shared FreeW.Core) against the public <see cref="IDocumentCommand"/>
/// interface, so it rides the same undo/redo bus without modifying the shared command set. This is the
/// foundation for table editing while full in-cell caret editing remains a later effort.
/// </summary>
internal sealed class CellTextCommand(int blockIndex, int rowIndex, int columnIndex, string text) : IDocumentCommand
{
    private List<Paragraph>? _saved;

    public string Label => "Edit table cell";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetCell(context, out var cell))
            return;

        _saved = new List<Paragraph>(cell.Paragraphs);
        var fmt = cell.Paragraphs.Count > 0 && cell.Paragraphs[0].Runs.Count > 0
            ? cell.Paragraphs[0].Runs[0].Formatting
            : RunFormatting.Default;

        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(text, fmt));
        cell.Paragraphs.Clear();
        cell.Paragraphs.Add(paragraph);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_saved is null || !TryGetCell(context, out var cell))
            return;
        cell.Paragraphs.Clear();
        cell.Paragraphs.AddRange(_saved);
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
        var cells = table.Rows[rowIndex].Cells;
        if (columnIndex < 0 || columnIndex >= cells.Count)
            return false;
        cell = cells[columnIndex];
        return true;
    }
}
