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

/// <summary>
/// Replace the contiguous span of blocks [<paramref name="index"/>, <paramref name="index"/> +
/// <paramref name="count"/>) with <paramref name="replacement"/>, snapshotting the removed blocks so
/// undo restores the exact originals at their original position. The building block for edits that
/// restructure a run of body blocks in one reversible step — reordering paragraphs after a sort, or
/// swapping a paragraph span for a table (and vice versa) in the text/table converters. The span is
/// clamped to the body so a stale index can never throw.
/// </summary>
public sealed class ReplaceBlocksCommand(int index, int count, IReadOnlyList<Block> replacement) : IDocumentCommand
{
    private Block[]? _removed;
    private int _appliedAt = -1;

    public string Label => "Edit";

    public void Apply(IDocumentCommandContext context)
    {
        var blocks = context.Document.Blocks;
        var at = Math.Clamp(index, 0, blocks.Count);
        var take = Math.Clamp(count, 0, blocks.Count - at);

        _appliedAt = at;
        _removed = new Block[take];
        for (var i = 0; i < take; i++)
            _removed[i] = blocks[at + i];

        blocks.RemoveRange(at, take);
        blocks.InsertRange(at, replacement);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_removed is null || _appliedAt < 0)
            return;
        var blocks = context.Document.Blocks;
        blocks.RemoveRange(_appliedAt, replacement.Count);
        blocks.InsertRange(_appliedAt, _removed);
        _removed = null;
        _appliedAt = -1;
    }
}

/// <summary>
/// Reversibly reorder the whole body by replacing it with <paramref name="reordered"/> — a permutation
/// of the current blocks (same instances, new order). Snapshots the prior block order so undo restores
/// it exactly. Used by the navigation pane's "Move Up / Move Down" to relocate a heading-subtree
/// (<see cref="OutlineTools.MoveSubtree"/>) in one undoable step. The replacement is applied as a clear
/// + re-add of the existing <see cref="TextDocument.Blocks"/> list, so no block instance is recreated.
/// </summary>
public sealed class ReorderBlocksCommand(IReadOnlyList<Block> reordered) : IDocumentCommand
{
    private Block[]? _previous;

    public string Label => "Move Heading";

    public void Apply(IDocumentCommandContext context)
    {
        var blocks = context.Document.Blocks;
        _previous = [.. blocks];
        blocks.Clear();
        blocks.AddRange(reordered);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;
        var blocks = context.Document.Blocks;
        blocks.Clear();
        blocks.AddRange(_previous);
        _previous = null;
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
/// Merge a contiguous horizontal run of cells in one row of the table at <paramref name="blockIndex"/>.
/// The cells <c>[firstColumn, lastColumn]</c> of row <paramref name="rowIndex"/> collapse into the
/// left-most cell: its <see cref="TableCell.GridSpan"/> grows to cover the run (summing the merged
/// cells' spans) and the absorbed cells are dropped from the row. The full original row is snapshotted
/// so undo restores it exactly (cells, spans, and content). No-op if the run is empty or out of range.
/// </summary>
public sealed class MergeCellsHorizontalCommand(int blockIndex, int rowIndex, int firstColumn, int lastColumn) : IDocumentCommand
{
    private TableCell[]? _removedRow;
    private int _survivorColumn = -1;
    private int _survivorSpan = 1;

    public string Label => "Merge Cells";

    public void Apply(IDocumentCommandContext context)
    {
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        if (rowIndex < 0 || rowIndex >= table.Rows.Count)
            return;
        var cells = table.Rows[rowIndex].Cells;
        var first = Math.Clamp(Math.Min(firstColumn, lastColumn), 0, cells.Count - 1);
        var last = Math.Clamp(Math.Max(firstColumn, lastColumn), 0, cells.Count - 1);
        if (first >= last)
            return;

        // Snapshot the row layout and the survivor's original span so undo restores both (the survivor
        // is one of the snapshotted cell instances, so its span must be remembered separately).
        _removedRow = [.. cells];
        _survivorColumn = first;
        var survivor = cells[first];
        _survivorSpan = survivor.GridSpan;

        var totalSpan = 0;
        for (var c = first; c <= last; c++)
            totalSpan += Math.Max(1, cells[c].GridSpan);
        survivor.GridSpan = totalSpan;

        for (var c = last; c > first; c--)
            cells.RemoveAt(c);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_removedRow is null)
            return;
        var cells = InsertTableRowCommand.TableAt(context, blockIndex).Rows[rowIndex].Cells;
        if (_survivorColumn >= 0 && _survivorColumn < _removedRow.Length)
            _removedRow[_survivorColumn].GridSpan = _survivorSpan;
        cells.Clear();
        cells.AddRange(_removedRow);
        _removedRow = null;
        _survivorColumn = -1;
    }
}

/// <summary>
/// Merge a contiguous vertical run of cells in one column of the table at <paramref name="blockIndex"/>.
/// The cell at <c>(firstRow, columnIndex)</c> becomes the merge head
/// (<see cref="VerticalMergeState.Restart"/>) and the cells directly below it down to
/// <paramref name="lastRow"/> become <see cref="VerticalMergeState.Continue"/>. The previous merge
/// states of every touched cell are snapshotted so undo restores them. No-op if the run is empty or
/// out of range.
/// </summary>
public sealed class MergeCellsVerticalCommand(int blockIndex, int columnIndex, int firstRow, int lastRow) : IDocumentCommand
{
    private (int Row, VerticalMergeState State)[]? _previous;

