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
    private double _prevBrightness, _prevContrast, _prevSaturation, _prevTransparency;
    private bool _applied;

    public string Label => "Reset Picture";

    public void Apply(IDocumentCommandContext context)
    {
        if (ImageAt(context) is not { } image) return;
        _pw = image.WidthPt; _ph = image.HeightPt;
        _prevAngle = image.RotationAngle; _prevFlipH = image.FlipH; _prevFlipV = image.FlipV;
        _pl = image.CropLeft; _pr = image.CropRight; _pt = image.CropTop; _pb = image.CropBottom;
        _prevBrightness   = image.BrightnessPct;
        _prevContrast     = image.ContrastPct;
        _prevSaturation   = image.SaturationPct;
        _prevTransparency = image.TransparencyPct;
        image.WidthPt = naturalWidthPt; image.HeightPt = naturalHeightPt;
        image.RotationAngle = 0; image.FlipH = false; image.FlipV = false;
        image.CropLeft = image.CropRight = image.CropTop = image.CropBottom = 0;
        // Reset adjustments to neutral.
        image.BrightnessPct   = 0;
        image.ContrastPct     = 0;
        image.SaturationPct   = 100;
        image.TransparencyPct = 0;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ImageAt(context) is not { } image) return;
        image.WidthPt = _pw; image.HeightPt = _ph;
        image.RotationAngle = _prevAngle; image.FlipH = _prevFlipH; image.FlipV = _prevFlipV;
        image.CropLeft = _pl; image.CropRight = _pr; image.CropTop = _pt; image.CropBottom = _pb;
        image.BrightnessPct   = _prevBrightness;
        image.ContrastPct     = _prevContrast;
        image.SaturationPct   = _prevSaturation;
        image.TransparencyPct = _prevTransparency;
        _applied = false;
    }

    private InlineImage? ImageAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Image : null;
}

