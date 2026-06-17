namespace FreeW.Core.Model;

/// <summary>Insert a block (paragraph or table) at an index in the document body.</summary>
public sealed class InsertBlockCommand(int index, Block block) : IDocumentCommand
{
    public string Label => block is Table ? "Insert Table" : "Insert Paragraph";

    public void Apply(IDocumentCommandContext context) =>
        context.Document.Blocks.Insert(index, block);

    public void Revert(IDocumentCommandContext context) =>
        context.Document.Blocks.RemoveAt(index);
}

/// <summary>Insert a paragraph at a block index.</summary>
public sealed class InsertParagraphCommand(int index, Paragraph paragraph) : IDocumentCommand
{
    public string Label => "Insert Paragraph";

    public void Apply(IDocumentCommandContext context) =>
        context.Document.Blocks.Insert(index, paragraph);

    public void Revert(IDocumentCommandContext context) =>
        context.Document.Blocks.RemoveAt(index);
}

/// <summary>Remove the block at an index (restores it on undo).</summary>
public sealed class DeleteParagraphCommand(int index) : IDocumentCommand
{
    private Block? _removed;

    public string Label => "Delete Paragraph";

    public void Apply(IDocumentCommandContext context)
    {
        _removed = context.Document.Blocks[index];
        context.Document.Blocks.RemoveAt(index);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_removed is not null)
            context.Document.Blocks.Insert(index, _removed);
    }
}

/// <summary>Replace a paragraph's formatting, snapshotting the previous value for undo.</summary>
public sealed class SetParagraphFormattingCommand(int index, ParagraphFormatting formatting) : IDocumentCommand
{
    private ParagraphFormatting? _previous;

    public string Label => "Paragraph Formatting";

    public void Apply(IDocumentCommandContext context)
    {
        var paragraph = ParagraphAt(context, index);
        _previous = paragraph.Formatting;
        paragraph.Formatting = formatting;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is not null)
            ParagraphAt(context, index).Formatting = _previous;
    }

    private static Paragraph ParagraphAt(IDocumentCommandContext context, int index) =>
        (Paragraph)context.Document.Blocks[index];
}

/// <summary>
/// Set a paragraph's <see cref="Paragraph.StyleId"/> (the named style it resolves formatting through),
/// snapshotting the previous style id for undo. Setting <paramref name="styleId"/> to null clears the
/// style back to the document defaults.
/// </summary>
public sealed class SetParagraphStyleCommand(int index, string? styleId) : IDocumentCommand
{
    private string? _previous;
    private bool _applied;

    public string Label => "Apply Style";

    public void Apply(IDocumentCommandContext context)
    {
        var paragraph = ParagraphAt(context, index);
        _previous = paragraph.StyleId;
        paragraph.StyleId = styleId;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_applied)
            ParagraphAt(context, index).StyleId = _previous;
    }

    private static Paragraph ParagraphAt(IDocumentCommandContext context, int index) =>
        (Paragraph)context.Document.Blocks[index];
}

/// <summary>Replace one run's formatting, snapshotting the previous value for undo.</summary>
public sealed class SetRunFormattingCommand(int paragraphIndex, int runIndex, RunFormatting formatting) : IDocumentCommand
{
    private RunFormatting? _previous;

    public string Label => "Character Formatting";

    public void Apply(IDocumentCommandContext context)
    {
        var run = ((Paragraph)context.Document.Blocks[paragraphIndex]).Runs[runIndex];
        _previous = run.Formatting;
        run.Formatting = formatting;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is not null)
            ((Paragraph)context.Document.Blocks[paragraphIndex]).Runs[runIndex].Formatting = _previous;
    }
}

/// <summary>
/// Replace a paragraph's run list wholesale (snapshotting the prior runs for undo). Used by edits
/// that restructure a paragraph's runs — e.g. applying a drop cap, which splits the first run so the
/// leading letter becomes its own enlarged run. The replacement runs are produced by
/// <paramref name="rebuild"/> from the paragraph; on undo the exact original run objects are restored.
/// </summary>
public sealed class ReplaceParagraphRunsCommand(int paragraphIndex, Action<Paragraph> rebuild) : IDocumentCommand
{
    private List<Run>? _previous;

    public string Label => "Format";

    public void Apply(IDocumentCommandContext context)
    {
        var paragraph = (Paragraph)context.Document.Blocks[paragraphIndex];
        _previous = [.. paragraph.Runs];
        rebuild(paragraph);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;
        var runs = ((Paragraph)context.Document.Blocks[paragraphIndex]).Runs;
        runs.Clear();
        runs.AddRange(_previous);
    }
}

/// <summary>
/// Insert a blank row into the table at <paramref name="blockIndex"/>, at <paramref name="rowIndex"/>
/// (clamped to the row count). The new row gets one empty cell per existing column. Reversible.
/// </summary>
public sealed class InsertTableRowCommand(int blockIndex, int rowIndex) : IDocumentCommand
{
    private int _appliedAt = -1;

    public string Label => "Insert Row";

    public void Apply(IDocumentCommandContext context)
    {
        var table = TableAt(context, blockIndex);
        var columns = Math.Max(table.ColumnCount, 1);
        var at = Math.Clamp(rowIndex, 0, table.Rows.Count);
        var row = new TableRow();
        for (var c = 0; c < columns; c++)
            row.Cells.Add(new TableCell(string.Empty));
        table.Rows.Insert(at, row);
        _appliedAt = at;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_appliedAt < 0)
            return;
        TableAt(context, blockIndex).Rows.RemoveAt(_appliedAt);
        _appliedAt = -1;
    }

    internal static Table TableAt(IDocumentCommandContext context, int index) =>
        (Table)context.Document.Blocks[index];
}