    public string Label => "Merge Cells";

    public void Apply(IDocumentCommandContext context)
    {
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        var first = Math.Min(firstRow, lastRow);
        var last = Math.Max(firstRow, lastRow);
        if (first < 0 || last >= table.Rows.Count || first >= last)
            return;

        var snapshot = new List<(int, VerticalMergeState)>();
        for (var r = first; r <= last; r++)
        {
            var cells = table.Rows[r].Cells;
            if (columnIndex < 0 || columnIndex >= cells.Count)
                return;
            snapshot.Add((r, cells[columnIndex].VerticalMerge));
        }

        _previous = [.. snapshot];
        table.Rows[first].Cells[columnIndex].VerticalMerge = VerticalMergeState.Restart;
        for (var r = first + 1; r <= last; r++)
            table.Rows[r].Cells[columnIndex].VerticalMerge = VerticalMergeState.Continue;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        foreach (var (row, state) in _previous)
        {
            if (row < table.Rows.Count && columnIndex < table.Rows[row].Cells.Count)
                table.Rows[row].Cells[columnIndex].VerticalMerge = state;
        }
        _previous = null;
    }
}

/// <summary>
/// Split a previously merged cell at <c>(rowIndex, columnIndex)</c> in the table at
/// <paramref name="blockIndex"/> back into single cells. A horizontal merge (GridSpan &gt; 1) is undone
/// by resetting the cell's span to 1 and re-adding the dropped empty cells to its right. A vertical
/// merge head (<see cref="VerticalMergeState.Restart"/>) is undone by clearing its merge state and the
/// <see cref="VerticalMergeState.Continue"/> cells below it in the same grid column. The prior state is
/// snapshotted so undo re-merges. No-op if the cell is not merged.
/// </summary>
public sealed class SplitCellCommand(int blockIndex, int rowIndex, int columnIndex) : IDocumentCommand
{
    private int _restoredSpan = 1;
    private int _splitColumn = -1;
    private (int Row, int Column, VerticalMergeState State)[]? _verticalPrevious;
    private bool _appliedHorizontal;

    public string Label => "Split Cell";

    public void Apply(IDocumentCommandContext context)
    {
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        if (rowIndex < 0 || rowIndex >= table.Rows.Count)
            return;
        var cells = table.Rows[rowIndex].Cells;
        if (columnIndex < 0 || columnIndex >= cells.Count)
            return;
        var cell = cells[columnIndex];

        // Horizontal split: collapse GridSpan back to 1, re-adding the absorbed empty cells.
        if (cell.GridSpan > 1)
        {
            _restoredSpan = cell.GridSpan;
            _splitColumn = columnIndex;
            cell.GridSpan = 1;
            for (var i = 1; i < _restoredSpan; i++)
                cells.Insert(columnIndex + i, new TableCell(string.Empty));
            _appliedHorizontal = true;
        }

        // Vertical split: clear the restart head and the continue cells beneath it in the same column.
        if (cell.VerticalMerge == VerticalMergeState.Restart)
        {
            var snapshot = new List<(int, int, VerticalMergeState)> { (rowIndex, columnIndex, VerticalMergeState.Restart) };
            cell.VerticalMerge = VerticalMergeState.None;
            for (var r = rowIndex + 1; r < table.Rows.Count; r++)
            {
                var below = table.Rows[r].Cells;
                if (columnIndex >= below.Count || below[columnIndex].VerticalMerge != VerticalMergeState.Continue)
                    break;
                snapshot.Add((r, columnIndex, VerticalMergeState.Continue));
                below[columnIndex].VerticalMerge = VerticalMergeState.None;
            }
            _verticalPrevious = [.. snapshot];
        }
    }

