namespace FreeW.Core.Model;

/// <summary>Rebuilds one table-cell paragraph's runs while preserving undo state.</summary>
public sealed class ReplaceCellParagraphRunsCommand(
    int blockIndex,
    int rowIndex,
    int cellStartColumn,
    int paragraphIndex,
    Action<Paragraph> rebuild) : IDocumentCommand
{
    private List<Run>? _previous;

    public string Label => "Edit table cell";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TableCellCommandAddress.TryGetParagraph(
                context.Document,
                blockIndex,
                rowIndex,
                cellStartColumn,
                paragraphIndex,
                out var paragraph))
        {
            return;
        }

        _previous = [.. paragraph.Runs];
        rebuild(paragraph);
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

        paragraph.Runs.Clear();
        paragraph.Runs.AddRange(_previous);
        _previous = null;
    }
}

/// <summary>Replaces a range of paragraphs inside one table cell while preserving undo state.</summary>
public sealed class SpliceCellParagraphsCommand(
    int blockIndex,
    int rowIndex,
    int cellStartColumn,
    int firstParagraphIndex,
    int removeCount,
    IReadOnlyList<Paragraph> replacement) : IDocumentCommand
{
    private List<Paragraph>? _removed;

    public string Label => "Edit table cell";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TableCellCommandAddress.TryGetCell(
                context.Document,
                blockIndex,
                rowIndex,
                cellStartColumn,
                out var cell))
        {
            return;
        }

        var at = Math.Clamp(firstParagraphIndex, 0, cell.Paragraphs.Count);
        var count = Math.Clamp(removeCount, 0, cell.Paragraphs.Count - at);
        _removed = cell.Paragraphs.GetRange(at, count);
        cell.Paragraphs.RemoveRange(at, count);
        cell.Paragraphs.InsertRange(at, replacement);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_removed is null
            || !TableCellCommandAddress.TryGetCell(
                context.Document,
                blockIndex,
                rowIndex,
                cellStartColumn,
                out var cell))
        {
            return;
        }

        var at = Math.Clamp(firstParagraphIndex, 0, cell.Paragraphs.Count);
        var count = Math.Clamp(replacement.Count, 0, cell.Paragraphs.Count - at);
        cell.Paragraphs.RemoveRange(at, count);
        cell.Paragraphs.InsertRange(at, _removed);
        _removed = null;
    }
}

/// <summary>Sets a table-cell paragraph mark revision while preserving undo state.</summary>
public sealed class SetCellParagraphMarkRevisionCommand(
    int blockIndex,
    int rowIndex,
    int cellStartColumn,
    int paragraphIndex,
    RevisionKind kind,
    string? author,
    string? dateXml) : IDocumentCommand
{
    private RevisionKind _previousKind;
    private string? _previousAuthor;
    private string? _previousDateXml;
    private bool _applied;

    public string Label => kind == RevisionKind.Deleted ? "Delete Paragraph Mark" : "Insert Paragraph Mark";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TableCellCommandAddress.TryGetParagraph(
                context.Document,
                blockIndex,
                rowIndex,
                cellStartColumn,
                paragraphIndex,
                out var paragraph))
        {
            return;
        }

        _previousKind = paragraph.MarkRevision;
        _previousAuthor = paragraph.MarkRevisionAuthor;
        _previousDateXml = paragraph.MarkRevisionDateXml;
        paragraph.MarkRevision = kind;
        paragraph.MarkRevisionAuthor = author;
        paragraph.MarkRevisionDateXml = dateXml;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied
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

        paragraph.MarkRevision = _previousKind;
        paragraph.MarkRevisionAuthor = _previousAuthor;
        paragraph.MarkRevisionDateXml = _previousDateXml;
        _applied = false;
    }
}

internal static class TableCellCommandAddress
{
    public static bool TryGetParagraph(
        TextDocument document,
        int blockIndex,
        int rowIndex,
        int cellStartColumn,
        int paragraphIndex,
        out Paragraph paragraph)
    {
        paragraph = null!;
        if (!TryGetCell(document, blockIndex, rowIndex, cellStartColumn, out var cell)
            || paragraphIndex < 0
            || paragraphIndex >= cell.Paragraphs.Count)
        {
            return false;
        }

        paragraph = cell.Paragraphs[paragraphIndex];
        return true;
    }

    public static bool TryGetCell(
        TextDocument document,
        int blockIndex,
        int rowIndex,
        int cellStartColumn,
        out TableCell cell)
    {
        cell = null!;
        if (blockIndex < 0
            || blockIndex >= document.Blocks.Count
            || document.Blocks[blockIndex] is not Table table
            || rowIndex < 0
            || rowIndex >= table.Rows.Count)
        {
            return false;
        }

        var projected = TableGridProjection.StartingAt(table.Rows[rowIndex], cellStartColumn);
        if (projected is null)
            return false;
        cell = projected.Value.Cell;
        return true;
    }
}