/// <summary>
/// Set the Picture Format > Adjust parameters (brightness, contrast, saturation, transparency) on the
/// inline image at the given paragraph/run indices, snapshotting prior values for undo.
/// Brightness and contrast are in percent offset (-100..100, 0=neutral).
/// Saturation is in percent (0=grey, 100=normal, 400=max).
/// Transparency is in percent (0=opaque, 100=transparent).
/// </summary>
public sealed class SetImageAdjustCommand(
    int paragraphIndex, int runIndex,
    double brightnessPct, double contrastPct, double saturationPct, double transparencyPct)
    : IDocumentCommand
{
    private double _prevBrightness, _prevContrast, _prevSaturation, _prevTransparency;
    private bool _applied;

    public string Label => "Picture Adjust";

    public void Apply(IDocumentCommandContext context)
    {
        if (ImageAt(context) is not { } image) return;
        _prevBrightness   = image.BrightnessPct;
        _prevContrast     = image.ContrastPct;
        _prevSaturation   = image.SaturationPct;
        _prevTransparency = image.TransparencyPct;
        image.BrightnessPct   = brightnessPct;
        image.ContrastPct     = contrastPct;
        image.SaturationPct   = saturationPct;
        image.TransparencyPct = transparencyPct;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ImageAt(context) is not { } image) return;
        image.BrightnessPct   = _prevBrightness;
        image.ContrastPct     = _prevContrast;
        image.SaturationPct   = _prevSaturation;
        image.TransparencyPct = _prevTransparency;
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
/// Reorder the z-index of any floating drawing object (<see cref="InlineImage"/>,
/// <see cref="Shape"/>, <see cref="Chart"/>, <see cref="SmartArt"/> or <see cref="WordArt"/>)
/// across ALL floating objects in the document. Supports four Word-style arrange operations:
/// BringToFront (max+1), SendToBack (min-1), BringForward (swap with the next-higher neighbour),
/// SendBackward (swap with the next-lower neighbour). The operation is undoable:
/// <see cref="Revert"/> restores all ZOrderIndex values exactly as they were before
/// <see cref="Apply"/>. The command is a no-op when no floating objects exist.
/// </summary>
public sealed class ChangeZOrderCommand(int paragraphIndex, int runIndex, ZOrderOperation operation) : IDocumentCommand
{
    // Internal handle: index pair + stable getter/setter delegates so we can manipulate
    // ZOrderIndex on any object type without a shared interface.
    private sealed record FloatingRef(int Bi, int Ri, Func<int> GetZ, Action<int> SetZ);

    private (int Bi, int Ri, int OldZ)[]? _snapshot;

    public string Label => operation switch
    {
        ZOrderOperation.BringToFront => "Bring to Front",
        ZOrderOperation.SendToBack => "Send to Back",
        ZOrderOperation.BringForward => "Bring Forward",
        ZOrderOperation.SendBackward => "Send Backward",
        _ => "Z-Order"
    };

    public void Apply(IDocumentCommandContext context)
    {
        var all = CollectFloating(context.Document);
        if (all.Count == 0) return;

        _snapshot = all.Select(t => (t.Bi, t.Ri, t.GetZ())).ToArray();

        var target = all.FirstOrDefault(t => t.Bi == paragraphIndex && t.Ri == runIndex);
        if (target is null) return;

        switch (operation)
        {
            case ZOrderOperation.BringToFront:
            {
                var max = all.Max(t => t.GetZ());
                target.SetZ(max + 1);
                break;
            }
            case ZOrderOperation.SendToBack:
            {
                var min = all.Min(t => t.GetZ());
                target.SetZ(min - 1);
                break;
            }
            case ZOrderOperation.BringForward:
            {
                var targetZ = target.GetZ();
                var neighbor = all
                    .Where(t => t.GetZ() > targetZ)
                    .OrderBy(t => t.GetZ())
                    .FirstOrDefault();
                if (neighbor is not null)
                {
                    target.SetZ(neighbor.GetZ());
                    neighbor.SetZ(targetZ);
                }
                break;
            }
            case ZOrderOperation.SendBackward:
            {
                var targetZ = target.GetZ();
                var neighbor = all
                    .Where(t => t.GetZ() < targetZ)
                    .OrderByDescending(t => t.GetZ())
                    .FirstOrDefault();
                if (neighbor is not null)
                {
                    target.SetZ(neighbor.GetZ());
                    neighbor.SetZ(targetZ);
                }
                break;
            }
        }
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_snapshot is null) return;
        var all = CollectFloating(context.Document);
        foreach (var (bi, ri, oldZ) in _snapshot)
        {
            var t = all.FirstOrDefault(x => x.Bi == bi && x.Ri == ri);
            t?.SetZ(oldZ);
        }
        _snapshot = null;
    }

    private static List<FloatingRef> CollectFloating(TextDocument doc)
    {
        var result = new List<FloatingRef>();
        for (var b = 0; b < doc.Blocks.Count; b++)
        {
            if (doc.Blocks[b] is not Paragraph para) continue;
            for (var r = 0; r < para.Runs.Count; r++)
            {
                var run = para.Runs[r];
                if (run.Image is { IsFloating: true } img)
                {
                    result.Add(new FloatingRef(b, r, () => img.ZOrderIndex, z => img.ZOrderIndex = z));
                }
                else if (run.Shape is { IsFloating: true } shape && shape.Placement is { } sp)
                {
                    result.Add(new FloatingRef(b, r, () => sp.ZOrderIndex, z => sp.ZOrderIndex = z));
                }
                else if (run.Chart is { IsFloating: true } chart && chart.Placement is { } cp)
                {
                    result.Add(new FloatingRef(b, r, () => cp.ZOrderIndex, z => cp.ZOrderIndex = z));
                }
                else if (run.SmartArt is { IsFloating: true } smartArt && smartArt.Placement is { } sap)
                {
                    result.Add(new FloatingRef(b, r, () => sap.ZOrderIndex, z => sap.ZOrderIndex = z));
                }
                else if (run.WordArt is { IsFloating: true } wordArt && wordArt.Placement is { } wap)
                {
                    result.Add(new FloatingRef(b, r, () => wap.ZOrderIndex, z => wap.ZOrderIndex = z));
                }
                else if (run.DrawingGroup is { } grp)
                {
                    result.Add(new FloatingRef(b, r, () => grp.Placement.ZOrderIndex, z => grp.Placement.ZOrderIndex = z));
                }
            }
        }
        return result;
    }
}