    public void Revert(IDocumentCommandContext context)
    {
        var table = InsertTableRowCommand.TableAt(context, blockIndex);

        if (_appliedHorizontal && _splitColumn >= 0 && rowIndex < table.Rows.Count)
        {
            var cells = table.Rows[rowIndex].Cells;
            for (var i = _restoredSpan - 1; i >= 1; i--)
            {
                if (_splitColumn + i < cells.Count)
                    cells.RemoveAt(_splitColumn + i);
            }
            if (_splitColumn < cells.Count)
                cells[_splitColumn].GridSpan = _restoredSpan;
            _appliedHorizontal = false;
        }

        if (_verticalPrevious is not null)
        {
            foreach (var (row, column, state) in _verticalPrevious)
            {
                if (row < table.Rows.Count && column < table.Rows[row].Cells.Count)
                    table.Rows[row].Cells[column].VerticalMerge = state;
            }
            _verticalPrevious = null;
        }
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
/// Set the rotation angle and flip flags on the inline image at the given paragraph/run indices,
/// snapshotting prior values for undo.
/// </summary>
public sealed class SetImageRotationCommand(int paragraphIndex, int runIndex, double angleDeg, bool flipH, bool flipV) : IDocumentCommand
{
    private double _prevAngle;
    private bool _prevFlipH, _prevFlipV;
    private bool _applied;

    public string Label => "Rotate/Flip Image";

    public void Apply(IDocumentCommandContext context)
    {
        if (ImageAt(context) is not { } image) return;
        _prevAngle = image.RotationAngle; _prevFlipH = image.FlipH; _prevFlipV = image.FlipV;
        image.RotationAngle = angleDeg; image.FlipH = flipH; image.FlipV = flipV;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ImageAt(context) is not { } image) return;
        image.RotationAngle = _prevAngle; image.FlipH = _prevFlipH; image.FlipV = _prevFlipV;
        _applied = false;
    }

    private InlineImage? ImageAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Image : null;
}

/// <summary>
/// Set crop fractions (0–1 per edge) on the inline image at the given paragraph/run indices,
/// snapshotting prior values for undo.
/// </summary>
public sealed class SetImageCropCommand(int paragraphIndex, int runIndex, double left, double right, double top, double bottom) : IDocumentCommand
{
    private double _pl, _pr, _pt, _pb;
    private bool _applied;

    public string Label => "Crop Image";

    public void Apply(IDocumentCommandContext context)
    {
        if (ImageAt(context) is not { } image) return;
        _pl = image.CropLeft; _pr = image.CropRight; _pt = image.CropTop; _pb = image.CropBottom;
        image.CropLeft = left; image.CropRight = right; image.CropTop = top; image.CropBottom = bottom;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ImageAt(context) is not { } image) return;
        image.CropLeft = _pl; image.CropRight = _pr; image.CropTop = _pt; image.CropBottom = _pb;
        _applied = false;
    }

    private InlineImage? ImageAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Image : null;
}

/// <summary>
/// Set picture border (color hex, width in points, dash token) on the inline image at the given
/// paragraph/run indices, snapshotting prior values for undo. Pass null colorHex to remove the border.
/// </summary>
public sealed class SetImageBorderCommand(int paragraphIndex, int runIndex, string? colorHex, double widthPt, string? dash) : IDocumentCommand
{
    private string? _prevColor;
    private double _prevWidth;
    private string? _prevDash;
    private bool _applied;

    public string Label => "Picture Border";