/// <summary>
/// Delete the row at <paramref name="rowIndex"/> from the table at <paramref name="blockIndex"/>,
/// snapshotting it (and its position) so undo restores the exact row. Never removes the last row.
/// </summary>
public sealed class DeleteTableRowCommand(int blockIndex, int rowIndex) : IDocumentCommand
{
    private TableRow? _removed;
    private int _removedAt = -1;

    public string Label => "Delete Row";

    public void Apply(IDocumentCommandContext context)
    {
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        if (table.Rows.Count <= 1 || rowIndex < 0 || rowIndex >= table.Rows.Count)
            return;
        _removedAt = rowIndex;
        _removed = table.Rows[rowIndex];
        table.Rows.RemoveAt(rowIndex);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_removed is null || _removedAt < 0)
            return;
        InsertTableRowCommand.TableAt(context, blockIndex).Rows.Insert(_removedAt, _removed);
        _removed = null;
        _removedAt = -1;
    }
}

/// <summary>
/// Insert a blank column at <paramref name="columnIndex"/> (clamped) into the table at
/// <paramref name="blockIndex"/>: one new empty cell in every row. Reversible.
/// </summary>
public sealed class InsertTableColumnCommand(int blockIndex, int columnIndex) : IDocumentCommand
{
    private int _appliedAt = -1;

    public string Label => "Insert Column";

    public void Apply(IDocumentCommandContext context)
    {
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        _appliedAt = Math.Max(columnIndex, 0);
        foreach (var row in table.Rows)
        {
            var at = Math.Clamp(_appliedAt, 0, row.Cells.Count);
            row.Cells.Insert(at, new TableCell(string.Empty));
        }
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_appliedAt < 0)
            return;
        foreach (var row in InsertTableRowCommand.TableAt(context, blockIndex).Rows)
        {
            if (_appliedAt < row.Cells.Count)
                row.Cells.RemoveAt(_appliedAt);
        }
        _appliedAt = -1;
    }
}

/// <summary>
/// Delete the column at <paramref name="columnIndex"/> from the table at <paramref name="blockIndex"/>,
/// snapshotting the removed cell of every row so undo restores them. Never removes the last column.
/// </summary>
public sealed class DeleteTableColumnCommand(int blockIndex, int columnIndex) : IDocumentCommand
{
    private List<(int Row, TableCell Cell)>? _removed;

    public string Label => "Delete Column";

    public void Apply(IDocumentCommandContext context)
    {
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        if (table.ColumnCount <= 1 || columnIndex < 0)
            return;
        var removed = new List<(int, TableCell)>();
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var cells = table.Rows[r].Cells;
            if (columnIndex < cells.Count)
            {
                removed.Add((r, cells[columnIndex]));
                cells.RemoveAt(columnIndex);
            }
        }
        _removed = removed.Count > 0 ? removed : null;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_removed is null)
            return;
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        foreach (var (rowIndex, cell) in _removed)
        {
            var cells = table.Rows[rowIndex].Cells;
            var at = Math.Clamp(columnIndex, 0, cells.Count);
            cells.Insert(at, cell);
        }
        _removed = null;
    }
}

/// <summary>
/// Set the size (points) of the inline image carried by run <paramref name="runIndex"/> of the
/// paragraph at <paramref name="paragraphIndex"/>, snapshotting the prior size for undo.
/// </summary>
public sealed class SetImageSizeCommand(int paragraphIndex, int runIndex, double widthPt, double heightPt) : IDocumentCommand
{
    private double _previousWidth;
    private double _previousHeight;
    private bool _applied;

    public string Label => "Resize Image";

    public void Apply(IDocumentCommandContext context)
    {
        if (ImageAt(context) is not { } image)
            return;
        _previousWidth = image.WidthPt;
        _previousHeight = image.HeightPt;
        image.WidthPt = widthPt;
        image.HeightPt = heightPt;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ImageAt(context) is not { } image)
            return;
        image.WidthPt = _previousWidth;
        image.HeightPt = _previousHeight;
        _applied = false;
    }

    private InlineImage? ImageAt(IDocumentCommandContext context)
    {
        if (context.Document.Blocks[paragraphIndex] is not Paragraph paragraph
            || runIndex < 0 || runIndex >= paragraph.Runs.Count)
            return null;
        return paragraph.Runs[runIndex].Image;
    }
}

/// <summary>
/// Apply a formatting transform to every run in a paragraph (e.g. toggle bold), snapshotting
/// each run's prior formatting. The building block the ribbon will call for selection-wide format.
/// </summary>
public sealed class FormatParagraphRunsCommand(int paragraphIndex, Func<RunFormatting, RunFormatting> transform) : IDocumentCommand
{
    private RunFormatting[]? _previous;

    public string Label => "Format";

    public void Apply(IDocumentCommandContext context)
    {
        var runs = ((Paragraph)context.Document.Blocks[paragraphIndex]).Runs;
        _previous = runs.Select(r => r.Formatting).ToArray();
        foreach (var run in runs)
            run.Formatting = transform(run.Formatting);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;
        var runs = ((Paragraph)context.Document.Blocks[paragraphIndex]).Runs;
        for (var i = 0; i < runs.Count && i < _previous.Length; i++)
            runs[i].Formatting = _previous[i];
    }
}