/// <summary>The four Word-style z-order arrange operations.</summary>
public enum ZOrderOperation
{
    BringToFront,
    SendToBack,
    BringForward,
    SendBackward
}

/// <summary>
/// Switches a drawing object's wrapping between <see cref="ImageWrapping.Inline"/> and a floating
/// mode (<see cref="ImageWrapping.Square"/> by default), applying to <see cref="InlineImage"/>,
/// <see cref="Shape"/>, <see cref="Chart"/>, <see cref="SmartArt"/> and <see cref="WordArt"/>.
/// When converting Inline to floating, the wrapping is set to <paramref name="floatingWrapping"/>
/// (Square if omitted) and <see cref="FloatingPlacement"/> is created/populated. When converting
/// floating to Inline, the wrapping is set to Inline (placement fields are preserved so a
/// subsequent float-again restores the last position). Undoable.
/// </summary>
public sealed class ToggleObjectWrappingCommand(
    int paragraphIndex,
    int runIndex,
    ImageWrapping floatingWrapping = ImageWrapping.Square) : IDocumentCommand
{
    private ImageWrapping _previousWrapping;
    private bool _applied;

    public string Label => "Set Wrap";

    public void Apply(IDocumentCommandContext context)
    {
        ApplyTo(context, floatingWrapping);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied) return;
        ApplyTo(context, _previousWrapping);
        _applied = false;
    }

    private void ApplyTo(IDocumentCommandContext context, ImageWrapping targetWrapping)
    {
        if (context.Document.Blocks[paragraphIndex] is not Paragraph p) return;
        if (runIndex < 0 || runIndex >= p.Runs.Count) return;
        var run = p.Runs[runIndex];

        if (run.Image is { } img)
        {
            _previousWrapping = img.Wrapping;
            img.Wrapping = targetWrapping;
        }
        else if (run.Shape is { } shape)
        {
            shape.Placement ??= new FloatingPlacement();
            _previousWrapping = shape.Placement.Wrapping;
            shape.Placement.Wrapping = targetWrapping;
        }
        else if (run.Chart is { } chart)
        {
            chart.Placement ??= new FloatingPlacement();
            _previousWrapping = chart.Placement.Wrapping;
            chart.Placement.Wrapping = targetWrapping;
        }
        else if (run.SmartArt is { } smartArt)
        {
            smartArt.Placement ??= new FloatingPlacement();
            _previousWrapping = smartArt.Placement.Wrapping;
            smartArt.Placement.Wrapping = targetWrapping;
        }
        else if (run.WordArt is { } wordArt)
        {
            wordArt.Placement ??= new FloatingPlacement();
            _previousWrapping = wordArt.Placement.Wrapping;
            wordArt.Placement.Wrapping = targetWrapping;
        }
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

// ── Shape / Drawing commands (Drawing Format contextual tab) ──────────────────────────────────────

/// <summary>
/// Change the <see cref="Shape.Kind"/> of the inline shape at the given paragraph/run indices,
/// snapshotting the prior kind for undo.
/// </summary>
public sealed class SetShapeKindCommand(int paragraphIndex, int runIndex, ShapeKind kind) : IDocumentCommand
{
    private ShapeKind _previous;
    private bool _applied;

    public string Label => "Change Shape";

    public void Apply(IDocumentCommandContext context)
    {
        if (ShapeAt(context) is not { } shape) return;
        _previous = shape.Kind;
        shape.Kind = kind;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ShapeAt(context) is not { } shape) return;
        shape.Kind = _previous;
        _applied = false;
    }

    private Shape? ShapeAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Shape : null;
}

/// <summary>
/// Set the fill colour of the inline shape at the given paragraph/run indices, snapshotting the
/// prior colour for undo. Pass null to remove the fill.
/// </summary>
public sealed class SetShapeFillCommand(int paragraphIndex, int runIndex, string? colorHex) : IDocumentCommand
{
    private string? _previous;
    private bool _applied;

    public string Label => "Shape Fill";