    public void Apply(IDocumentCommandContext context)
    {
        if (ImageAt(context) is not { } image) return;
        _prevColor = image.BorderColorHex; _prevWidth = image.BorderWidthPt; _prevDash = image.BorderDash;
        image.BorderColorHex = colorHex; image.BorderWidthPt = widthPt; image.BorderDash = dash;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ImageAt(context) is not { } image) return;
        image.BorderColorHex = _prevColor; image.BorderWidthPt = _prevWidth; image.BorderDash = _prevDash;
        _applied = false;
    }

    private InlineImage? ImageAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Image : null;
}

/// <summary>
/// Restore an inline image to its natural size (computed from OriginalPixelWidth/Height at the given
/// screen DPI) and clear any rotation, flip, and crop. Snaps all prior values for undo.
/// </summary>
public sealed class ResetImageSizeCommand(int paragraphIndex, int runIndex, double naturalWidthPt, double naturalHeightPt) : IDocumentCommand
{
    private double _pw, _ph, _prevAngle;
    private bool _prevFlipH, _prevFlipV;
    private double _pl, _pr, _pt, _pb;
    private bool _applied;

    public string Label => "Reset Picture";

    public void Apply(IDocumentCommandContext context)
    {
        if (ImageAt(context) is not { } image) return;
        _pw = image.WidthPt; _ph = image.HeightPt;
        _prevAngle = image.RotationAngle; _prevFlipH = image.FlipH; _prevFlipV = image.FlipV;
        _pl = image.CropLeft; _pr = image.CropRight; _pt = image.CropTop; _pb = image.CropBottom;
        image.WidthPt = naturalWidthPt; image.HeightPt = naturalHeightPt;
        image.RotationAngle = 0; image.FlipH = false; image.FlipV = false;
        image.CropLeft = image.CropRight = image.CropTop = image.CropBottom = 0;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ImageAt(context) is not { } image) return;
        image.WidthPt = _pw; image.HeightPt = _ph;
        image.RotationAngle = _prevAngle; image.FlipH = _prevFlipH; image.FlipV = _prevFlipV;
        image.CropLeft = _pl; image.CropRight = _pr; image.CropTop = _pt; image.CropBottom = _pb;
        _applied = false;
    }

    private InlineImage? ImageAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Image : null;
}

/// <summary>
/// Set the floating position offsets and anchors for an inline image, snapshotting prior values for undo.
/// </summary>
public sealed class SetImagePositionCommand(int paragraphIndex, int runIndex,
    double horizontalOffsetPt, double verticalOffsetPt,
    HorizontalAnchor horizontalAnchor, VerticalAnchor verticalAnchor) : IDocumentCommand
{
    private double _ph, _pv;
    private HorizontalAnchor _pha;
    private VerticalAnchor _pva;
    private bool _applied;

    public string Label => "Set Image Position";

    public void Apply(IDocumentCommandContext context)
    {
        if (ImageAt(context) is not { } image) return;
        _ph = image.HorizontalOffsetPt; _pv = image.VerticalOffsetPt;
        _pha = image.HorizontalAnchor; _pva = image.VerticalAnchor;
        image.HorizontalOffsetPt = horizontalOffsetPt; image.VerticalOffsetPt = verticalOffsetPt;
        image.HorizontalAnchor = horizontalAnchor; image.VerticalAnchor = verticalAnchor;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ImageAt(context) is not { } image) return;
        image.HorizontalOffsetPt = _ph; image.VerticalOffsetPt = _pv;
        image.HorizontalAnchor = _pha; image.VerticalAnchor = _pva;
        _applied = false;
    }

    private InlineImage? ImageAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Image : null;
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

/// <summary>
/// Replace the document's bibliography source list, snapshotting the previous list for undo.
/// </summary>
public sealed class ReplaceSourcesCommand(IReadOnlyList<Source> sources) : IDocumentCommand
{
    private Source[]? _previous;
    private readonly Source[] _replacement = sources.ToArray();

    public string Label => "Manage Sources";

    public int EstimatedBytes => 256 + (_replacement.Length * 256);

    public void Apply(IDocumentCommandContext context)
    {
        _previous = context.Document.Sources.ToArray();
        context.Document.Sources.Clear();
        context.Document.Sources.AddRange(_replacement);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;
        context.Document.Sources.Clear();
        context.Document.Sources.AddRange(_previous);
    }
}