    public void Apply(IDocumentCommandContext context)
    {
        if (ShapeAt(context) is not { } shape) return;
        _previous = shape.FillColorHex;
        shape.FillColorHex = colorHex;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ShapeAt(context) is not { } shape) return;
        shape.FillColorHex = _previous;
        _applied = false;
    }

    private Shape? ShapeAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Shape : null;
}

/// <summary>
/// Set the outline (color hex, width in points, dash token) of the inline shape at the given
/// paragraph/run indices, snapshotting prior values for undo. Pass null colorHex to remove the outline.
/// </summary>
public sealed class SetShapeOutlineCommand(int paragraphIndex, int runIndex,
    string? colorHex, double widthPt, string? dash) : IDocumentCommand
{
    private string? _prevColor;
    private double _prevWidth;
    private string? _prevDash;
    private bool _applied;

    public string Label => "Shape Outline";

    public void Apply(IDocumentCommandContext context)
    {
        if (ShapeAt(context) is not { } shape) return;
        _prevColor = shape.OutlineColorHex; _prevWidth = shape.OutlineWidthPt; _prevDash = shape.OutlineDash;
        shape.OutlineColorHex = colorHex; shape.OutlineWidthPt = widthPt; shape.OutlineDash = dash;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ShapeAt(context) is not { } shape) return;
        shape.OutlineColorHex = _prevColor; shape.OutlineWidthPt = _prevWidth; shape.OutlineDash = _prevDash;
        _applied = false;
    }

    private Shape? ShapeAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Shape : null;
}

/// <summary>
/// Set the size (points) of the inline shape at the given paragraph/run indices, snapshotting the
/// prior size for undo.
/// </summary>
public sealed class SetShapeSizeCommand(int paragraphIndex, int runIndex, double widthPt, double heightPt) : IDocumentCommand
{
    private double _prevWidth;
    private double _prevHeight;
    private bool _applied;

    public string Label => "Resize Shape";

    public void Apply(IDocumentCommandContext context)
    {
        if (ShapeAt(context) is not { } shape) return;
        _prevWidth = shape.WidthPt; _prevHeight = shape.HeightPt;
        shape.WidthPt = widthPt; shape.HeightPt = heightPt;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ShapeAt(context) is not { } shape) return;
        shape.WidthPt = _prevWidth; shape.HeightPt = _prevHeight;
        _applied = false;
    }

    private Shape? ShapeAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Shape : null;
}

/// <summary>
/// Set the alt-text accessibility description on the inline shape at the given paragraph/run indices,
/// snapshotting the prior value for undo.
/// </summary>
public sealed class SetShapeAltTextCommand(int paragraphIndex, int runIndex, string? altText) : IDocumentCommand
{
    private string? _previous;
    private bool _applied;

    public string Label => "Shape Alt Text";

    public void Apply(IDocumentCommandContext context)
    {
        if (ShapeAt(context) is not { } shape) return;
        _previous = shape.AltText;
        shape.AltText = altText;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ShapeAt(context) is not { } shape) return;
        shape.AltText = _previous;
        _applied = false;
    }

    private Shape? ShapeAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Shape : null;
}

/// <summary>
/// Set the text direction on the inline text-box shape at the given paragraph/run indices,
/// snapshotting the prior value for undo. No-op for non-text-box shapes.
/// </summary>
public sealed class SetShapeTextDirectionCommand(int paragraphIndex, int runIndex, ShapeTextDirection direction) : IDocumentCommand
{
    private ShapeTextDirection _previous;
    private bool _applied;

    public string Label => "Text Direction";

    public void Apply(IDocumentCommandContext context)
    {
        if (ShapeAt(context) is not { } shape) return;
        _previous = shape.TextDirection;
        shape.TextDirection = direction;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ShapeAt(context) is not { } shape) return;
        shape.TextDirection = _previous;
        _applied = false;
    }

    private Shape? ShapeAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Shape : null;
}

/// <summary>
/// Set the alt-text accessibility description on the WordArt at the given paragraph/run indices,
/// snapshotting the prior value for undo.
/// </summary>
public sealed class SetWordArtAltTextCommand(int paragraphIndex, int runIndex, string? altText) : IDocumentCommand
{
    private string? _previous;
    private bool _applied;

    public string Label => "WordArt Alt Text";

    public void Apply(IDocumentCommandContext context)
    {
        if (WordArtAt(context) is not { } wordArt) return;
        _previous = wordArt.AltText;
        wordArt.AltText = altText;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || WordArtAt(context) is not { } wordArt) return;
        wordArt.AltText = _previous;
        _applied = false;
    }

    private WordArt? WordArtAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].WordArt : null;
}

/// <summary>
/// Set the WordArt style preset at the given paragraph/run indices, snapshotting the prior value for undo.
/// </summary>
public sealed class SetWordArtStyleCommand(int paragraphIndex, int runIndex, WordArtStyle style) : IDocumentCommand
{
    private WordArtStyle _previous;
    private bool _applied;

    public string Label => "WordArt Style";

    public void Apply(IDocumentCommandContext context)
    {
        if (WordArtAt(context) is not { } wordArt) return;
        _previous = wordArt.Style;
        wordArt.Style = style;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || WordArtAt(context) is not { } wordArt) return;
        wordArt.Style = _previous;
        _applied = false;
    }

    private WordArt? WordArtAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].WordArt : null;
}

// ── Group / Ungroup commands (Phase 4 — floating multi-select) ──────────────────────────────────

/// <summary>
/// Groups two or more selected floating objects into a single <see cref="DrawingGroup"/>. The members'
/// anchor runs are removed from their paragraphs; a new run carrying the group is inserted at the
/// location of the first member. Undoable via <see cref="Revert"/>.
/// </summary>
public sealed class GroupFloatingObjectsCommand : IDocumentCommand
{
    private List<(int Bi, int Ri, Run RemovedRun)>? _snapshot;
    private (int Bi, int Ri)? _groupLocation;

    public string Label => "Group";

    public GroupFloatingObjectsCommand(IReadOnlyList<(int Bi, int Ri)> members)
    {
        _members = [.. members.OrderBy(m => m.Bi).ThenBy(m => m.Ri)];
    }

    private readonly (int Bi, int Ri)[] _members;

    public void Apply(IDocumentCommandContext context)
    {
        var doc = context.Document;
        var group = new DrawingGroup();
        double minH = double.MaxValue, minV = double.MaxValue;
        double maxH = double.MinValue, maxV = double.MinValue;

        foreach (var (bi, ri) in _members)
        {
            if (doc.Blocks[bi] is not Paragraph p || ri >= p.Runs.Count) continue;
            var (obj, widthPt, heightPt, placement) = ExtractFloatingInfo(p.Runs[ri]);
            if (obj is null || placement is null) continue;

            group.Children.Add(obj);
            group.ChildOffsets.Add((placement.HorizontalOffsetPt, placement.VerticalOffsetPt));

            if (placement.HorizontalOffsetPt < minH) minH = placement.HorizontalOffsetPt;
            if (placement.VerticalOffsetPt < minV) minV = placement.VerticalOffsetPt;
            if (placement.HorizontalOffsetPt + widthPt > maxH) maxH = placement.HorizontalOffsetPt + widthPt;
            if (placement.VerticalOffsetPt + heightPt > maxV) maxV = placement.VerticalOffsetPt + heightPt;
        }

        if (group.Children.Count < 2) return;

        if (minH == double.MaxValue) minH = 0;
        if (minV == double.MaxValue) minV = 0;
        group.WidthPt = Math.Max(1, maxH - minH);
        group.HeightPt = Math.Max(1, maxV - minV);

        for (var i = 0; i < group.ChildOffsets.Count; i++)
        {
            var (ox, oy) = group.ChildOffsets[i];
            group.ChildOffsets[i] = (ox - minH, oy - minV);
        }

        var (firstBi, firstRi) = _members[0];
        FloatingPlacement? firstPlacement = null;
        if (doc.Blocks[firstBi] is Paragraph fp && firstRi < fp.Runs.Count)
            firstPlacement = ExtractFloatingInfo(fp.Runs[firstRi]).Placement;

        group.Placement = new FloatingPlacement
        {
            Wrapping = firstPlacement?.Wrapping ?? ImageWrapping.Square,
            HorizontalOffsetPt = minH,
            VerticalOffsetPt = minV,
            HorizontalAnchor = firstPlacement?.HorizontalAnchor ?? HorizontalAnchor.Column,
            VerticalAnchor = firstPlacement?.VerticalAnchor ?? VerticalAnchor.Paragraph,
            ZOrderIndex = firstPlacement?.ZOrderIndex ?? 0
        };

        _snapshot = [];
        foreach (var (bi, ri) in _members.Reverse())
        {
            if (doc.Blocks[bi] is not Paragraph p || ri >= p.Runs.Count) continue;
            _snapshot.Add((bi, ri, p.Runs[ri]));
            p.Runs.RemoveAt(ri);
        }

        if (doc.Blocks[firstBi] is not Paragraph insertPara) return;
        var insertRi = Math.Min(firstRi, insertPara.Runs.Count);
        insertPara.Runs.Insert(insertRi, Run.FromDrawingGroup(group));
        _groupLocation = (firstBi, insertRi);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_snapshot is null || _groupLocation is null) return;
        var doc = context.Document;
        var (gBi, gRi) = _groupLocation.Value;
        if (doc.Blocks[gBi] is Paragraph gPara && gRi < gPara.Runs.Count)
            gPara.Runs.RemoveAt(gRi);
        foreach (var (bi, ri, run) in ((IEnumerable<(int, int, Run)>)_snapshot).Reverse())
        {
            if (doc.Blocks[bi] is not Paragraph p) continue;
            p.Runs.Insert(Math.Min(ri, p.Runs.Count), run);
        }
        _snapshot = null;
        _groupLocation = null;
    }

    internal static (object? Obj, double WidthPt, double HeightPt, FloatingPlacement? Placement)
        ExtractFloatingInfo(Run run)
    {
        if (run.Image is { IsFloating: true } img)
            return (img, img.WidthPt, img.HeightPt, new FloatingPlacement
            {
                Wrapping = img.Wrapping,
                HorizontalOffsetPt = img.HorizontalOffsetPt,
                VerticalOffsetPt = img.VerticalOffsetPt,
                HorizontalAnchor = img.HorizontalAnchor,
                VerticalAnchor = img.VerticalAnchor,
                ZOrderIndex = img.ZOrderIndex
            });
        if (run.Shape is { IsFloating: true } s && s.Placement is { } sp) return (s, s.WidthPt, s.HeightPt, sp);
        if (run.Chart is { IsFloating: true } c && c.Placement is { } cp) return (c, c.WidthPt, c.HeightPt, cp);
        if (run.SmartArt is { IsFloating: true } sa && sa.Placement is { } sap) return (sa, sa.WidthPt, sa.HeightPt, sap);
        if (run.WordArt is { IsFloating: true } wa && wa.Placement is { } wap)
            return (wa, wa.FontSizePt * Math.Max(1, wa.Text.Length) * 0.62, wa.FontSizePt * 1.6, wap);
        return (null, 0, 0, null);
    }
}

/// <summary>
/// Ungroups a <see cref="DrawingGroup"/> back into individual floating objects, restoring each
/// member's absolute placement from the group origin + per-child offset.  Undoable.
/// </summary>
public sealed class UngroupFloatingObjectsCommand(int paragraphIndex, int runIndex) : IDocumentCommand
{
    private DrawingGroup? _group;
    private bool _applied;

    public string Label => "Ungroup";

    public void Apply(IDocumentCommandContext context)
    {
        var doc = context.Document;
        if (doc.Blocks[paragraphIndex] is not Paragraph p) return;
        if (runIndex < 0 || runIndex >= p.Runs.Count) return;
        if (p.Runs[runIndex].DrawingGroup is not { } group || !group.IsValid) return;
        _group = group;

        p.Runs.RemoveAt(runIndex);

        for (var i = 0; i < group.Children.Count; i++)
        {
            var child = group.Children[i];
            var (ox, oy) = i < group.ChildOffsets.Count ? group.ChildOffsets[i] : (0.0, 0.0);
            var absH = group.Placement.HorizontalOffsetPt + ox;
            var absV = group.Placement.VerticalOffsetPt + oy;
            var z = group.Placement.ZOrderIndex + i;

            Run? memberRun = child switch
            {
                InlineImage img => RestoreImagePlacement(img, group.Placement, absH, absV, z),
                Shape shape     => RestoreShapePlacement(shape, group.Placement, absH, absV, z),
                Chart chart     => RestoreChartPlacement(chart, group.Placement, absH, absV, z),
                SmartArt sa     => RestoreSmartArtPlacement(sa, group.Placement, absH, absV, z),
                WordArt wa      => RestoreWordArtPlacement(wa, group.Placement, absH, absV, z),
                _               => null
            };
            if (memberRun is null) continue;
            p.Runs.Insert(runIndex + i, memberRun);
        }

        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || _group is null) return;
        var doc = context.Document;
        if (doc.Blocks[paragraphIndex] is not Paragraph p) return;
        var count = Math.Min(_group.Children.Count, p.Runs.Count - runIndex);
        for (var i = 0; i < count; i++) p.Runs.RemoveAt(runIndex);
        p.Runs.Insert(runIndex, Run.FromDrawingGroup(_group));
        _applied = false;
        _group = null;
    }

    private static Run RestoreImagePlacement(InlineImage img, FloatingPlacement gp, double h, double v, int z)
    {
        img.Wrapping = gp.Wrapping; img.HorizontalOffsetPt = h; img.VerticalOffsetPt = v;
        img.HorizontalAnchor = gp.HorizontalAnchor; img.VerticalAnchor = gp.VerticalAnchor; img.ZOrderIndex = z;
        return Run.FromImage(img);
    }
    private static Run RestoreShapePlacement(Shape s, FloatingPlacement gp, double h, double v, int z)
    {
        s.Placement ??= new FloatingPlacement();
        s.Placement.Wrapping = gp.Wrapping; s.Placement.HorizontalOffsetPt = h; s.Placement.VerticalOffsetPt = v;
        s.Placement.HorizontalAnchor = gp.HorizontalAnchor; s.Placement.VerticalAnchor = gp.VerticalAnchor; s.Placement.ZOrderIndex = z;
        return Run.FromShape(s);
    }
    private static Run RestoreChartPlacement(Chart c, FloatingPlacement gp, double h, double v, int z)
    {
        c.Placement ??= new FloatingPlacement { Wrapping = gp.Wrapping };
        c.Placement.Wrapping = gp.Wrapping; c.Placement.HorizontalOffsetPt = h; c.Placement.VerticalOffsetPt = v;
        c.Placement.HorizontalAnchor = gp.HorizontalAnchor; c.Placement.VerticalAnchor = gp.VerticalAnchor; c.Placement.ZOrderIndex = z;
        return Run.FromChart(c);
    }
    private static Run RestoreSmartArtPlacement(SmartArt sa, FloatingPlacement gp, double h, double v, int z)
    {
        sa.Placement ??= new FloatingPlacement { Wrapping = gp.Wrapping };
        sa.Placement.Wrapping = gp.Wrapping; sa.Placement.HorizontalOffsetPt = h; sa.Placement.VerticalOffsetPt = v;
        sa.Placement.HorizontalAnchor = gp.HorizontalAnchor; sa.Placement.VerticalAnchor = gp.VerticalAnchor; sa.Placement.ZOrderIndex = z;
        return Run.FromSmartArt(sa);
    }
    private static Run RestoreWordArtPlacement(WordArt wa, FloatingPlacement gp, double h, double v, int z)
    {
        wa.Placement ??= new FloatingPlacement { Wrapping = gp.Wrapping };
        wa.Placement.Wrapping = gp.Wrapping; wa.Placement.HorizontalOffsetPt = h; wa.Placement.VerticalOffsetPt = v;
        wa.Placement.HorizontalAnchor = gp.HorizontalAnchor; wa.Placement.VerticalAnchor = gp.VerticalAnchor; wa.Placement.ZOrderIndex = z;
        return Run.FromWordArt(wa);
    }
}
