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
    private ParagraphFormatRevision? _previousRevision;

    public string Label => "Paragraph Formatting";

    public void Apply(IDocumentCommandContext context)
    {
        var paragraph = ParagraphAt(context, index);
        _previous = paragraph.Formatting;
        _previousRevision = paragraph.ParagraphFormatRevision;
        paragraph.Formatting = formatting;
        if (TrackedFormattingRevisionFactory.ShouldTrack(context.Document)
            && formatting != _previous
            && paragraph.ParagraphFormatRevision is null)
        {
            paragraph.ParagraphFormatRevision = TrackedFormattingRevisionFactory.ForParagraph(_previous, context.RevisionAuthor);
        }
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is not null)
        {
            ParagraphAt(context, index).Formatting = _previous;
            ParagraphAt(context, index).ParagraphFormatRevision = _previousRevision;
        }
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
    private FormatRevision? _previousRevision;

    public string Label => "Character Formatting";

    public void Apply(IDocumentCommandContext context)
    {
        var run = ((Paragraph)context.Document.Blocks[paragraphIndex]).Runs[runIndex];
        _previous = run.Formatting;
        _previousRevision = run.FormatRevision;
        run.Formatting = formatting;
        if (TrackedFormattingRevisionFactory.ShouldTrack(context.Document)
            && formatting != _previous
            && run.FormatRevision is null)
        {
            run.FormatRevision = TrackedFormattingRevisionFactory.ForRun(_previous, context.RevisionAuthor);
        }
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is not null)
        {
            var run = ((Paragraph)context.Document.Blocks[paragraphIndex]).Runs[runIndex];
            run.Formatting = _previous;
            run.FormatRevision = _previousRevision;
        }
    }
}

/// <summary>
/// Replace a paragraph's run list wholesale (snapshotting the prior runs and drop-cap intent for
/// undo). Used by edits that restructure a paragraph's runs — e.g. applying a drop cap, which splits
/// the first run so the leading letter becomes its own enlarged run. The replacement runs are
/// produced by <paramref name="rebuild"/> from the paragraph; on undo the exact original run objects
/// and prior drop-cap intent are restored.
/// </summary>
public sealed class ReplaceParagraphRunsCommand(int paragraphIndex, Action<Paragraph> rebuild) : IDocumentCommand
{
    private List<Run>? _previous;
    private DropCapLayoutIntent? _previousDropCap;

    public string Label => "Format";

    public void Apply(IDocumentCommandContext context)
    {
        var paragraph = (Paragraph)context.Document.Blocks[paragraphIndex];
        _previous = [.. paragraph.Runs];
        _previousDropCap = paragraph.DropCap;
        rebuild(paragraph);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;
        var paragraph = (Paragraph)context.Document.Blocks[paragraphIndex];
        var runs = paragraph.Runs;
        runs.Clear();
        runs.AddRange(_previous);
        paragraph.DropCap = _previousDropCap;
    }
}

/// <summary>
/// Insert a blank row into the table at <paramref name="blockIndex"/>, at <paramref name="rowIndex"/>
/// (clamped to the row count). The new row gets one empty cell per grid column. When the insert
/// position falls strictly INSIDE a vertical-merged run for a given grid column (the cell above is
/// Restart or Continue AND the cell below is Continue), the new cell inherits
/// <see cref="VerticalMergeState.Continue"/> so the merge is extended rather than severed (BF2).
/// Reversible.
/// </summary>
public sealed class InsertTableRowCommand(int blockIndex, int rowIndex) : IDocumentCommand
{
    private int _appliedAt = -1;

    public string Label => "Insert Row";

    public void Apply(IDocumentCommandContext context)
    {
        var table = TableAt(context, blockIndex);
        var at = Math.Clamp(rowIndex, 0, table.Rows.Count);

        // Compute the total grid width from the first row (or ColumnCount fallback).
        var gridWidth = table.Rows.Count > 0
            ? TableColumnHelpers.RowGridWidth(table.Rows[0])
            : Math.Max(table.ColumnCount, 1);

        var row = new TableRow();
        for (var gc = 0; gc < gridWidth; gc++)
        {
            // BF2: Determine whether this grid column is strictly inside a vertical-merged run
            // at the insert position.  A position is "strictly inside" when the row above
            // (index at-1) carries Restart or Continue for this column AND the row below
            // (index at, before insertion) carries Continue.
            var mergeState = VerticalMergeState.None;
            if (at > 0 && at < table.Rows.Count)
            {
                var cellAboveIdx = TableColumnHelpers.GridColumnToCellIndex(table.Rows[at - 1], gc);
                var cellBelowIdx = TableColumnHelpers.GridColumnToCellIndex(table.Rows[at], gc);
                if (cellAboveIdx >= 0 && cellBelowIdx >= 0)
                {
                    var aboveState = table.Rows[at - 1].Cells[cellAboveIdx].VerticalMerge;
                    var belowState = table.Rows[at].Cells[cellBelowIdx].VerticalMerge;
                    if ((aboveState == VerticalMergeState.Restart || aboveState == VerticalMergeState.Continue)
                        && belowState == VerticalMergeState.Continue)
                        mergeState = VerticalMergeState.Continue;
                }
            }
            row.Cells.Add(new TableCell(string.Empty) { VerticalMerge = mergeState });
        }

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
/// <para>BF1: When the deleted row contains a <see cref="VerticalMergeState.Restart"/> cell, the
/// cell directly below it in the same grid column is promoted from
/// <see cref="VerticalMergeState.Continue"/> to <see cref="VerticalMergeState.Restart"/> so the
/// vertical merge continues from the next row (matching Word's behaviour). The prior states of any
/// promoted cells are snapshotted for exact undo restoration.</para>
/// </summary>
public sealed class DeleteTableRowCommand(int blockIndex, int rowIndex) : IDocumentCommand
{
    private TableRow? _removed;
    private int _removedAt = -1;
    // Snapshot of cells that were promoted (BF1): (rowIndexAfterDeletion, cellListIndex, priorState).
    private (int Row, int CellIdx, VerticalMergeState PriorState)[]? _promoted;

    public string Label => "Delete Row";

    public void Apply(IDocumentCommandContext context)
    {
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        if (table.Rows.Count <= 1 || rowIndex < 0 || rowIndex >= table.Rows.Count)
            return;
        _removedAt = rowIndex;
        _removed = table.Rows[rowIndex];

        // BF1: Before removing the row, promote any orphaned vertical-merge continuations.
        // For each cell in the deleted row that is a Restart head, the cell directly below it
        // (same grid column, next row) must become Restart so the merge survives.
        var nextRowIndex = rowIndex + 1;
        if (nextRowIndex < table.Rows.Count)
        {
            var promotions = new List<(int, int, VerticalMergeState)>();
            var deletedRow = table.Rows[rowIndex];
            var nextRow = table.Rows[nextRowIndex];
            // Walk each grid column of the deleted row.
            var gridPos = 0;
            foreach (var cell in deletedRow.Cells)
            {
                var span = Math.Max(1, cell.GridSpan);
                // Only the grid column at the START of this cell matters for vertical merge lookup.
                var gc = gridPos;
                if (cell.VerticalMerge == VerticalMergeState.Restart)
                {
                    var nextCellIdx = TableColumnHelpers.GridColumnToCellIndex(nextRow, gc);
                    if (nextCellIdx >= 0 && nextRow.Cells[nextCellIdx].VerticalMerge == VerticalMergeState.Continue)
                    {
                        // The row below deletion index is currently at nextRowIndex, but after the
                        // actual RemoveAt it will be at rowIndex. Record the post-deletion row index.
                        promotions.Add((rowIndex, nextCellIdx, VerticalMergeState.Continue));
                        nextRow.Cells[nextCellIdx].VerticalMerge = VerticalMergeState.Restart;
                    }
                }
                gridPos += span;
            }
            _promoted = promotions.Count > 0 ? [.. promotions] : null;
        }

        table.Rows.RemoveAt(rowIndex);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_removed is null || _removedAt < 0)
            return;
        var table = InsertTableRowCommand.TableAt(context, blockIndex);

        // Re-insert the removed row first so that the row indices in _promoted are valid.
        table.Rows.Insert(_removedAt, _removed);

        // BF1 undo: restore promoted cells to their prior state.
        if (_promoted is not null)
        {
            // After re-insertion, the next row is at _removedAt + 1.
            var nextRowAfterUndo = _removedAt + 1;
            foreach (var (_, cellIdx, priorState) in _promoted)
            {
                if (nextRowAfterUndo < table.Rows.Count && cellIdx < table.Rows[nextRowAfterUndo].Cells.Count)
                    table.Rows[nextRowAfterUndo].Cells[cellIdx].VerticalMerge = priorState;
            }
            _promoted = null;
        }

        _removed = null;
        _removedAt = -1;
    }
}

/// <summary>
/// Shared grid-column helpers used by column insert/delete/merge commands. These helpers exist because
/// the cell-list index does NOT equal the grid-column index when any cell in the row has
/// <see cref="TableCell.GridSpan"/> &gt; 1 (horizontal merge).
/// </summary>
internal static class TableColumnHelpers
{
    /// <summary>
    /// Maps a target GRID-column index to the <see cref="TableRow.Cells"/> list index for
    /// <paramref name="row"/>, accounting for each preceding cell's <see cref="TableCell.GridSpan"/>.
    /// Returns the index of the first cell whose cumulative grid span covers the target column, or -1
    /// if the target is beyond the row's total grid width.
    /// </summary>
    internal static int GridColumnToCellIndex(TableRow row, int targetGridColumn)
    {
        var gridPos = 0;
        for (var i = 0; i < row.Cells.Count; i++)
        {
            var span = Math.Max(1, row.Cells[i].GridSpan);
            if (targetGridColumn < gridPos + span)
                return i;
            gridPos += span;
        }
        return -1; // target grid column is beyond the row's extent
    }

    /// <summary>
    /// Returns the total number of grid columns for <paramref name="row"/> (sum of all cell GridSpans).
    /// </summary>
    internal static int RowGridWidth(TableRow row) =>
        row.Cells.Sum(c => Math.Max(1, c.GridSpan));

    /// <summary>
    /// Maps a <see cref="TableRow.Cells"/> list index to the GRID-column index it occupies (i.e. the
    /// sum of GridSpans of all preceding cells).  Returns -1 if <paramref name="cellIndex"/> is out of
    /// range.
    /// </summary>
    internal static int CellIndexToGridColumn(TableRow row, int cellIndex)
    {
        if (cellIndex < 0 || cellIndex >= row.Cells.Count)
            return -1;
        var gridPos = 0;
        for (var i = 0; i < cellIndex; i++)
            gridPos += Math.Max(1, row.Cells[i].GridSpan);
        return gridPos;
    }
}

/// <summary>
/// Insert a blank column at <paramref name="columnIndex"/> (clamped) into the table at
/// <paramref name="blockIndex"/>: one new empty cell per row (or a GridSpan increment when the
/// target grid column falls strictly inside an existing horizontal merge). Keeps
/// <see cref="Table.ColumnWidthsPt"/> in sync. Reversible.
/// <para>BF3: When <paramref name="columnIndex"/> falls strictly INSIDE a cell's GridSpan (i.e. the
/// cell starts before the target column), the cell's GridSpan is incremented instead of inserting a
/// stand-alone cell, keeping the rectangular grid intact (matching Word's behaviour).</para>
/// </summary>
public sealed class InsertTableColumnCommand(int blockIndex, int columnIndex) : IDocumentCommand
{
    private int _appliedAt = -1;
    // Per-row action: either an inserted cell (for removal on undo) or a widened spanning cell (for
    // GridSpan decrement on undo).  Null means the row was untouched (can't happen in practice).
    private List<(TableRow Row, TableCell Cell, bool WasSpanIncrement)>? _actions;

    public string Label => "Insert Column";

    public void Apply(IDocumentCommandContext context)
    {
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        _appliedAt = Math.Max(columnIndex, 0);
        var actions = new List<(TableRow, TableCell, bool)>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            // Walk the row to find the cell covering _appliedAt and whether we're at its boundary.
            var gridPos = 0;
            var handled = false;
            for (var i = 0; i < row.Cells.Count; i++)
            {
                var span = Math.Max(1, row.Cells[i].GridSpan);
                if (_appliedAt >= gridPos && _appliedAt < gridPos + span)
                {
                    if (_appliedAt > gridPos)
                    {
                        // BF3: Target falls STRICTLY inside this cell's span — widen it.
                        row.Cells[i].GridSpan = span + 1;
                        actions.Add((row, row.Cells[i], true));
                    }
                    else
                    {
                        // Target is at the START of this cell (a cell boundary) — insert a new cell.
                        var cell = new TableCell(string.Empty);
                        row.Cells.Insert(i, cell);
                        actions.Add((row, cell, false));
                    }
                    handled = true;
                    break;
                }
                gridPos += span;
            }
            if (!handled)
            {
                // _appliedAt is beyond the row's current grid extent — append a new cell.
                var cell = new TableCell(string.Empty);
                row.Cells.Add(cell);
                actions.Add((row, cell, false));
            }
        }
        _actions = actions;
        // Keep ColumnWidthsPt consistent with the new column count (H4). Insert a default width at the
        // same position; use the average of neighbours when available, else zero (auto).
        if (table.ColumnWidthsPt.Count > 0)
        {
            var insertAt = Math.Clamp(_appliedAt, 0, table.ColumnWidthsPt.Count);
            var defaultWidth = table.ColumnWidthsPt.Average();
            table.ColumnWidthsPt.Insert(insertAt, defaultWidth);
        }
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_appliedAt < 0 || _actions is null)
            return;
        foreach (var (row, cell, wasSpanIncrement) in _actions)
        {
            if (wasSpanIncrement)
                cell.GridSpan = Math.Max(1, cell.GridSpan - 1);  // restore widened span
            else
                row.Cells.Remove(cell);  // remove the inserted cell by reference
        }
        _actions = null;
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        if (table.ColumnWidthsPt.Count > 0)
        {
            var removeAt = Math.Clamp(_appliedAt, 0, table.ColumnWidthsPt.Count - 1);
            table.ColumnWidthsPt.RemoveAt(removeAt);
        }
        _appliedAt = -1;
    }
}

/// <summary>
/// Delete the column at grid-column index <paramref name="columnIndex"/> from the table at
/// <paramref name="blockIndex"/>, snapshotting the removed cell of every row so undo restores them.
/// Handles rows with horizontal merges (GridSpan &gt; 1): when the target grid column falls inside a
/// spanning cell, the span is decremented instead of the cell being removed. Keeps
/// <see cref="Table.ColumnWidthsPt"/> in sync. Never removes the last column.
/// </summary>
public sealed class DeleteTableColumnCommand(int blockIndex, int columnIndex) : IDocumentCommand
{
    private List<(int Row, TableCell Cell, bool WasSpanDecrement)>? _removed;
    private double _removedWidth;
    private bool _widthRemoved;

    public string Label => "Delete Column";

    public void Apply(IDocumentCommandContext context)
    {
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        // Guard: need at least one grid column to delete, and columnIndex must be valid.
        if (columnIndex < 0)
            return;
        // Compute total grid width from row 0 (or any row); bail if only one grid column remains.
        var totalGridCols = table.Rows.Count > 0 ? TableColumnHelpers.RowGridWidth(table.Rows[0]) : 0;
        if (totalGridCols <= 1)
            return;

        var removed = new List<(int, TableCell, bool)>();
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var cells = table.Rows[r].Cells;
            // Map target grid column → cell list index for this row.
            var gridPos = 0;
            for (var i = 0; i < cells.Count; i++)
            {
                var span = Math.Max(1, cells[i].GridSpan);
                if (columnIndex >= gridPos && columnIndex < gridPos + span)
                {
                    if (span > 1)
                    {
                        // The target grid column falls inside a spanning cell — decrement its span
                        // rather than removing the whole cell.
                        cells[i].GridSpan = span - 1;
                        removed.Add((r, cells[i], true));
                    }
                    else
                    {
                        // Normal single-grid-column cell: remove it.
                        removed.Add((r, cells[i], false));
                        cells.RemoveAt(i);
                    }
                    break;
                }
                gridPos += span;
            }
        }
        _removed = removed.Count > 0 ? removed : null;

        // Keep ColumnWidthsPt consistent (H4): remove the width at the deleted grid-column position.
        if (table.ColumnWidthsPt.Count > columnIndex)
        {
            _removedWidth = table.ColumnWidthsPt[columnIndex];
            _widthRemoved = true;
            table.ColumnWidthsPt.RemoveAt(columnIndex);
        }
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_removed is null)
            return;
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        foreach (var (rowIndex, cell, wasSpanDecrement) in _removed)
        {
            var cells = table.Rows[rowIndex].Cells;
            if (wasSpanDecrement)
            {
                // Restore the decremented span.
                cell.GridSpan++;
            }
            else
            {
                // Re-insert the removed cell at the correct grid position.
                var gridPos = 0;
                var insertAt = cells.Count; // default: end of row
                for (var i = 0; i < cells.Count; i++)
                {
                    if (gridPos >= columnIndex)
                    {
                        insertAt = i;
                        break;
                    }
                    gridPos += Math.Max(1, cells[i].GridSpan);
                }
                cells.Insert(insertAt, cell);
            }
        }
        // Restore the removed column width — ONLY if Apply actually removed one, else undo would add a
        // phantom width and drift the ColumnWidthsPt<->grid-column invariant.
        if (_widthRemoved)
        {
            var at = Math.Clamp(columnIndex, 0, table.ColumnWidthsPt.Count);
            table.ColumnWidthsPt.Insert(at, _removedWidth);
        }
        _removed = null;
        _removedWidth = 0;
        _widthRemoved = false;
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
    // Stores (rowIndex, cellListIndex, previousMergeState) — the cell-list index is resolved per-row
    // via GridColumnToCellIndex so horizontal merges (GridSpan > 1) are accounted for correctly.
    private (int Row, int CellIdx, VerticalMergeState State)[]? _previous;

    public string Label => "Merge Cells";

    public void Apply(IDocumentCommandContext context)
    {
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        var first = Math.Min(firstRow, lastRow);
        var last = Math.Max(firstRow, lastRow);
        if (first < 0 || last >= table.Rows.Count || first >= last)
            return;

        var snapshot = new List<(int, int, VerticalMergeState)>();
        for (var r = first; r <= last; r++)
        {
            // H6 fix: map the target GRID column to the correct cell-list index for this row.
            // A direct cells[columnIndex] lookup is wrong when preceding cells have GridSpan > 1.
            var cellIdx = TableColumnHelpers.GridColumnToCellIndex(table.Rows[r], columnIndex);
            if (cellIdx < 0)
                return;
            snapshot.Add((r, cellIdx, table.Rows[r].Cells[cellIdx].VerticalMerge));
        }

        _previous = [.. snapshot];
        table.Rows[first].Cells[_previous[0].CellIdx].VerticalMerge = VerticalMergeState.Restart;
        for (var r = first + 1; r <= last; r++)
            table.Rows[r].Cells[_previous[r - first].CellIdx].VerticalMerge = VerticalMergeState.Continue;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        foreach (var (row, cellIdx, state) in _previous)
        {
            if (row < table.Rows.Count && cellIdx < table.Rows[row].Cells.Count)
                table.Rows[row].Cells[cellIdx].VerticalMerge = state;
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
            // BH4 fix: derive the GRID column from the head row's cell-list index so that lower rows
            // with a different cell-list layout (e.g. a preceding horizontal merge) are resolved
            // correctly.  Mirrors the GridColumnToCellIndex mapping used by MergeCellsVerticalCommand.
            var gridColumn = TableColumnHelpers.CellIndexToGridColumn(table.Rows[rowIndex], columnIndex);
            var snapshot = new List<(int, int, VerticalMergeState)> { (rowIndex, columnIndex, VerticalMergeState.Restart) };
            cell.VerticalMerge = VerticalMergeState.None;
            for (var r = rowIndex + 1; r < table.Rows.Count; r++)
            {
                var belowRow = table.Rows[r];
                var belowIdx = gridColumn >= 0
                    ? TableColumnHelpers.GridColumnToCellIndex(belowRow, gridColumn)
                    : columnIndex; // fallback: grid col unavailable, use raw index (head row has no preceding cells)
                if (belowIdx < 0 || belowIdx >= belowRow.Cells.Count
                    || belowRow.Cells[belowIdx].VerticalMerge != VerticalMergeState.Continue)
                    break;
                snapshot.Add((r, belowIdx, VerticalMergeState.Continue));
                belowRow.Cells[belowIdx].VerticalMerge = VerticalMergeState.None;
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
/// Replace the content (paragraphs) of a specific table cell identified by
/// <paramref name="blockIndex"/> / <paramref name="rowIndex"/> / <paramref name="colIndex"/> with
/// <paramref name="replacement"/> paragraphs.  Snapshots the prior paragraphs so undo restores the
/// original content exactly.  Table structure (row/column counts, cell widths, merge state, shading)
/// is preserved; only the cell's <see cref="TableCell.Paragraphs"/> list is replaced.
/// Out-of-range indices are silently clamped / ignored so the command is a no-op when the
/// coordinates do not exist.
/// </summary>
public sealed class SetTableCellContentCommand(
    int blockIndex,
    int rowIndex,
    int colIndex,
    IReadOnlyList<Paragraph> replacement) : IDocumentCommand
{
    private Paragraph[]? _previous;

    public string Label => "Edit Cell";

    public void Apply(IDocumentCommandContext context)
    {
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        if (rowIndex < 0 || rowIndex >= table.Rows.Count)
            return;
        var cells = table.Rows[rowIndex].Cells;
        if (colIndex < 0 || colIndex >= cells.Count)
            return;
        var cell = cells[colIndex];

        // Snapshot previous content for undo.
        _previous = [.. cell.Paragraphs];

        // Replace with new content (ensure at least one paragraph so the cell is never empty).
        cell.Paragraphs.Clear();
        if (replacement.Count > 0)
        {
            foreach (var p in replacement)
                cell.Paragraphs.Add(p);
        }
        else
        {
            cell.Paragraphs.Add(new Paragraph());
        }
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;
        var table = InsertTableRowCommand.TableAt(context, blockIndex);
        if (rowIndex < 0 || rowIndex >= table.Rows.Count)
            return;
        var cells = table.Rows[rowIndex].Cells;
        if (colIndex < 0 || colIndex >= cells.Count)
            return;
        var cell = cells[colIndex];

        cell.Paragraphs.Clear();
        foreach (var p in _previous)
            cell.Paragraphs.Add(p);
        _previous = null;
    }
}

// ── AV-TBL4: per-cell shading + border commands ───────────────────────────────────────────────

/// <summary>
/// Set (or clear) the background shading of a single table cell.
/// <para><paramref name="colorHex"/> is an RRGGBB hex string (e.g. <c>"#FFFF00"</c>) or null/empty
/// to clear the fill.  The previous value is snapshot-ed so <see cref="Revert"/> restores it.</para>
/// Coordinates are: <paramref name="blockIndex"/> = the table's block index in the document,
/// <paramref name="rowIndex"/> / <paramref name="colIndex"/> = the cell-list indices within that row.
/// Out-of-range addresses are silently ignored (no-op).
/// </summary>
public sealed class SetCellShadingCommand(
    int blockIndex,
    int rowIndex,
    int colIndex,
    string? colorHex) : IDocumentCommand
{
    private string? _previous;
    private bool _applied;

    public string Label => "Set Cell Shading";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetCell(context, out var cell))
            return;
        _previous = cell.ShadingColorHex;
        cell.ShadingColorHex = string.IsNullOrEmpty(colorHex) ? null : colorHex;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || !TryGetCell(context, out var cell))
            return;
        cell.ShadingColorHex = _previous;
        _applied = false;
    }

    private bool TryGetCell(IDocumentCommandContext context, out TableCell cell)
    {
        cell = null!;
        if (blockIndex < 0 || blockIndex >= context.Document.Blocks.Count) return false;
        if (context.Document.Blocks[blockIndex] is not Table table) return false;
        if (rowIndex < 0 || rowIndex >= table.Rows.Count) return false;
        var cells = table.Rows[rowIndex].Cells;
        if (colIndex < 0 || colIndex >= cells.Count) return false;
        cell = cells[colIndex];
        return true;
    }
}

/// <summary>Replaces the complete per-edge border payload of one table cell and restores it on undo.</summary>
public sealed class SetCellBorderPayloadCommand(
    int blockIndex,
    int rowIndex,
    int colIndex,
    CellBorders? borders) : IDocumentCommand
{
    private CellBorders? _previous;
    private bool _applied;

    public string Label => "Set Cell Borders";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetCell(context, out var cell))
            return;
        _previous = cell.Borders;
        cell.Borders = borders;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || !TryGetCell(context, out var cell))
            return;
        cell.Borders = _previous;
        _applied = false;
    }

    private bool TryGetCell(IDocumentCommandContext context, out TableCell cell)
    {
        cell = null!;
        if (blockIndex < 0
            || blockIndex >= context.Document.Blocks.Count
            || context.Document.Blocks[blockIndex] is not Table table
            || rowIndex < 0
            || rowIndex >= table.Rows.Count
            || colIndex < 0
            || colIndex >= table.Rows[rowIndex].Cells.Count)
        {
            return false;
        }

        cell = table.Rows[rowIndex].Cells[colIndex];
        return true;
    }
}

/// <summary>Sets one table cell's text direction and restores the prior source token on undo.</summary>
public sealed class SetCellTextDirectionCommand(
    int blockIndex,
    int rowIndex,
    int colIndex,
    CellTextDirection direction) : IDocumentCommand
{
    private CellTextDirection _previous;
    private bool _applied;

    public string Label => "Set Cell Text Direction";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetCell(context, out var cell))
            return;
        _previous = cell.TextDirection;
        cell.TextDirection = direction;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || !TryGetCell(context, out var cell))
            return;
        cell.TextDirection = _previous;
        _applied = false;
    }

    private bool TryGetCell(IDocumentCommandContext context, out TableCell cell)
    {
        cell = null!;
        if (blockIndex < 0
            || blockIndex >= context.Document.Blocks.Count
            || context.Document.Blocks[blockIndex] is not Table table
            || rowIndex < 0
            || rowIndex >= table.Rows.Count
            || colIndex < 0
            || colIndex >= table.Rows[rowIndex].Cells.Count)
        {
            return false;
        }

        cell = table.Rows[rowIndex].Cells[colIndex];
        return true;
    }
}

/// <summary>
/// Set the vertical alignment and all paragraphs' horizontal alignment of a single table cell.
/// <para><paramref name="verticalAlignment"/> maps to <c>tc/tcPr/w:vAlign</c>; each paragraph's
/// <see cref="ParagraphFormatting.Alignment"/> is set to <paramref name="horizontalAlignment"/>.</para>
/// The previous values are snapshot-ed so <see cref="Revert"/> restores them exactly.
/// Coordinates: same as <see cref="SetCellShadingCommand"/>.
/// </summary>
public sealed class SetCellAlignmentCommand(
    int blockIndex,
    int rowIndex,
    int colIndex,
    TableCellVerticalAlignment verticalAlignment,
    TextAlignment horizontalAlignment) : IDocumentCommand
{
    private TableCellVerticalAlignment _prevVertical;
    private ParagraphFormatting[]? _prevParaFormattings;
    private bool _applied;

    public string Label => "Set Cell Alignment";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetCell(context, out var cell))
            return;
        _prevVertical = cell.VerticalAlignment;
        _prevParaFormattings = cell.Paragraphs.Select(p => p.Formatting).ToArray();
        cell.VerticalAlignment = verticalAlignment;
        foreach (var paragraph in cell.Paragraphs)
            paragraph.Formatting = paragraph.Formatting with { Alignment = horizontalAlignment };
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || !TryGetCell(context, out var cell))
            return;
        cell.VerticalAlignment = _prevVertical;
        if (_prevParaFormattings is not null)
        {
            for (var i = 0; i < Math.Min(cell.Paragraphs.Count, _prevParaFormattings.Length); i++)
                cell.Paragraphs[i].Formatting = _prevParaFormattings[i];
        }
        _applied = false;
    }

    private bool TryGetCell(IDocumentCommandContext context, out TableCell cell)
    {
        cell = null!;
        if (blockIndex < 0 || blockIndex >= context.Document.Blocks.Count) return false;
        if (context.Document.Blocks[blockIndex] is not Table table) return false;
        if (rowIndex < 0 || rowIndex >= table.Rows.Count) return false;
        var cells = table.Rows[rowIndex].Cells;
        if (colIndex < 0 || colIndex >= cells.Count) return false;
        cell = cells[colIndex];
        return true;
    }
}

/// <summary>
/// Set the per-edge borders of a single table cell, merging with any existing per-edge settings.
/// <para>Only the edges specified in <paramref name="edges"/> are touched; the others are preserved
/// from the cell's current <see cref="CellBorders"/> (or left null when no borders existed).</para>
/// An edge is set to a new <see cref="CellBorderEdge"/> built from <paramref name="style"/>,
/// <paramref name="colorHex"/> and <paramref name="widthPt"/>; passing
/// <paramref name="clearEdges"/> = true removes the specified edges instead of setting them.
/// The previous <see cref="CellBorders"/> is snapshot-ed so <see cref="Revert"/> restores it exactly.
/// Coordinates: same as <see cref="SetCellShadingCommand"/>.
/// </summary>
public sealed class SetCellBordersCommand(
    int blockIndex,
    int rowIndex,
    int colIndex,
    CellBorderEdges edges,
    BorderLineStyle style,
    string colorHex,
    double widthPt,
    bool clearEdges = false) : IDocumentCommand
{
    private CellBorders? _previous;
    private bool _applied;

    public string Label => clearEdges ? "Clear Cell Border" : "Set Cell Border";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetCell(context, out var cell))
            return;
        _previous = cell.Borders;
        cell.Borders = ApplyEdges(cell.Borders, edges, style, colorHex, widthPt, clearEdges);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || !TryGetCell(context, out var cell))
            return;
        cell.Borders = _previous;
        _applied = false;
    }

    private bool TryGetCell(IDocumentCommandContext context, out TableCell cell)
    {
        cell = null!;
        if (blockIndex < 0 || blockIndex >= context.Document.Blocks.Count) return false;
        if (context.Document.Blocks[blockIndex] is not Table table) return false;
        if (rowIndex < 0 || rowIndex >= table.Rows.Count) return false;
        var cells = table.Rows[rowIndex].Cells;
        if (colIndex < 0 || colIndex >= cells.Count) return false;
        cell = cells[colIndex];
        return true;
    }

    internal static CellBorders? ApplyEdges(
        CellBorders? existing,
        CellBorderEdges edges,
        BorderLineStyle style,
        string colorHex,
        double widthPt,
        bool clear)
    {
        var edge = clear ? null : new CellBorderEdge(style, colorHex, widthPt);
        var top    = (edges & CellBorderEdges.Top)    != 0 ? edge : existing?.Top;
        var bottom = (edges & CellBorderEdges.Bottom) != 0 ? edge : existing?.Bottom;
        var left   = (edges & CellBorderEdges.Left)   != 0 ? edge : existing?.Left;
        var right  = (edges & CellBorderEdges.Right)  != 0 ? edge : existing?.Right;
        if (top is null && bottom is null && left is null && right is null)
            return null;
        return new CellBorders { Top = top, Bottom = bottom, Left = left, Right = right };
    }
}

/// <summary>
/// Edge selector for <see cref="SetCellBordersCommand"/>. Can be combined as flags.
/// Composite values (<see cref="All"/>, <see cref="Outside"/>, <see cref="Inside"/>) are
/// expanded by the DocumentView before issuing per-cell commands, so each command only sees
/// the four primitive edge bits.
/// </summary>
[Flags]
public enum CellBorderEdges
{
    None   = 0,
    Top    = 1,
    Bottom = 2,
    Left   = 4,
    Right  = 8,
    All      = Top | Bottom | Left | Right,
    /// <summary>All four primitive edges — alias for <see cref="All"/>.</summary>
    Outside  = All,
    /// <summary>Inside edges of a selection (handled at the DocumentView layer).</summary>
    Inside   = 16,
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
/// Set the alt-text accessibility description on the inline image at the given paragraph/run indices,
/// snapshotting the prior value for undo.
/// </summary>
public sealed class SetImageAltTextCommand(int paragraphIndex, int runIndex, string? altText) : IDocumentCommand
{
    private string? _previous;
    private bool _applied;

    public string Label => "Image Alt Text";

    public void Apply(IDocumentCommandContext context)
    {
        if (ImageAt(context) is not { } image) return;
        _previous = image.AltText;
        image.AltText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim();
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ImageAt(context) is not { } image) return;
        image.AltText = _previous;
        _applied = false;
    }

    private InlineImage? ImageAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Image : null;
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
    private int _prevShadow, _prevReflection, _prevBevel;
    private double _prevGlow, _prevSoftEdge;
    private string? _prevGlowColor;
    private ShapeEffectLst? _prevImportedEffects;
    private ImageRecolorMode _prevRecolor;
    private double _prevColorTemp;
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
        // Snapshot effects and recolor.
        _prevShadow     = image.ShadowPreset;
        _prevGlow       = image.GlowSizePt;
        _prevGlowColor  = image.GlowColorHex;
        _prevReflection = image.ReflectionPreset;
        _prevSoftEdge   = image.SoftEdgePt;
        _prevBevel      = image.BevelPreset;
        _prevImportedEffects = image.ImportedEffects?.Clone();
        _prevRecolor    = image.RecolorMode;
        _prevColorTemp  = image.ColorTemperature;
        image.WidthPt = naturalWidthPt; image.HeightPt = naturalHeightPt;
        image.RotationAngle = 0; image.FlipH = false; image.FlipV = false;
        image.CropLeft = image.CropRight = image.CropTop = image.CropBottom = 0;
        // Reset adjustments to neutral.
        image.BrightnessPct   = 0;
        image.ContrastPct     = 0;
        image.SaturationPct   = 100;
        image.TransparencyPct = 0;
        // Reset effects.
        image.ShadowPreset     = 0;
        image.GlowSizePt       = 0;
        image.GlowColorHex     = null;
        image.ReflectionPreset = 0;
        image.SoftEdgePt       = 0;
        image.BevelPreset      = 0;
        image.ImportedEffects  = null;
        // Reset recolor.
        image.RecolorMode      = ImageRecolorMode.None;
        image.ColorTemperature = 0;
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
        image.ShadowPreset     = _prevShadow;
        image.GlowSizePt       = _prevGlow;
        image.GlowColorHex     = _prevGlowColor;
        image.ReflectionPreset = _prevReflection;
        image.SoftEdgePt       = _prevSoftEdge;
        image.BevelPreset      = _prevBevel;
        image.ImportedEffects  = _prevImportedEffects?.Clone();
        image.RecolorMode      = _prevRecolor;
        image.ColorTemperature = _prevColorTemp;
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
/// Set the picture-effects parameters (shadow, glow, reflection, soft-edge, bevel) on the inline image
/// at the given paragraph/run indices, snapshotting prior values for undo. 0/0.0 clears each effect.
/// </summary>
public sealed class SetImageEffectCommand(
    int paragraphIndex, int runIndex,
    int shadowPreset, double glowSizePt, string? glowColorHex,
    int reflectionPreset, double softEdgePt, int bevelPreset)
    : IDocumentCommand
{
    private int _prevShadow, _prevReflection, _prevBevel;
    private double _prevGlow, _prevSoftEdge;
    private string? _prevGlowColor;
    private ShapeEffectLst? _prevImportedEffects;
    private bool _applied;

    public string Label => "Picture Effect";

    public void Apply(IDocumentCommandContext context)
    {
        if (ImageAt(context) is not { } image) return;
        _prevShadow     = image.ShadowPreset;
        _prevGlow       = image.GlowSizePt;
        _prevGlowColor  = image.GlowColorHex;
        _prevReflection = image.ReflectionPreset;
        _prevSoftEdge   = image.SoftEdgePt;
        _prevBevel      = image.BevelPreset;
        _prevImportedEffects = image.ImportedEffects?.Clone();
        image.ShadowPreset     = shadowPreset;
        image.GlowSizePt       = glowSizePt;
        image.GlowColorHex     = glowColorHex;
        image.ReflectionPreset = reflectionPreset;
        image.SoftEdgePt       = softEdgePt;
        image.BevelPreset      = bevelPreset;
        image.ImportedEffects  = null;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ImageAt(context) is not { } image) return;
        image.ShadowPreset     = _prevShadow;
        image.GlowSizePt       = _prevGlow;
        image.GlowColorHex     = _prevGlowColor;
        image.ReflectionPreset = _prevReflection;
        image.SoftEdgePt       = _prevSoftEdge;
        image.BevelPreset      = _prevBevel;
        image.ImportedEffects  = _prevImportedEffects?.Clone();
        _applied = false;
    }

    private InlineImage? ImageAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Image : null;
}

/// <summary>
/// Set the recolor mode and/or color temperature on the inline image at the given paragraph/run indices,
/// snapshotting prior values for undo. Non-destructive: original bytes are never modified.
/// </summary>
public sealed class SetImageRecolorCommand(
    int paragraphIndex, int runIndex,
    ImageRecolorMode recolorMode, double colorTemperature)
    : IDocumentCommand
{
    private ImageRecolorMode _prevMode;
    private double _prevTemp;
    private bool _applied;

    public string Label => "Picture Recolor";

    public void Apply(IDocumentCommandContext context)
    {
        if (ImageAt(context) is not { } image) return;
        _prevMode = image.RecolorMode;
        _prevTemp = image.ColorTemperature;
        image.RecolorMode      = recolorMode;
        image.ColorTemperature = colorTemperature;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ImageAt(context) is not { } image) return;
        image.RecolorMode      = _prevMode;
        image.ColorTemperature = _prevTemp;
        _applied = false;
    }

    private InlineImage? ImageAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Image : null;
}

/// <summary>
/// Apply a Picture Style preset: bundles border + effect settings. Snaps prior border/effect fields for undo.
/// </summary>
public sealed class SetImageStyleCommand(
    int paragraphIndex, int runIndex,
    int stylePreset,
    string? borderColorHex, double borderWidthPt, string? borderDash,
    int shadowPreset, int reflectionPreset, double softEdgePt)
    : IDocumentCommand
{
    public SetImageStyleCommand(int paragraphIndex, int runIndex, PictureStylePreset preset)
        : this(
            paragraphIndex,
            runIndex,
            preset.Id,
            preset.BorderColorHex,
            preset.BorderWidthPt,
            preset.BorderDash,
            preset.ShadowPreset,
            preset.ReflectionPreset,
            preset.SoftEdgePt)
    {
        ArgumentNullException.ThrowIfNull(preset);
    }

    private string? _prevBorderColor;
    private double _prevBorderWidth;
    private string? _prevBorderDash;
    private int _prevShadow, _prevReflection, _prevStyle;
    private double _prevSoftEdge;
    private ShapeEffectLst? _prevImportedEffects;
    private bool _applied;

    public string Label => "Apply Picture Style";

    public void Apply(IDocumentCommandContext context)
    {
        if (ImageAt(context) is not { } image) return;
        _prevBorderColor  = image.BorderColorHex;
        _prevBorderWidth  = image.BorderWidthPt;
        _prevBorderDash   = image.BorderDash;
        _prevShadow       = image.ShadowPreset;
        _prevReflection   = image.ReflectionPreset;
        _prevSoftEdge     = image.SoftEdgePt;
        _prevStyle        = image.PictureStylePreset;
        _prevImportedEffects = image.ImportedEffects?.Clone();
        image.BorderColorHex    = borderColorHex;
        image.BorderWidthPt     = borderWidthPt;
        image.BorderDash        = borderDash;
        image.ShadowPreset      = shadowPreset;
        image.ReflectionPreset  = reflectionPreset;
        image.SoftEdgePt        = softEdgePt;
        image.PictureStylePreset = stylePreset;
        image.ImportedEffects   = null;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ImageAt(context) is not { } image) return;
        image.BorderColorHex    = _prevBorderColor;
        image.BorderWidthPt     = _prevBorderWidth;
        image.BorderDash        = _prevBorderDash;
        image.ShadowPreset      = _prevShadow;
        image.ReflectionPreset  = _prevReflection;
        image.SoftEdgePt        = _prevSoftEdge;
        image.PictureStylePreset = _prevStyle;
        image.ImportedEffects   = _prevImportedEffects?.Clone();
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
    private FormatRevision?[]? _previousRevisions;

    public string Label => "Format";

    public void Apply(IDocumentCommandContext context)
    {
        var runs = ((Paragraph)context.Document.Blocks[paragraphIndex]).Runs;
        _previous = runs.Select(r => r.Formatting).ToArray();
        _previousRevisions = runs.Select(r => r.FormatRevision).ToArray();
        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            var formatting = transform(run.Formatting);
            run.Formatting = formatting;
            if (TrackedFormattingRevisionFactory.ShouldTrack(context.Document)
                && formatting != _previous[i]
                && run.FormatRevision is null)
            {
                run.FormatRevision = TrackedFormattingRevisionFactory.ForRun(_previous[i], context.RevisionAuthor);
            }
        }
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null)
            return;
        var runs = ((Paragraph)context.Document.Blocks[paragraphIndex]).Runs;
        for (var i = 0; i < runs.Count && i < _previous.Length; i++)
        {
            runs[i].Formatting = _previous[i];
            runs[i].FormatRevision = _previousRevisions?[i];
        }
    }
}

internal static class TrackedFormattingRevisionFactory
{
    private const string DefaultAuthor = "FreeW User";

    public static bool ShouldTrack(TextDocument document) =>
        document.TrackRevisions && !document.DoNotTrackFormatting;

    public static FormatRevision ForRun(RunFormatting previous, string? author) =>
        new(previous, NormalizeAuthor(author), CurrentDateXml());

    public static ParagraphFormatRevision ForParagraph(ParagraphFormatting previous, string? author) =>
        new(previous, NormalizeAuthor(author), CurrentDateXml());

    private static string NormalizeAuthor(string? author) =>
        string.IsNullOrWhiteSpace(author) ? DefaultAuthor : author.Trim();

    private static string CurrentDateXml() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
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
public sealed class SetShapeKindCommand(
    int paragraphIndex,
    int runIndex,
    ShapeKind kind,
    IReadOnlyList<int>? childPath = null) : IDocumentCommand
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
        ShapeCommandTargetResolver.TryGetShape(context, paragraphIndex, runIndex, childPath, out var shape)
            ? shape : null;
}

/// <summary>
/// Set the fill colour of the inline shape at the given paragraph/run indices, snapshotting the
/// prior colour for undo. Pass null to remove the fill.
/// </summary>
public sealed class SetShapeFillCommand(
    int paragraphIndex,
    int runIndex,
    string? colorHex,
    IReadOnlyList<int>? childPath = null) : IDocumentCommand
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
        ShapeCommandTargetResolver.TryGetShape(context, paragraphIndex, runIndex, childPath, out var shape)
            ? shape : null;
}

/// <summary>
/// Set the outline (color hex, width in points, dash token) of the inline shape at the given
/// paragraph/run indices, snapshotting prior values for undo. Pass null colorHex to remove the outline.
/// </summary>
public sealed class SetShapeOutlineCommand(int paragraphIndex, int runIndex,
    string? colorHex, double widthPt, string? dash, IReadOnlyList<int>? childPath = null) : IDocumentCommand
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
        ShapeCommandTargetResolver.TryGetShape(context, paragraphIndex, runIndex, childPath, out var shape)
            ? shape : null;
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
public sealed class SetShapeAltTextCommand(
    int paragraphIndex,
    int runIndex,
    string? altText,
    IReadOnlyList<int>? childPath = null) : IDocumentCommand
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
        ShapeCommandTargetResolver.TryGetShape(context, paragraphIndex, runIndex, childPath, out var shape)
            ? shape : null;
}

/// <summary>
/// Set the text direction on the inline text-box shape at the given paragraph/run indices,
/// snapshotting the prior value for undo. No-op for non-text-box shapes.
/// </summary>
public sealed class SetShapeTextDirectionCommand(
    int paragraphIndex,
    int runIndex,
    ShapeTextDirection direction,
    IReadOnlyList<int>? childPath = null) : IDocumentCommand
{
    private ShapeTextDirection _previous;
    private bool _applied;

    public string Label => "Text Direction";

    public void Apply(IDocumentCommandContext context)
    {
        if (!ShapeTextTargetResolver.TryGetShape(
                context, paragraphIndex, runIndex, childPath, out var shape))
            return;
        _previous = shape.TextDirection;
        shape.TextDirection = direction;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied
            || !ShapeTextTargetResolver.TryGetShape(
                context, paragraphIndex, runIndex, childPath, out var shape))
            return;
        shape.TextDirection = _previous;
        _applied = false;
    }
}

/// <summary>Resolves direct and nested grouped text-box targets for shared text commands.</summary>
internal static class ShapeTextTargetResolver
{
    public static bool TryGetShape(
        IDocumentCommandContext context,
        int paragraphIndex,
        int runIndex,
        IReadOnlyList<int>? childPath,
        out Shape shape)
        => ShapeCommandTargetResolver.TryGetShape(
            context, paragraphIndex, runIndex, childPath, out shape);
}

/// <summary>Resolves direct and nested grouped shape targets for shared formatting commands.</summary>
internal static class ShapeCommandTargetResolver
{
    public static bool TryGetShape(
        IDocumentCommandContext context,
        int paragraphIndex,
        int runIndex,
        IReadOnlyList<int>? childPath,
        out Shape shape)
        => ShapeTextFormattingPlanner.TryGetShape(
            context.Document, paragraphIndex, runIndex, childPath, out shape);
}

/// <summary>
/// Replace one text run inside an inline text-box shape, snapshotting the prior value for undo.
/// The owning drawing run's plain-text mirror is kept synchronized with the edited shape body.
/// </summary>
public sealed class SetShapeTextRunCommand(
    int paragraphIndex,
    int runIndex,
    int textParagraphIndex,
    int textRunIndex,
    string text,
    IReadOnlyList<int>? childPath = null) : IDocumentCommand
{
    private string? _previous;
    private bool _applied;

    public string Label => "Edit Shape Text";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetShapeTextRun(context, out var shape, out var run)) return;
        _previous = run.Text;
        run.Text = text;
        SyncShapeRunText(context, shape);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || !TryGetShapeTextRun(context, out var shape, out var run) || _previous is null) return;
        run.Text = _previous;
        SyncShapeRunText(context, shape);
        _applied = false;
    }

    private bool TryGetShapeTextRun(IDocumentCommandContext context, out Shape shape, out Run textRun)
    {
        shape = null!;
        textRun = null!;
        if (!ShapeTextTargetResolver.TryGetShape(
                context, paragraphIndex, runIndex, childPath, out var foundShape)
            || textParagraphIndex < 0 || textParagraphIndex >= foundShape.TextParagraphs.Count)
            return false;

        shape = foundShape;
        var textParagraph = shape.TextParagraphs[textParagraphIndex];
        if (textRunIndex < 0 || textRunIndex >= textParagraph.Runs.Count)
            return false;

        textRun = textParagraph.Runs[textRunIndex];
        return true;
    }

    private void SyncShapeRunText(IDocumentCommandContext context, Shape shape)
    {
        if (childPath is null
            && context.Document.Blocks[paragraphIndex] is Paragraph paragraph
            && runIndex >= 0 && runIndex < paragraph.Runs.Count
            && ReferenceEquals(paragraph.Runs[runIndex].Shape, shape))
            paragraph.Runs[runIndex].Text = shape.PlainText;
    }
}

/// <summary>
/// Replaces the paragraph list inside an inline text-box shape, keeping the owning drawing run's plain-text
/// mirror synchronized and restoring the exact prior paragraph graph on undo. Avalonia uses this command for
/// text-box range replacement and range formatting so those edits share the same model/undo path as WPF shape
/// editing rather than mutating the shape directly from the renderer.
/// </summary>
public sealed class ReplaceShapeTextParagraphsCommand(
    int paragraphIndex,
    int runIndex,
    IReadOnlyList<Paragraph> replacement,
    IReadOnlyList<int>? childPath = null) : IDocumentCommand
{
    private List<Paragraph>? _previous;
    private List<Paragraph>? _next;

    public string Label => "Edit Shape Text";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetShape(context, out var owner, out var shape))
            return;

        if (_previous is null)
        {
            _previous = shape.TextParagraphs.ToList();
            _next = replacement.ToList();
        }

        shape.TextParagraphs.Clear();
        shape.TextParagraphs.AddRange(_next!);
        if (childPath is null)
            owner.Text = shape.PlainText;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null || !TryGetShape(context, out var owner, out var shape))
            return;

        shape.TextParagraphs.Clear();
        shape.TextParagraphs.AddRange(_previous);
        if (childPath is null)
            owner.Text = shape.PlainText;
    }

    private bool TryGetShape(IDocumentCommandContext context, out Run owner, out Shape shape)
    {
        owner = null!;
        shape = null!;
        if (!ShapeTextTargetResolver.TryGetShape(
                context, paragraphIndex, runIndex, childPath, out var found))
            return false;

        owner = context.Document.Blocks[paragraphIndex] is Paragraph paragraph
            && runIndex >= 0 && runIndex < paragraph.Runs.Count
            ? paragraph.Runs[runIndex]
            : null!;
        shape = found;
        return true;
    }
}

/// <summary>
/// Inserts a real paragraph break inside an inline text-box body. The command replaces the affected
/// shape paragraph list as one undoable operation and keeps the outer drawing run's plain-text mirror
/// aligned with the shape content.
/// </summary>
public sealed class InsertShapeTextParagraphBreakCommand(
    int paragraphIndex,
    int runIndex,
    int textParagraphIndex,
    int textRunIndex,
    int textRunOffset,
    IReadOnlyList<int>? childPath = null) : IDocumentCommand
{
    private List<Paragraph>? _previous;
    private List<Paragraph>? _next;

    public string Label => "Insert Shape Text Paragraph Break";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetShape(context, out var owner, out var shape)
            || textParagraphIndex < 0 || textParagraphIndex >= shape.TextParagraphs.Count
            || textRunIndex < 0 || textRunIndex >= shape.TextParagraphs[textParagraphIndex].Runs.Count)
            return;

        if (_previous is null)
        {
            _previous = shape.TextParagraphs.ToList();
            _next = BuildSplitParagraphs(_previous, textParagraphIndex, textRunIndex, textRunOffset);
        }

        shape.TextParagraphs.Clear();
        shape.TextParagraphs.AddRange(_next!);
        if (childPath is null)
            owner.Text = shape.PlainText;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null || !TryGetShape(context, out var owner, out var shape))
            return;

        shape.TextParagraphs.Clear();
        shape.TextParagraphs.AddRange(_previous);
        if (childPath is null)
            owner.Text = shape.PlainText;
    }

    private bool TryGetShape(IDocumentCommandContext context, out Run owner, out Shape shape)
    {
        owner = null!;
        shape = null!;
        if (!ShapeTextTargetResolver.TryGetShape(
                context, paragraphIndex, runIndex, childPath, out var found))
            return false;

        owner = context.Document.Blocks[paragraphIndex] is Paragraph paragraph
            && runIndex >= 0 && runIndex < paragraph.Runs.Count
            ? paragraph.Runs[runIndex]
            : null!;
        shape = found;
        return true;
    }

    private static List<Paragraph> BuildSplitParagraphs(
        IReadOnlyList<Paragraph> paragraphs,
        int paragraphIndex,
        int runIndex,
        int runOffset)
    {
        var source = paragraphs[paragraphIndex];
        var fullOffset = source.Runs.Take(runIndex).Sum(run => run.Text.Length)
            + Math.Clamp(runOffset, 0, source.Runs[runIndex].Text.Length);
        var prefix = CloneParagraphWithTextRange(source, 0, fullOffset);
        var suffix = CloneParagraphWithTextRange(source, fullOffset, source.PlainText.Length);
        var result = paragraphs.ToList();
        result.RemoveAt(paragraphIndex);
        result.InsertRange(paragraphIndex, [prefix, suffix]);
        return result;
    }

    private static Paragraph CloneParagraphWithTextRange(Paragraph source, int start, int end)
    {
        var clone = (Paragraph)DocumentMerge.CloneBlock(source);
        clone.Runs.Clear();

        var position = 0;
        foreach (var run in source.Runs)
        {
            var runStart = position;
            var runEnd = position + run.Text.Length;
            position = runEnd;
            var overlapStart = Math.Max(start, runStart);
            var overlapEnd = Math.Min(end, runEnd);
            if (overlapEnd > overlapStart)
            {
                clone.Runs.Add(RevisionEditPlanner.CloneRunWithText(
                    run,
                    run.Text[(overlapStart - runStart)..(overlapEnd - runStart)]));
            }
        }

        if (clone.Runs.Count == 0)
        {
            var formatting = source.Runs.FirstOrDefault()?.Formatting ?? RunFormatting.Default;
            clone.Runs.Add(new Run(string.Empty, formatting));
        }

        return clone;
    }
}

/// <summary>
/// Joins a text-box paragraph with the paragraph before it when Backspace is pressed at column zero.
/// The complete paragraph list is restored by undo, matching the WPF text-box editing contract.
/// </summary>
public sealed class MergeShapeTextParagraphWithPreviousCommand(
    int ownerParagraphIndex,
    int ownerRunIndex,
    int textParagraphIndex,
    IReadOnlyList<int>? childPath = null) : IDocumentCommand
{
    private List<Paragraph>? _previous;
    private List<Paragraph>? _next;

    public string Label => "Merge Shape Text Paragraphs";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetShape(context, out var owner, out var shape)
            || textParagraphIndex <= 0 || textParagraphIndex >= shape.TextParagraphs.Count)
            return;

        if (_previous is null)
        {
            _previous = shape.TextParagraphs.ToList();
            _next = BuildMergedParagraphs(_previous, textParagraphIndex);
        }

        shape.TextParagraphs.Clear();
        shape.TextParagraphs.AddRange(_next!);
        if (childPath is null)
            owner.Text = shape.PlainText;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_previous is null || !TryGetShape(context, out var owner, out var shape))
            return;

        shape.TextParagraphs.Clear();
        shape.TextParagraphs.AddRange(_previous);
        if (childPath is null)
            owner.Text = shape.PlainText;
    }

    private bool TryGetShape(IDocumentCommandContext context, out Run owner, out Shape shape)
    {
        owner = null!;
        shape = null!;
        if (!ShapeTextTargetResolver.TryGetShape(
                context, ownerParagraphIndex, ownerRunIndex, childPath, out var found))
            return false;

        owner = context.Document.Blocks[ownerParagraphIndex] is Paragraph paragraph
            && ownerRunIndex >= 0 && ownerRunIndex < paragraph.Runs.Count
            ? paragraph.Runs[ownerRunIndex]
            : null!;
        shape = found;
        return true;
    }

    private static List<Paragraph> BuildMergedParagraphs(
        IReadOnlyList<Paragraph> paragraphs,
        int paragraphIndex)
    {
        var previous = paragraphs[paragraphIndex - 1];
        var current = paragraphs[paragraphIndex];
        var merged = (Paragraph)DocumentMerge.CloneBlock(previous);
        foreach (var run in current.Runs)
            merged.Runs.Add(RevisionEditPlanner.CloneRunWithText(run, run.Text));

        var result = paragraphs.ToList();
        result[paragraphIndex - 1] = merged;
        result.RemoveAt(paragraphIndex);
        return result;
    }
}

/// <summary>
/// Set the rotation angle and flip flags on the inline shape at the given paragraph/run indices,
/// snapshotting prior values for undo. Mirrors <see cref="SetImageRotationCommand"/> for shapes.
/// </summary>
public sealed class SetShapeRotationCommand(int paragraphIndex, int runIndex, double angleDeg, bool flipH, bool flipV) : IDocumentCommand
{
    private double _prevAngle;
    private bool _prevFlipH, _prevFlipV;
    private bool _applied;

    public string Label => "Rotate/Flip Shape";

    public void Apply(IDocumentCommandContext context)
    {
        if (ShapeAt(context) is not { } shape) return;
        _prevAngle = shape.RotationAngle; _prevFlipH = shape.FlipH; _prevFlipV = shape.FlipV;
        shape.RotationAngle = angleDeg; shape.FlipH = flipH; shape.FlipV = flipV;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ShapeAt(context) is not { } shape) return;
        shape.RotationAngle = _prevAngle; shape.FlipH = _prevFlipH; shape.FlipV = _prevFlipV;
        _applied = false;
    }

    private Shape? ShapeAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Shape : null;
}

/// <summary>
/// Set the floating wrapping mode on the inline shape at the given paragraph/run indices,
/// snapshotting the prior value for undo. Mirrors <see cref="SetImagePositionCommand"/> for shapes.
/// </summary>
public sealed class SetShapeWrappingCommand(int paragraphIndex, int runIndex, ImageWrapping wrapping) : IDocumentCommand
{
    private ImageWrapping _previous;
    private bool _applied;

    public string Label => "Shape Wrap Text";

    public void Apply(IDocumentCommandContext context)
    {
        if (ShapeAt(context) is not { } shape) return;
        // Ensure a FloatingPlacement exists before writing wrapping.
        shape.Placement ??= new FloatingPlacement();
        _previous = shape.Placement.Wrapping;
        shape.Placement.Wrapping = wrapping;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ShapeAt(context) is not { } shape) return;
        if (shape.Placement is not null)
            shape.Placement.Wrapping = _previous;
        _applied = false;
    }

    private Shape? ShapeAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Shape : null;
}

/// <summary>
/// Set the floating position offsets and anchors on the inline shape at the given paragraph/run indices,
/// snapshotting prior values for undo. Mirrors <see cref="SetImagePositionCommand"/> for shapes.
/// </summary>
public sealed class SetShapePositionCommand(int paragraphIndex, int runIndex,
    double horizontalOffsetPt, double verticalOffsetPt,
    HorizontalAnchor horizontalAnchor, VerticalAnchor verticalAnchor) : IDocumentCommand
{
    private double _ph, _pv;
    private HorizontalAnchor _pha;
    private VerticalAnchor _pva;
    private bool _applied;

    public string Label => "Set Shape Position";

    public void Apply(IDocumentCommandContext context)
    {
        if (ShapeAt(context) is not { } shape) return;
        shape.Placement ??= new FloatingPlacement();
        _ph = shape.Placement.HorizontalOffsetPt; _pv = shape.Placement.VerticalOffsetPt;
        _pha = shape.Placement.HorizontalAnchor; _pva = shape.Placement.VerticalAnchor;
        shape.Placement.HorizontalOffsetPt = horizontalOffsetPt; shape.Placement.VerticalOffsetPt = verticalOffsetPt;
        shape.Placement.HorizontalAnchor = horizontalAnchor; shape.Placement.VerticalAnchor = verticalAnchor;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ShapeAt(context) is not { } shape || shape.Placement is null) return;
        shape.Placement.HorizontalOffsetPt = _ph; shape.Placement.VerticalOffsetPt = _pv;
        shape.Placement.HorizontalAnchor = _pha; shape.Placement.VerticalAnchor = _pva;
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

// ── New W24 Shape commands (effects, extended fill, style preset) ─────────────────────────────────

/// <summary>
/// Apply a <see cref="ShapeStylePreset"/> to the inline shape at the given paragraph/run indices.
/// Snapshots fill, outline and effect for undo.
/// </summary>
public sealed class ApplyShapeStyleCommand(
    int paragraphIndex,
    int runIndex,
    ShapeStylePreset preset,
    IReadOnlyList<int>? childPath = null) : IDocumentCommand
{
    private string? _prevFill;
    private ShapeFill? _prevExtFill;
    private string? _prevOutlineColor;
    private double _prevOutlineWidth;
    private string? _prevOutlineDash;
    private ShapeEffectLst? _prevEffect;
    private bool _applied;

    public string Label => "Shape Style";

    public void Apply(IDocumentCommandContext context)
    {
        if (ShapeAt(context) is not { } shape) return;
        _prevFill        = shape.FillColorHex;
        _prevExtFill     = shape.ExtendedFill;
        _prevOutlineColor = shape.OutlineColorHex;
        _prevOutlineWidth = shape.OutlineWidthPt;
        _prevOutlineDash  = shape.OutlineDash;
        _prevEffect       = shape.Effects;

        shape.FillColorHex  = preset.FillColorHex;
        shape.ExtendedFill  = preset.Fill;
        shape.OutlineColorHex = preset.OutlineColorHex;
        shape.OutlineWidthPt  = preset.OutlineWidthPt;
        shape.OutlineDash     = preset.OutlineDash;
        shape.Effects         = preset.Effect;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ShapeAt(context) is not { } shape) return;
        shape.FillColorHex  = _prevFill;
        shape.ExtendedFill  = _prevExtFill;
        shape.OutlineColorHex = _prevOutlineColor;
        shape.OutlineWidthPt  = _prevOutlineWidth;
        shape.OutlineDash     = _prevOutlineDash;
        shape.Effects         = _prevEffect;
        _applied = false;
    }

    private Shape? ShapeAt(IDocumentCommandContext context) =>
        ShapeCommandTargetResolver.TryGetShape(context, paragraphIndex, runIndex, childPath, out var shape)
            ? shape : null;
}

/// <summary>
/// Set the extended fill (gradient / pattern / no-fill) on the inline shape. Snapshots prior fill for undo.
/// </summary>
public sealed class SetShapeExtendedFillCommand(
    int paragraphIndex,
    int runIndex,
    ShapeFill? fill,
    IReadOnlyList<int>? childPath = null) : IDocumentCommand
{
    private string? _prevSolid;
    private ShapeFill? _prevExt;
    private bool _applied;

    public string Label => "Shape Fill";

    public void Apply(IDocumentCommandContext context)
    {
        if (ShapeAt(context) is not { } shape) return;
        _prevSolid = shape.FillColorHex; _prevExt = shape.ExtendedFill;
        shape.ExtendedFill = fill;
        if (fill is not null) shape.FillColorHex = null; // ExtendedFill takes precedence
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ShapeAt(context) is not { } shape) return;
        shape.FillColorHex = _prevSolid; shape.ExtendedFill = _prevExt;
        _applied = false;
    }

    private Shape? ShapeAt(IDocumentCommandContext context) =>
        ShapeCommandTargetResolver.TryGetShape(context, paragraphIndex, runIndex, childPath, out var shape)
            ? shape : null;
}

/// <summary>
/// Set (or clear) the effects bundle on the inline shape. Snapshots prior effects for undo.
/// </summary>
public sealed class SetShapeEffectsCommand(
    int paragraphIndex,
    int runIndex,
    ShapeEffectLst? effects,
    IReadOnlyList<int>? childPath = null) : IDocumentCommand
{
    private ShapeEffectLst? _previous;
    private bool _applied;

    public string Label => "Shape Effects";

    public void Apply(IDocumentCommandContext context)
    {
        if (ShapeAt(context) is not { } shape) return;
        _previous = shape.Effects;
        shape.Effects = effects;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ShapeAt(context) is not { } shape) return;
        shape.Effects = _previous;
        _applied = false;
    }

    private Shape? ShapeAt(IDocumentCommandContext context) =>
        ShapeCommandTargetResolver.TryGetShape(context, paragraphIndex, runIndex, childPath, out var shape)
            ? shape : null;
}

/// <summary>Set the text warp preset on the WordArt at the given paragraph/run indices.</summary>
public sealed class SetWordArtWarpCommand(int paragraphIndex, int runIndex, WordArtWarp warp) : IDocumentCommand
{
    private WordArtWarp _previous;
    private bool _applied;

    public string Label => "WordArt Transform";

    public void Apply(IDocumentCommandContext context)
    {
        if (WordArtAt(context) is not { } wordArt) return;
        _previous = wordArt.Warp;
        wordArt.Warp = warp;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || WordArtAt(context) is not { } wordArt) return;
        wordArt.Warp = _previous;
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
        if (run.DrawingGroup is { IsValid: true } group)
            return (group, group.WidthPt, group.HeightPt, group.Placement);
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
                DrawingGroup nested => RestoreGroupPlacement(nested, group.Placement, absH, absV, z),
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
    private static Run RestoreGroupPlacement(DrawingGroup nested, FloatingPlacement gp, double h, double v, int z)
    {
        nested.Placement = new FloatingPlacement
        {
            Wrapping = gp.Wrapping,
            HorizontalOffsetPt = h,
            VerticalOffsetPt = v,
            HorizontalAnchor = gp.HorizontalAnchor,
            VerticalAnchor = gp.VerticalAnchor,
            ZOrderIndex = z
        };
        return Run.FromDrawingGroup(nested);
    }
}

// ── W25: Artistic Effects + Edit Points commands ──────────────────────────────────────────────────

/// <summary>
/// Set the <see cref="ImageArtisticEffect"/> on the inline image at the given paragraph/run indices,
/// snapshotting the prior value for undo. Non-destructive: original <see cref="InlineImage.Bytes"/> are
/// never modified; the effect is applied at render time by the pixel pipeline.
/// </summary>
public sealed class SetImageArtisticEffectCommand(
    int paragraphIndex, int runIndex, ImageArtisticEffect effect) : IDocumentCommand
{
    private ImageArtisticEffect _previous;
    private bool _previousHadBakedPreview;
    private bool _applied;

    public string Label => "Artistic Effect";

    public void Apply(IDocumentCommandContext context)
    {
        if (ImageAt(context) is not { } image) return;
        _previous = image.ArtisticEffect;
        _previousHadBakedPreview = image.HasBakedArtisticEffectPreview;
        image.ArtisticEffect = effect;
        if (effect != _previous)
            image.HasBakedArtisticEffectPreview = false;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ImageAt(context) is not { } image) return;
        image.ArtisticEffect = _previous;
        image.HasBakedArtisticEffectPreview = _previousHadBakedPreview;
        _applied = false;
    }

    private InlineImage? ImageAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Image : null;
}

/// <summary>
/// Set or replace the <see cref="CustomGeometry"/> on the inline shape at the given paragraph/run indices,
/// snapshotting the prior geometry (and kind) for undo. Used by "Convert to Freeform" and drag-point edits.
/// </summary>
public sealed class SetShapeCustomGeometryCommand(
    int paragraphIndex, int runIndex, CustomGeometry? geometry,
    IReadOnlyList<int>? childPath = null) : IDocumentCommand
{
    private CustomGeometry? _previousGeometry;
    private ShapeKind _previousKind;
    private bool _applied;

    public string Label => geometry is null ? "Remove Freeform" : "Edit Points";

    public void Apply(IDocumentCommandContext context)
    {
        if (ShapeAt(context) is not { } shape) return;
        _previousGeometry = shape.CustomGeometry;
        _previousKind     = shape.Kind;
        shape.CustomGeometry = geometry;
        // When converting to freeform, lock the Kind to Freeform-as-rectangle (no visual change).
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ShapeAt(context) is not { } shape) return;
        shape.CustomGeometry = _previousGeometry;
        shape.Kind           = _previousKind;
        _applied = false;
    }

    private Shape? ShapeAt(IDocumentCommandContext context)
    {
        if (context.Document.Blocks[paragraphIndex] is not Paragraph p
            || runIndex < 0 || runIndex >= p.Runs.Count)
            return null;

        if (childPath is null)
            return p.Runs[runIndex].Shape;

        return p.Runs[runIndex].DrawingGroup is { } root
            && DrawingGroupChildPathResolver.TryGetChild(root, childPath, out _, out var child)
            ? child as Shape
            : null;
    }
}

/// <summary>
/// Move a single edit point (vertex) on the inline shape's custom geometry. Snaps prior coordinates
/// for undo. No-op if the shape has no <see cref="CustomGeometry"/> or the index is out of range.
/// </summary>
public sealed class MoveShapeEditPointCommand(
    int paragraphIndex, int runIndex, int segmentIndex, long newX, long newY,
    IReadOnlyList<int>? childPath = null) : IDocumentCommand
{
    private long _prevX, _prevY;
    private bool _applied;

    public string Label => "Move Edit Point";

    public void Apply(IDocumentCommandContext context)
    {
        if (ShapeAt(context)?.CustomGeometry is not { } geo) return;
        if (segmentIndex < 0 || segmentIndex >= geo.Segments.Count) return;
        var seg = geo.Segments[segmentIndex];
        if (seg.Point is null) return;
        _prevX = seg.Point.X;
        _prevY = seg.Point.Y;
        geo.Segments[segmentIndex] = seg with { Point = new CustomPoint(newX, newY) };
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ShapeAt(context)?.CustomGeometry is not { } geo) return;
        if (segmentIndex < 0 || segmentIndex >= geo.Segments.Count) return;
        var seg = geo.Segments[segmentIndex];
        geo.Segments[segmentIndex] = seg with { Point = new CustomPoint(_prevX, _prevY) };
        _applied = false;
    }

    private Shape? ShapeAt(IDocumentCommandContext context)
    {
        if (context.Document.Blocks[paragraphIndex] is not Paragraph p
            || runIndex < 0 || runIndex >= p.Runs.Count)
            return null;

        if (childPath is null)
            return p.Runs[runIndex].Shape;

        return p.Runs[runIndex].DrawingGroup is { } root
            && DrawingGroupChildPathResolver.TryGetChild(root, childPath, out _, out var child)
            ? child as Shape
            : null;
    }
}

// ── AV-FLSEL: generic floating-object placement/size/rotation/wrapping commands ───────────────────

/// <summary>
/// Set the position (offsets + anchors) on ANY floating object (Image, Shape, Chart, SmartArt,
/// WordArt, DrawingGroup) identified by (paragraphIndex, runIndex). Snaps the prior placement
/// for undo. No-op when the run is not a floating object.
/// </summary>
public sealed class SetFloatingPositionCommand(
    int paragraphIndex, int runIndex,
    double horizontalOffsetPt, double verticalOffsetPt,
    HorizontalAnchor horizontalAnchor, VerticalAnchor verticalAnchor) : IDocumentCommand
{
    private double _ph, _pv;
    private HorizontalAnchor _pha;
    private VerticalAnchor _pva;
    private bool _applied;

    public string Label => "Set Position";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetPlacement(context, out var pl)) return;
        _ph = pl.HorizontalOffsetPt; _pv = pl.VerticalOffsetPt;
        _pha = pl.HorizontalAnchor;  _pva = pl.VerticalAnchor;
        pl.HorizontalOffsetPt = horizontalOffsetPt; pl.VerticalOffsetPt = verticalOffsetPt;
        pl.HorizontalAnchor = horizontalAnchor;     pl.VerticalAnchor = verticalAnchor;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || !TryGetPlacement(context, out var pl)) return;
        pl.HorizontalOffsetPt = _ph; pl.VerticalOffsetPt = _pv;
        pl.HorizontalAnchor = _pha; pl.VerticalAnchor = _pva;
        _applied = false;
    }

    private bool TryGetPlacement(IDocumentCommandContext context, out FloatingPlacement pl)
    {
        pl = null!;
        if (context.Document.Blocks[paragraphIndex] is not Paragraph p) return false;
        if (runIndex < 0 || runIndex >= p.Runs.Count) return false;
        pl = GetFloatingPlacement(p.Runs[runIndex])!;
        return pl is not null;
    }

    public static FloatingPlacement? GetFloatingPlacement(Run run)
    {
        if (run.Image is { IsFloating: true } img)
        {
            // InlineImage stores offsets/anchors directly (no FloatingPlacement object).
            // Wrap them in a proxy? No — for image we use a shim.
            return null; // Images use SetImagePositionCommand instead.
        }
        if (run.Shape is { } shape)  { shape.Placement ??= new FloatingPlacement(); return shape.Placement; }
        if (run.Chart is { } chart)  { chart.Placement ??= new FloatingPlacement { Wrapping = ImageWrapping.Square }; return chart.Placement; }
        if (run.SmartArt is { } sa)  { sa.Placement ??= new FloatingPlacement { Wrapping = ImageWrapping.Square }; return sa.Placement; }
        if (run.WordArt is { } wa)   { wa.Placement ??= new FloatingPlacement { Wrapping = ImageWrapping.Square }; return wa.Placement; }
        if (run.DrawingGroup is { } g) return g.Placement;
        return null;
    }
}

/// <summary>
/// Set the size (widthPt, heightPt) on ANY floating object (Image, Shape, Chart, SmartArt,
/// WordArt, DrawingGroup) identified by (paragraphIndex, runIndex). Snaps the prior size for undo.
/// </summary>
public sealed class SetFloatingSizeCommand(
    int paragraphIndex, int runIndex,
    double widthPt, double heightPt) : IDocumentCommand
{
    private double _prevW, _prevH;
    private bool _applied;

    public string Label => "Resize";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryMutate(context, widthPt, heightPt, out _prevW, out _prevH)) return;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied) return;
        TryMutate(context, _prevW, _prevH, out _, out _);
        _applied = false;
    }

    private bool TryMutate(IDocumentCommandContext context, double w, double h,
        out double prevW, out double prevH)
    {
        prevW = 0; prevH = 0;
        if (context.Document.Blocks[paragraphIndex] is not Paragraph p) return false;
        if (runIndex < 0 || runIndex >= p.Runs.Count) return false;
        var run = p.Runs[runIndex];
        if (run.Image is { IsFloating: true } img)
            { prevW = img.WidthPt; prevH = img.HeightPt; img.WidthPt = w; img.HeightPt = h; return true; }
        if (run.Shape is { } shape)
            { prevW = shape.WidthPt; prevH = shape.HeightPt; shape.WidthPt = w; shape.HeightPt = h; return true; }
        if (run.Chart is { } chart)
            { prevW = chart.WidthPt; prevH = chart.HeightPt; chart.WidthPt = w; chart.HeightPt = h; return true; }
        if (run.SmartArt is { } sa)
            { prevW = sa.WidthPt; prevH = sa.HeightPt; sa.WidthPt = w; sa.HeightPt = h; return true; }
        if (run.DrawingGroup is { } grp)
            { prevW = grp.WidthPt; prevH = grp.HeightPt; grp.WidthPt = w; grp.HeightPt = h; return true; }
        return false;
    }
}

/// <summary>
/// Set the wrapping mode on ANY floating object (Image, Shape, Chart, SmartArt, WordArt,
/// DrawingGroup) identified by (paragraphIndex, runIndex). Snaps the prior wrapping for undo.
/// For Image, this updates <see cref="InlineImage.Wrapping"/> directly.
/// For all others, it updates <see cref="FloatingPlacement.Wrapping"/> (creating placement if absent).
/// </summary>
public sealed class SetFloatingWrapCommand(
    int paragraphIndex, int runIndex,
    ImageWrapping wrapping) : IDocumentCommand
{
    private ImageWrapping _previous;
    private bool _applied;

    public string Label => "Wrap Text";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryMutate(context, wrapping, out _previous)) return;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied) return;
        TryMutate(context, _previous, out _);
        _applied = false;
    }

    private bool TryMutate(IDocumentCommandContext context, ImageWrapping w, out ImageWrapping prev)
    {
        prev = ImageWrapping.Inline;
        if (context.Document.Blocks[paragraphIndex] is not Paragraph p) return false;
        if (runIndex < 0 || runIndex >= p.Runs.Count) return false;
        var run = p.Runs[runIndex];
        if (run.Image is { } img)
            { prev = img.Wrapping; img.Wrapping = w; return true; }
        var pl = SetFloatingPositionCommand.GetFloatingPlacement(run);
        // For non-image types, GetFloatingPlacement returns the Placement (creating it if needed).
        // But Image returns null from GetFloatingPlacement — handled above.
        if (pl is null)
        {
            // Explicitly handle remaining types not covered by GetFloatingPlacement (only Image was
            // excluded there, already handled above).
            return false;
        }
        prev = pl.Wrapping; pl.Wrapping = w; return true;
    }
}

/// <summary>
/// Set the rotation + flip on ANY floating object that supports rotation (Image, Shape, Chart, SmartArt,
/// WordArt, Group).
/// For Image: updates <see cref="InlineImage.RotationAngle"/>, FlipH, FlipV.
/// For Shape: updates <see cref="Shape.RotationAngle"/>, FlipH, FlipV.
/// For Group: updates the group-level DrawingML transform, leaving child-local transforms intact.
/// Chart and SmartArt carry the same local DrawingML transform as other grouped children.
/// </summary>
public sealed class SetFloatingRotationCommand(
    int paragraphIndex, int runIndex,
    double angleDeg, bool flipH, bool flipV) : IDocumentCommand
{
    private double _prevAngle;
    private bool _prevFlipH, _prevFlipV;
    private bool _applied;

    public string Label => "Rotate/Flip";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryMutate(context, angleDeg, flipH, flipV, out _prevAngle, out _prevFlipH, out _prevFlipV)) return;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied) return;
        TryMutate(context, _prevAngle, _prevFlipH, _prevFlipV, out _, out _, out _);
        _applied = false;
    }

    private bool TryMutate(IDocumentCommandContext context, double a, bool fh, bool fv,
        out double pAngle, out bool pFH, out bool pFV)
    {
        pAngle = 0; pFH = false; pFV = false;
        if (context.Document.Blocks[paragraphIndex] is not Paragraph p) return false;
        if (runIndex < 0 || runIndex >= p.Runs.Count) return false;
        var run = p.Runs[runIndex];
        if (run.Image is { } img)
        {
            pAngle = img.RotationAngle; pFH = img.FlipH; pFV = img.FlipV;
            img.RotationAngle = a; img.FlipH = fh; img.FlipV = fv; return true;
        }
        if (run.Shape is { } shape)
        {
            pAngle = shape.RotationAngle; pFH = shape.FlipH; pFV = shape.FlipV;
            shape.RotationAngle = a; shape.FlipH = fh; shape.FlipV = fv; return true;
        }
        if (run.Chart is { } chart)
        {
            pAngle = chart.RotationAngle; pFH = chart.FlipH; pFV = chart.FlipV;
            chart.RotationAngle = a; chart.FlipH = fh; chart.FlipV = fv; return true;
        }
        if (run.SmartArt is { } smartArt)
        {
            pAngle = smartArt.RotationAngle; pFH = smartArt.FlipH; pFV = smartArt.FlipV;
            smartArt.RotationAngle = a; smartArt.FlipH = fh; smartArt.FlipV = fv; return true;
        }
        if (run.WordArt is { } wordArt)
        {
            pAngle = wordArt.RotationAngle; pFH = wordArt.FlipH; pFV = wordArt.FlipV;
            wordArt.RotationAngle = a; wordArt.FlipH = fh; wordArt.FlipV = fv; return true;
        }
        if (run.DrawingGroup is { } group)
        {
            pAngle = group.RotationAngle; pFH = group.FlipH; pFV = group.FlipV;
            group.RotationAngle = a; group.FlipH = fh; group.FlipV = fv; return true;
        }
        return false;
    }
}

/// <summary>
/// Set the rotation and flips on one child of a floating drawing group.
/// The group remains the owning floating run; this command only changes the child's
/// local transform and is undoable through the normal document command bus.
/// </summary>
public sealed class SetDrawingGroupChildRotationCommand : IDocumentCommand
{
    private readonly int _paragraphIndex;
    private readonly int _runIndex;
    private readonly IReadOnlyList<int> _childPath;
    private readonly double _angleDeg;
    private readonly bool _flipH;
    private readonly bool _flipV;
    private double _previousAngle;
    private bool _previousFlipH;
    private bool _previousFlipV;
    private bool _applied;

    public SetDrawingGroupChildRotationCommand(
        int paragraphIndex,
        int runIndex,
        int childIndex,
        double angleDeg,
        bool flipH,
        bool flipV)
        : this(paragraphIndex, runIndex, [childIndex], angleDeg, flipH, flipV)
    {
    }

    public SetDrawingGroupChildRotationCommand(
        int paragraphIndex,
        int runIndex,
        IReadOnlyList<int> childPath,
        double angleDeg,
        bool flipH,
        bool flipV)
    {
        _paragraphIndex = paragraphIndex;
        _runIndex = runIndex;
        _childPath = childPath.ToArray();
        _angleDeg = angleDeg;
        _flipH = flipH;
        _flipV = flipV;
    }

    public string Label => "Rotate Group Child";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryMutate(context, _angleDeg, _flipH, _flipV,
                out _previousAngle, out _previousFlipH, out _previousFlipV))
            return;

        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied)
            return;

        TryMutate(context, _previousAngle, _previousFlipH, _previousFlipV,
            out _, out _, out _);
        _applied = false;
    }

    private bool TryMutate(
        IDocumentCommandContext context,
        double angle,
        bool flipH,
        bool flipV,
        out double previousAngle,
        out bool previousFlipH,
        out bool previousFlipV)
    {
        previousAngle = 0;
        previousFlipH = false;
        previousFlipV = false;

        if (context.Document.Blocks[_paragraphIndex] is not Paragraph paragraph
            || _runIndex < 0
            || _runIndex >= paragraph.Runs.Count
            || paragraph.Runs[_runIndex].DrawingGroup is not { } rootGroup
            || !DrawingGroupChildPathResolver.TryGetChild(
                rootGroup,
                _childPath,
                out _,
                out var child))
            return false;

        switch (child)
        {
            case InlineImage image:
                previousAngle = image.RotationAngle;
                previousFlipH = image.FlipH;
                previousFlipV = image.FlipV;
                image.RotationAngle = angle;
                image.FlipH = flipH;
                image.FlipV = flipV;
                return true;

            case Shape shape:
                previousAngle = shape.RotationAngle;
                previousFlipH = shape.FlipH;
                previousFlipV = shape.FlipV;
                shape.RotationAngle = angle;
                shape.FlipH = flipH;
                shape.FlipV = flipV;
                return true;

            case WordArt wordArt:
                previousAngle = wordArt.RotationAngle;
                previousFlipH = wordArt.FlipH;
                previousFlipV = wordArt.FlipV;
                wordArt.RotationAngle = angle;
                wordArt.FlipH = flipH;
                wordArt.FlipV = flipV;
                return true;

            case Chart chart:
                previousAngle = chart.RotationAngle;
                previousFlipH = chart.FlipH;
                previousFlipV = chart.FlipV;
                chart.RotationAngle = angle;
                chart.FlipH = flipH;
                chart.FlipV = flipV;
                return true;

            case SmartArt smartArt:
                previousAngle = smartArt.RotationAngle;
                previousFlipH = smartArt.FlipH;
                previousFlipV = smartArt.FlipV;
                smartArt.RotationAngle = angle;
                smartArt.FlipH = flipH;
                smartArt.FlipV = flipV;
                return true;

            case DrawingGroup nestedGroup:
                previousAngle = nestedGroup.RotationAngle;
                previousFlipH = nestedGroup.FlipH;
                previousFlipV = nestedGroup.FlipV;
                nestedGroup.RotationAngle = angle;
                nestedGroup.FlipH = flipH;
                nestedGroup.FlipV = flipV;
                return true;

            default:
                return false;
        }
    }
}

/// <summary>
/// Set the group-local offset of one child. The owning group remains inside the floating run;
/// only the selected child's <see cref="DrawingGroup.ChildOffsets"/> entry changes.
/// </summary>
public sealed class SetDrawingGroupChildPositionCommand : IDocumentCommand
{
    private readonly int _paragraphIndex;
    private readonly int _runIndex;
    private readonly IReadOnlyList<int> _childPath;
    private readonly double _horizontalOffsetPt;
    private readonly double _verticalOffsetPt;
    private double _previousHorizontalOffsetPt;
    private double _previousVerticalOffsetPt;
    private bool _applied;

    public SetDrawingGroupChildPositionCommand(
        int paragraphIndex,
        int runIndex,
        int childIndex,
        double horizontalOffsetPt,
        double verticalOffsetPt)
        : this(paragraphIndex, runIndex, [childIndex], horizontalOffsetPt, verticalOffsetPt)
    {
    }

    public SetDrawingGroupChildPositionCommand(
        int paragraphIndex,
        int runIndex,
        IReadOnlyList<int> childPath,
        double horizontalOffsetPt,
        double verticalOffsetPt)
    {
        _paragraphIndex = paragraphIndex;
        _runIndex = runIndex;
        _childPath = childPath.ToArray();
        _horizontalOffsetPt = horizontalOffsetPt;
        _verticalOffsetPt = verticalOffsetPt;
    }

    public string Label => "Move Group Child";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryMutate(context, _horizontalOffsetPt, _verticalOffsetPt,
                out _previousHorizontalOffsetPt, out _previousVerticalOffsetPt))
            return;

        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied)
            return;

        TryMutate(context, _previousHorizontalOffsetPt, _previousVerticalOffsetPt,
            out _, out _);
        _applied = false;
    }

    private bool TryMutate(
        IDocumentCommandContext context,
        double horizontalOffsetPt,
        double verticalOffsetPt,
        out double previousHorizontalOffsetPt,
        out double previousVerticalOffsetPt)
    {
        previousHorizontalOffsetPt = 0;
        previousVerticalOffsetPt = 0;
        if (!TryGetChild(context, out var group, out var child))
            return false;

        var childIndex = _childPath[^1];
        EnsureOffsetSlot(group, childIndex);
        var previous = group.ChildOffsets[childIndex];
        previousHorizontalOffsetPt = previous.X;
        previousVerticalOffsetPt = previous.Y;
        group.ChildOffsets[childIndex] = (horizontalOffsetPt, verticalOffsetPt);
        return true;
    }

    private bool TryGetChild(
        IDocumentCommandContext context,
        out DrawingGroup owningGroup,
        out object child)
    {
        owningGroup = null!;
        child = null!;
        if (context.Document.Blocks[_paragraphIndex] is not Paragraph paragraph
            || _runIndex < 0
            || _runIndex >= paragraph.Runs.Count
            || paragraph.Runs[_runIndex].DrawingGroup is not { } candidate)
            return false;

        return DrawingGroupChildPathResolver.TryGetChild(
            candidate, _childPath, out owningGroup, out child);
    }

    public static void EnsureOffsetSlot(DrawingGroup group, int childIndex)
    {
        while (group.ChildOffsets.Count <= childIndex)
            group.ChildOffsets.Add((0, 0));
    }
}

/// <summary>
/// Reorder one direct or nested group child while keeping its local offset paired with it.
/// DrawingML group child order is the child-local paint order, so this command does not mutate
/// the floating root's page-level <see cref="FloatingPlacement.ZOrderIndex"/>.
/// </summary>
public sealed class ChangeDrawingGroupChildZOrderCommand : IDocumentCommand
{
    private readonly int _paragraphIndex;
    private readonly int _runIndex;
    private readonly IReadOnlyList<int> _childPath;
    private readonly ZOrderOperation _operation;
    private DrawingGroup? _owningGroup;
    private object[]? _childrenSnapshot;
    private (double X, double Y)[]? _offsetsSnapshot;

    public ChangeDrawingGroupChildZOrderCommand(
        int paragraphIndex,
        int runIndex,
        IReadOnlyList<int> childPath,
        ZOrderOperation operation)
    {
        _paragraphIndex = paragraphIndex;
        _runIndex = runIndex;
        _childPath = childPath.ToArray();
        _operation = operation;
    }

    public string Label => _operation switch
    {
        ZOrderOperation.BringToFront => "Bring Group Child to Front",
        ZOrderOperation.SendToBack => "Send Group Child to Back",
        ZOrderOperation.BringForward => "Bring Group Child Forward",
        ZOrderOperation.SendBackward => "Send Group Child Backward",
        _ => "Reorder Group Child"
    };

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetOwningGroup(context, out var owningGroup) || _childPath.Count == 0)
            return;

        var sourceIndex = _childPath[^1];
        var targetIndex = ResolveTargetIndex(sourceIndex, owningGroup.Children.Count, _operation);
        if (targetIndex == sourceIndex)
            return;

        _owningGroup = owningGroup;
        _childrenSnapshot = owningGroup.Children.ToArray();
        _offsetsSnapshot = owningGroup.ChildOffsets.ToArray();
        SetDrawingGroupChildPositionCommand.EnsureOffsetSlot(
            owningGroup, owningGroup.Children.Count - 1);

        var child = owningGroup.Children[sourceIndex];
        var offset = owningGroup.ChildOffsets[sourceIndex];
        owningGroup.Children.RemoveAt(sourceIndex);
        owningGroup.ChildOffsets.RemoveAt(sourceIndex);
        owningGroup.Children.Insert(targetIndex, child);
        owningGroup.ChildOffsets.Insert(targetIndex, offset);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (_owningGroup is null || _childrenSnapshot is null || _offsetsSnapshot is null)
            return;

        _owningGroup.Children.Clear();
        foreach (var child in _childrenSnapshot)
            _owningGroup.Children.Add(child);
        _owningGroup.ChildOffsets.Clear();
        foreach (var offset in _offsetsSnapshot)
            _owningGroup.ChildOffsets.Add(offset);
        _owningGroup = null;
        _childrenSnapshot = null;
        _offsetsSnapshot = null;
    }

    public static int ResolveTargetIndex(
        int sourceIndex,
        int childCount,
        ZOrderOperation operation)
    {
        if (sourceIndex < 0 || sourceIndex >= childCount)
            return sourceIndex;
        return operation switch
        {
            ZOrderOperation.BringToFront => childCount - 1,
            ZOrderOperation.SendToBack => 0,
            ZOrderOperation.BringForward => Math.Min(sourceIndex + 1, childCount - 1),
            ZOrderOperation.SendBackward => Math.Max(sourceIndex - 1, 0),
            _ => sourceIndex
        };
    }

    private bool TryGetOwningGroup(
        IDocumentCommandContext context,
        out DrawingGroup owningGroup)
    {
        owningGroup = null!;
        if (_paragraphIndex < 0
            || _paragraphIndex >= context.Document.Blocks.Count
            || context.Document.Blocks[_paragraphIndex] is not Paragraph paragraph
            || _runIndex < 0
            || _runIndex >= paragraph.Runs.Count
            || paragraph.Runs[_runIndex].DrawingGroup is not { } root)
        {
            return false;
        }

        return DrawingGroupChildPathResolver.TryGetChild(
            root, _childPath, out owningGroup, out _);
    }
}

/// <summary>
/// Set the local width and height of one group child. WordArt has no stored width/height;
/// its font size is scaled proportionally so its derived child bounds follow the resize gesture.
/// </summary>
public sealed class SetDrawingGroupChildSizeCommand : IDocumentCommand
{
    private readonly int _paragraphIndex;
    private readonly int _runIndex;
    private readonly IReadOnlyList<int> _childPath;
    private readonly double _widthPt;
    private readonly double _heightPt;
    private double _previousWidthPt;
    private double _previousHeightPt;
    private double _previousWordArtFontSizePt;
    private bool _applied;

    public SetDrawingGroupChildSizeCommand(
        int paragraphIndex,
        int runIndex,
        int childIndex,
        double widthPt,
        double heightPt)
        : this(paragraphIndex, runIndex, [childIndex], widthPt, heightPt)
    {
    }

    public SetDrawingGroupChildSizeCommand(
        int paragraphIndex,
        int runIndex,
        IReadOnlyList<int> childPath,
        double widthPt,
        double heightPt)
    {
        _paragraphIndex = paragraphIndex;
        _runIndex = runIndex;
        _childPath = childPath.ToArray();
        _widthPt = widthPt;
        _heightPt = heightPt;
    }

    public string Label => "Resize Group Child";

    public void Apply(IDocumentCommandContext context)
    {
        if (_widthPt <= 0 || _heightPt <= 0
            || !TryMutate(context, _widthPt, _heightPt,
                out _previousWidthPt, out _previousHeightPt, out _previousWordArtFontSizePt))
            return;

        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied)
            return;

        TryMutate(context, _previousWidthPt, _previousHeightPt,
            out _, out _, out _,
            restoreWordArtFontSizePt: _previousWordArtFontSizePt);
        _applied = false;
    }

    private bool TryMutate(
        IDocumentCommandContext context,
        double width,
        double height,
        out double previousWidth,
        out double previousHeight,
        out double previousWordArtFontSize,
        double? restoreWordArtFontSizePt = null)
    {
        previousWidth = 0;
        previousHeight = 0;
        previousWordArtFontSize = 0;
        if (!TryGetChild(context, out var group, out var child)
            || width <= 0
            || height <= 0)
            return false;

        var childIndex = _childPath[^1];
        previousWidth = group.ChildWidthPt(childIndex);
        previousHeight = group.ChildHeightPt(childIndex);
        switch (child)
        {
            case InlineImage image:
                image.WidthPt = width;
                image.HeightPt = height;
                return true;
            case Shape shape:
                shape.WidthPt = width;
                shape.HeightPt = height;
                return true;
            case Chart chart:
                chart.WidthPt = width;
                chart.HeightPt = height;
                return true;
            case SmartArt smartArt:
                smartArt.WidthPt = width;
                smartArt.HeightPt = height;
                return true;
            case DrawingGroup nestedGroup:
                nestedGroup.WidthPt = width;
                nestedGroup.HeightPt = height;
                return true;
            case WordArt wordArt:
                previousWordArtFontSize = wordArt.FontSizePt;
                wordArt.FontSizePt = restoreWordArtFontSizePt
                    ?? wordArt.FontSizePt * Math.Min(
                        width / Math.Max(0.01, previousWidth),
                        height / Math.Max(0.01, previousHeight));
                return true;
            default:
                return false;
        }
    }

    private bool TryGetChild(IDocumentCommandContext context, out DrawingGroup group, out object child)
    {
        group = null!;
        child = null!;
        if (context.Document.Blocks[_paragraphIndex] is not Paragraph paragraph
            || _runIndex < 0
            || _runIndex >= paragraph.Runs.Count
            || paragraph.Runs[_runIndex].DrawingGroup is not { } candidate)
            return false;

        return DrawingGroupChildPathResolver.TryGetChild(
            candidate, _childPath, out group, out child);
    }
}

/// <summary>
/// Set only the position offsets (H/V in points) on a floating Image, updating
/// <see cref="InlineImage.HorizontalOffsetPt"/> / <see cref="InlineImage.VerticalOffsetPt"/>
/// while keeping anchors unchanged. Used by drag-move and arrow-key nudge in the Avalonia view.
/// </summary>
public sealed class NudgeImagePositionCommand(
    int paragraphIndex, int runIndex,
    double horizontalOffsetPt, double verticalOffsetPt) : IDocumentCommand
{
    private double _ph, _pv;
    private bool _applied;

    public string Label => "Move";

    public void Apply(IDocumentCommandContext context)
    {
        if (ImageAt(context) is not { IsFloating: true } img) return;
        _ph = img.HorizontalOffsetPt; _pv = img.VerticalOffsetPt;
        img.HorizontalOffsetPt = horizontalOffsetPt; img.VerticalOffsetPt = verticalOffsetPt;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ImageAt(context) is not { IsFloating: true } img) return;
        img.HorizontalOffsetPt = _ph; img.VerticalOffsetPt = _pv;
        _applied = false;
    }

    private InlineImage? ImageAt(IDocumentCommandContext context) =>
        context.Document.Blocks[paragraphIndex] is Paragraph p && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Image : null;
}

/// <summary>
/// Remove the run at (paragraphIndex, runIndex) from its paragraph. Used by
/// <c>DeleteSelectedFloating()</c> in the Avalonia DocumentView. Snaps the removed run for undo.
/// No-op when the block is not a Paragraph or the run index is out of range.
/// </summary>
public sealed class RemoveFloatingRunCommand(int paragraphIndex, int runIndex) : IDocumentCommand
{
    private Run? _removed;
    private bool _applied;

    public string Label => "Delete";

    public void Apply(IDocumentCommandContext context)
    {
        if (context.Document.Blocks[paragraphIndex] is not Paragraph p) return;
        if (runIndex < 0 || runIndex >= p.Runs.Count) return;
        _removed = p.Runs[runIndex];
        p.Runs.RemoveAt(runIndex);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || _removed is null) return;
        if (context.Document.Blocks[paragraphIndex] is not Paragraph p) return;
        var at = Math.Clamp(runIndex, 0, p.Runs.Count);
        p.Runs.Insert(at, _removed);
        _applied = false;
    }
}

/// <summary>
/// Give every row in a table the same height, snapshotting each row's prior height and height rule
/// for undo. The target height is calculated by <see cref="TableLayoutOperations.DistributeRows"/>.
/// </summary>
public sealed class DistributeTableRowsCommand(int blockIndex) : IDocumentCommand
{
    private (double? HeightPt, TableRowHeightRule HeightRule)[]? _previous;
    private bool _applied;

    public string Label => "Distribute Rows";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetTable(context, out var table) || table.Rows.Count == 0)
            return;

        _previous = table.Rows.Select(row => (row.HeightPt, row.HeightRule)).ToArray();
        _applied = TableLayoutOperations.DistributeRows(table);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || _previous is null || !TryGetTable(context, out var table))
            return;

        for (var i = 0; i < Math.Min(table.Rows.Count, _previous.Length); i++)
        {
            table.Rows[i].HeightPt = _previous[i].HeightPt;
            table.Rows[i].HeightRule = _previous[i].HeightRule;
        }
        _applied = false;
    }

    private bool TryGetTable(IDocumentCommandContext context, out Table table)
    {
        table = null!;
        return blockIndex >= 0
            && blockIndex < context.Document.Blocks.Count
            && context.Document.Blocks[blockIndex] is Table resolved
            && (table = resolved) is not null;
    }
}

/// <summary>
/// Give every table column the same width, snapshotting the prior grid and per-cell widths for undo.
/// </summary>
public sealed class DistributeTableColumnsCommand(int blockIndex) : IDocumentCommand
{
    private double[]? _previousGridWidths;
    private double?[][]? _previousCellWidths;
    private bool _applied;

    public string Label => "Distribute Columns";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetTable(context, out var table) || table.ColumnCount == 0)
            return;

        CaptureWidths(table);
        _applied = TableLayoutOperations.DistributeColumns(table);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || !TryGetTable(context, out var table))
            return;

        RestoreWidths(table);
        _applied = false;
    }

    private void CaptureWidths(Table table)
    {
        _previousGridWidths = [.. table.ColumnWidthsPt];
        _previousCellWidths = table.Rows
            .Select(row => row.Cells.Select(cell => cell.WidthPt).ToArray())
            .ToArray();
    }

    private void RestoreWidths(Table table)
    {
        table.ColumnWidthsPt.Clear();
        if (_previousGridWidths is not null)
            table.ColumnWidthsPt.AddRange(_previousGridWidths);

        if (_previousCellWidths is null)
            return;
        for (var rowIndex = 0; rowIndex < Math.Min(table.Rows.Count, _previousCellWidths.Length); rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var widths = _previousCellWidths[rowIndex];
            for (var cellIndex = 0; cellIndex < Math.Min(row.Cells.Count, widths.Length); cellIndex++)
                row.Cells[cellIndex].WidthPt = widths[cellIndex];
        }
    }

    private bool TryGetTable(IDocumentCommandContext context, out Table table)
    {
        table = null!;
        return blockIndex >= 0
            && blockIndex < context.Document.Blocks.Count
            && context.Document.Blocks[blockIndex] is Table resolved
            && (table = resolved) is not null;
    }
}

/// <summary>
/// Apply a Word table AutoFit mode, snapshotting the mode and every width field that
/// <see cref="TableLayoutOperations.SetAutoFit"/> can mutate for undo.
/// </summary>
public sealed class SetTableAutoFitCommand(int blockIndex, AutoFitMode mode) : IDocumentCommand
{
    private AutoFitMode _previousMode;
    private double? _previousPreferredWidthPt;
    private double[]? _previousGridWidths;
    private double?[][]? _previousCellWidths;
    private bool _applied;

    public string Label => "AutoFit Table";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetTable(context, out var table))
            return;

        _previousMode = table.AutoFit;
        _previousPreferredWidthPt = table.PreferredWidthPt;
        _previousGridWidths = [.. table.ColumnWidthsPt];
        _previousCellWidths = table.Rows
            .Select(row => row.Cells.Select(cell => cell.WidthPt).ToArray())
            .ToArray();
        _applied = TableLayoutOperations.SetAutoFit(table, mode);
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || !TryGetTable(context, out var table))
            return;

        table.AutoFit = _previousMode;
        table.PreferredWidthPt = _previousPreferredWidthPt;
        table.ColumnWidthsPt.Clear();
        if (_previousGridWidths is not null)
            table.ColumnWidthsPt.AddRange(_previousGridWidths);
        if (_previousCellWidths is not null)
        {
            for (var rowIndex = 0; rowIndex < Math.Min(table.Rows.Count, _previousCellWidths.Length); rowIndex++)
            {
                var row = table.Rows[rowIndex];
                var widths = _previousCellWidths[rowIndex];
                for (var cellIndex = 0; cellIndex < Math.Min(row.Cells.Count, widths.Length); cellIndex++)
                    row.Cells[cellIndex].WidthPt = widths[cellIndex];
            }
        }
        _applied = false;
    }

    private bool TryGetTable(IDocumentCommandContext context, out Table table)
    {
        table = null!;
        return blockIndex >= 0
            && blockIndex < context.Document.Blocks.Count
            && context.Document.Blocks[blockIndex] is Table resolved
            && (table = resolved) is not null;
    }
}

/// <summary>
/// Replace the <see cref="TableFormatting"/> on the table at <paramref name="blockIndex"/>.
/// The previous formatting is snapshot-ed for undo. Out-of-range block index or a block that
/// is not a <see cref="Table"/> are silently ignored (no-op).
/// </summary>
public sealed class SetTableFormattingCommand(int blockIndex, TableFormatting newFormatting) : IDocumentCommand
{
    private TableFormatting _previous = TableFormatting.Default;
    private bool _applied;

    public string Label => "Change Table Formatting";

    public void Apply(IDocumentCommandContext context)
    {
        if (!TryGetTable(context, out var table)) return;
        _previous = table.Formatting;
        table.Formatting = newFormatting;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || !TryGetTable(context, out var table)) return;
        table.Formatting = _previous;
        _applied = false;
    }

    private bool TryGetTable(IDocumentCommandContext context, out Table table)
    {
        table = null!;
        if (blockIndex < 0 || blockIndex >= context.Document.Blocks.Count) return false;
        if (context.Document.Blocks[blockIndex] is not Table t) return false;
        table = t;
        return true;
    }
}

// ─── AV-CHARTTAB: Chart + SmartArt contextual-tab edit commands ──────────────────────────────────
//
// Each command mutates the Chart / SmartArt carried by the run at (paragraphIndex, runIndex), snapping
// the prior value for undo. They mirror the WPF FreeW chart/smartart contextual-tab editors (a reasonable
// subset: chart kind/style/colour-scheme + smartart layout/colour/style). All safely no-op when the run
// at the address is not the expected kind.

/// <summary>
/// Helper to resolve the <see cref="Chart"/> carried by a run, or null.
/// </summary>
internal static class ChartSmartArtCommandHelpers
{
    public static Chart? ChartAt(IDocumentCommandContext context, int paragraphIndex, int runIndex)
        => context.Document.Blocks.Count > paragraphIndex && paragraphIndex >= 0
           && context.Document.Blocks[paragraphIndex] is Paragraph p
           && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].Chart : null;

    public static SmartArt? SmartArtAt(IDocumentCommandContext context, int paragraphIndex, int runIndex)
        => context.Document.Blocks.Count > paragraphIndex && paragraphIndex >= 0
           && context.Document.Blocks[paragraphIndex] is Paragraph p
           && runIndex >= 0 && runIndex < p.Runs.Count
            ? p.Runs[runIndex].SmartArt : null;
}

/// <summary>
/// Change the <see cref="Chart.Kind"/> of the chart carried by the run at (paragraphIndex, runIndex).
/// Snaps the prior kind for undo. No-op when the run carries no chart.
/// </summary>
public sealed class SetChartKindCommand(int paragraphIndex, int runIndex, ChartKind kind) : IDocumentCommand
{
    private ChartKind _previous;
    private bool _applied;

    public string Label => "Change Chart Type";

    public void Apply(IDocumentCommandContext context)
    {
        if (ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart) return;
        _previous = chart.Kind;
        chart.Kind = kind;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart) return;
        chart.Kind = _previous;
        _applied = false;
    }
}

/// <summary>
/// Set the <see cref="Chart.StyleId"/> of the chart carried by the run at (paragraphIndex, runIndex).
/// Snaps the prior style id for undo. No-op when the run carries no chart.
/// </summary>
public sealed class SetChartStyleCommand(int paragraphIndex, int runIndex, int styleId) : IDocumentCommand
{
    private int _previous;
    private bool _applied;

    public string Label => "Change Chart Style";

    public void Apply(IDocumentCommandContext context)
    {
        if (ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart) return;
        _previous = chart.StyleId;
        chart.StyleId = styleId;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart) return;
        chart.StyleId = _previous;
        _applied = false;
    }
}

/// <summary>
/// Set the <see cref="Chart.ColorSchemeId"/> of the chart carried by the run at (paragraphIndex, runIndex).
/// Snaps the prior scheme id for undo. No-op when the run carries no chart.
/// </summary>
public sealed class SetChartColorSchemeCommand(int paragraphIndex, int runIndex, string? colorSchemeId) : IDocumentCommand
{
    private string? _previous;
    private bool _applied;

    public string Label => "Change Chart Colors";

    public void Apply(IDocumentCommandContext context)
    {
        if (ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart) return;
        _previous = chart.ColorSchemeId;
        chart.ColorSchemeId = colorSchemeId;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart) return;
        chart.ColorSchemeId = _previous;
        _applied = false;
    }
}

/// <summary>
/// Apply a built-in <see cref="ChartQuickLayout"/> to the chart carried by the run at
/// (paragraphIndex, runIndex). The layout id is an overlay interpreted by the shared chart planner;
/// chart data, style, colours, titles, and explicit element defaults remain intact. Snaps the prior
/// layout id for undo. No-op when the run carries no chart.
/// </summary>
public sealed class SetChartQuickLayoutCommand(
    int paragraphIndex,
    int runIndex,
    ChartQuickLayout layout) : IDocumentCommand
{
    private int _previous;
    private bool _applied;

    public string Label => "Change Chart Layout";

    public void Apply(IDocumentCommandContext context)
    {
        if (ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart)
            return;

        _previous = chart.QuickLayoutId;
        chart.QuickLayoutId = layout.Id;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart)
            return;

        chart.QuickLayoutId = _previous;
        _applied = false;
    }
}

/// <summary>
/// Set whether the chart carried by the run at (paragraphIndex, runIndex) shows a legend.
/// Snaps the prior legend flag and quick-layout id for undo. No-op when the run carries no chart.
/// </summary>
public sealed class SetChartLegendCommand(int paragraphIndex, int runIndex, bool showLegend) : IDocumentCommand
{
    private bool _previous;
    private int _previousQuickLayoutId;
    private bool _applied;

    public string Label => "Toggle Chart Legend";

    public void Apply(IDocumentCommandContext context)
    {
        if (ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart) return;
        _previous = chart.ShowLegend;
        _previousQuickLayoutId = chart.QuickLayoutId;
        chart.ShowLegend = showLegend;
        chart.QuickLayoutId = 0;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart) return;
        chart.ShowLegend = _previous;
        chart.QuickLayoutId = _previousQuickLayoutId;
        _applied = false;
    }
}

/// <summary>
/// Set or clear the title for the chart carried by the run at (paragraphIndex, runIndex).
/// Snaps the prior title and quick-layout id for undo. No-op when the run carries no chart.
/// </summary>
public sealed class SetChartTitleCommand(int paragraphIndex, int runIndex, string? title) : IDocumentCommand
{
    private string? _previousTitle;
    private int _previousQuickLayoutId;
    private bool _applied;

    public string Label => "Set Chart Title";

    public void Apply(IDocumentCommandContext context)
    {
        if (ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart)
            return;

        _previousTitle = chart.Title;
        _previousQuickLayoutId = chart.QuickLayoutId;
        chart.Title = Normalize(title);
        chart.QuickLayoutId = 0;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart)
            return;

        chart.Title = _previousTitle;
        chart.QuickLayoutId = _previousQuickLayoutId;
        _applied = false;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Set or clear the axis titles for the chart carried by the run at (paragraphIndex, runIndex).
/// Snaps the prior axis titles and quick-layout id for undo. No-op for axis-less chart kinds.
/// </summary>
public sealed class SetChartAxisTitlesCommand(
    int paragraphIndex,
    int runIndex,
    string? categoryAxisTitle,
    string? valueAxisTitle) : IDocumentCommand
{
    private string? _previousCategoryAxisTitle;
    private string? _previousValueAxisTitle;
    private int _previousQuickLayoutId;
    private bool _applied;

    public string Label => "Set Chart Axis Titles";

    public void Apply(IDocumentCommandContext context)
    {
        if (ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart)
            return;
        if (chart.Kind is ChartKind.Pie or ChartKind.Doughnut)
            return;

        _previousCategoryAxisTitle = chart.CategoryAxisTitle;
        _previousValueAxisTitle = chart.ValueAxisTitle;
        _previousQuickLayoutId = chart.QuickLayoutId;
        chart.CategoryAxisTitle = Normalize(categoryAxisTitle);
        chart.ValueAxisTitle = Normalize(valueAxisTitle);
        chart.QuickLayoutId = 0;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart)
            return;

        chart.CategoryAxisTitle = _previousCategoryAxisTitle;
        chart.ValueAxisTitle = _previousValueAxisTitle;
        chart.QuickLayoutId = _previousQuickLayoutId;
        _applied = false;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Replace the editable data on the chart carried by the run at (paragraphIndex, runIndex).
/// Snaps the prior chart data for undo. No-op when the run carries no chart.
/// </summary>
public sealed class ReplaceChartDataCommand(int paragraphIndex, int runIndex, Chart replacement) : IDocumentCommand
{
    private Chart? _previous;
    private bool _applied;

    public string Label => "Edit Chart Data";

    public void Apply(IDocumentCommandContext context)
    {
        if (ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart) return;
        _previous = Clone(chart);
        Copy(replacement, chart);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || _previous is null || ChartSmartArtCommandHelpers.ChartAt(context, paragraphIndex, runIndex) is not { } chart) return;
        Copy(_previous, chart);
        _applied = false;
    }

    private static Chart Clone(Chart source) => source.Clone();

    private static void Copy(Chart source, Chart target)
    {
        target.Kind = source.Kind;
        target.Title = source.Title;
        target.ShowLegend = source.ShowLegend;
        target.CategoryAxisTitle = source.CategoryAxisTitle;
        target.ValueAxisTitle = source.ValueAxisTitle;
        if (source.WidthPt > 0)
            target.WidthPt = source.WidthPt;
        if (source.HeightPt > 0)
            target.HeightPt = source.HeightPt;
        target.Categories.Clear();
        target.Categories.AddRange(source.Categories);
        target.Series.Clear();
        foreach (var series in source.Series)
            target.Series.Add(new ChartSeries(series.Name, series.Values));
    }
}

/// <summary>
/// Set the <see cref="SmartArt.Kind"/> (layout family) of the SmartArt carried by the run at
/// (paragraphIndex, runIndex). Snaps the prior kind for undo. No-op when the run carries no SmartArt.
/// </summary>
public sealed class SetSmartArtLayoutCommand(
    int paragraphIndex,
    int runIndex,
    SmartArtKind kind,
    string? layoutId = null) : IDocumentCommand
{
    private SmartArtKind _previous;
    private string? _previousLayoutId;
    private bool _applied;

    public string Label => "Change SmartArt Layout";

    public void Apply(IDocumentCommandContext context)
    {
        if (ChartSmartArtCommandHelpers.SmartArtAt(context, paragraphIndex, runIndex) is not { } sa) return;
        _previous = sa.Kind;
        _previousLayoutId = sa.LayoutId;
        sa.Kind = kind;
        if (layoutId is not null)
            sa.LayoutId = layoutId;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ChartSmartArtCommandHelpers.SmartArtAt(context, paragraphIndex, runIndex) is not { } sa) return;
        sa.Kind = _previous;
        sa.LayoutId = _previousLayoutId;
        _applied = false;
    }
}

/// <summary>
/// Set the <see cref="SmartArt.ColorSchemeId"/> of the SmartArt carried by the run at
/// (paragraphIndex, runIndex). Snaps the prior scheme id for undo. No-op when the run carries no SmartArt.
/// </summary>
public sealed class SetSmartArtColorCommand(int paragraphIndex, int runIndex, string? colorSchemeId) : IDocumentCommand
{
    private string? _previous;
    private bool _applied;

    public string Label => "Change SmartArt Colors";

    public void Apply(IDocumentCommandContext context)
    {
        if (ChartSmartArtCommandHelpers.SmartArtAt(context, paragraphIndex, runIndex) is not { } sa) return;
        _previous = sa.ColorSchemeId;
        sa.ColorSchemeId = colorSchemeId;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ChartSmartArtCommandHelpers.SmartArtAt(context, paragraphIndex, runIndex) is not { } sa) return;
        sa.ColorSchemeId = _previous;
        _applied = false;
    }
}

/// <summary>Structural SmartArt operation shared by the WPF and Avalonia contextual ribbons.</summary>
public enum SmartArtStructureOperation
{
    AddShape,
    RemoveShape,
    Promote,
    Demote,
    MoveUp,
    MoveDown,
}

/// <summary>
/// Apply one structural operation to the SmartArt carried by the addressed run. The complete diagram is
/// snapshotted so undo restores node text and hierarchy while size, placement, layout, colors, and style
/// remain unchanged by the operation itself.
/// </summary>
public sealed class MutateSmartArtStructureCommand(
    int paragraphIndex,
    int runIndex,
    SmartArtStructureOperation operation) : IDocumentCommand
{
    private SmartArt? _previous;
    private bool _applied;

    public string Label => operation switch
    {
        SmartArtStructureOperation.AddShape => "Add SmartArt Shape",
        SmartArtStructureOperation.RemoveShape => "Remove SmartArt Shape",
        SmartArtStructureOperation.Promote => "Promote SmartArt Shape",
        SmartArtStructureOperation.Demote => "Demote SmartArt Shape",
        SmartArtStructureOperation.MoveUp => "Move SmartArt Shape Up",
        SmartArtStructureOperation.MoveDown => "Move SmartArt Shape Down",
        _ => "Edit SmartArt",
    };

    public void Apply(IDocumentCommandContext context)
    {
        if (ChartSmartArtCommandHelpers.SmartArtAt(context, paragraphIndex, runIndex) is not { } smartArt
            || !CanApply(smartArt, operation))
        {
            return;
        }

        _previous = SmartArtCommandCopy.Clone(smartArt);
        ApplyOperation(smartArt, operation);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || _previous is null
            || ChartSmartArtCommandHelpers.SmartArtAt(context, paragraphIndex, runIndex) is not { } smartArt)
        {
            return;
        }

        SmartArtCommandCopy.Copy(_previous, smartArt);
        _applied = false;
    }

    public static bool CanApply(SmartArt? smartArt, SmartArtStructureOperation candidate) =>
        smartArt is not null && candidate switch
        {
            SmartArtStructureOperation.AddShape => true,
            SmartArtStructureOperation.RemoveShape => smartArt.Nodes.Count > 1,
            SmartArtStructureOperation.Promote => smartArt.Kind == SmartArtKind.Hierarchy
                && smartArt.Nodes.Any(node => node.Children.Count > 0),
            SmartArtStructureOperation.Demote => smartArt.Kind == SmartArtKind.Hierarchy
                && smartArt.Nodes.Count > 1,
            SmartArtStructureOperation.MoveUp or SmartArtStructureOperation.MoveDown => smartArt.Nodes.Count > 1,
            _ => false,
        };

    private static void ApplyOperation(SmartArt smartArt, SmartArtStructureOperation candidate)
    {
        switch (candidate)
        {
            case SmartArtStructureOperation.AddShape:
                smartArt.Nodes.Add(new SmartArtNode("New Item"));
                break;
            case SmartArtStructureOperation.RemoveShape:
                smartArt.Nodes.RemoveAt(smartArt.Nodes.Count - 1);
                break;
            case SmartArtStructureOperation.Promote:
                for (var index = 0; index < smartArt.Nodes.Count; index++)
                {
                    var parent = smartArt.Nodes[index];
                    if (parent.Children.Count == 0)
                        continue;
                    var promoted = parent.Children[^1];
                    parent.Children.RemoveAt(parent.Children.Count - 1);
                    smartArt.Nodes.Insert(index + 1, promoted);
                    break;
                }
                break;
            case SmartArtStructureOperation.Demote:
                var demoted = smartArt.Nodes[^1];
                smartArt.Nodes.RemoveAt(smartArt.Nodes.Count - 1);
                smartArt.Nodes[^1].Children.Add(demoted);
                break;
            case SmartArtStructureOperation.MoveUp:
                var last = smartArt.Nodes.Count - 1;
                (smartArt.Nodes[last], smartArt.Nodes[last - 1]) = (smartArt.Nodes[last - 1], smartArt.Nodes[last]);
                break;
            case SmartArtStructureOperation.MoveDown:
                (smartArt.Nodes[0], smartArt.Nodes[1]) = (smartArt.Nodes[1], smartArt.Nodes[0]);
                break;
        }
    }
}

/// <summary>
/// Replace the selected diagram's kind and node tree while preserving its geometry, placement, layout,
/// colors, and style. This backs the shared Edit Text workflow and is fully undoable.
/// </summary>
public sealed class ReplaceSmartArtContentCommand(
    int paragraphIndex,
    int runIndex,
    SmartArt replacement) : IDocumentCommand
{
    private SmartArt? _previous;
    private bool _applied;

    public string Label => "Edit SmartArt Text";

    public void Apply(IDocumentCommandContext context)
    {
        if (ChartSmartArtCommandHelpers.SmartArtAt(context, paragraphIndex, runIndex) is not { } smartArt)
            return;

        _previous = SmartArtCommandCopy.Clone(smartArt);
        smartArt.Kind = replacement.Kind;
        SmartArtCommandCopy.CopyNodes(replacement.Nodes, smartArt.Nodes);
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || _previous is null
            || ChartSmartArtCommandHelpers.SmartArtAt(context, paragraphIndex, runIndex) is not { } smartArt)
        {
            return;
        }

        SmartArtCommandCopy.Copy(_previous, smartArt);
        _applied = false;
    }
}

/// <summary>Apply a shared SmartArt style catalog entry and preserve every unrelated diagram property.</summary>
public sealed class SetSmartArtStyleCommand(int paragraphIndex, int runIndex, string? styleId) : IDocumentCommand
{
    private string? _previous;
    private bool _applied;

    public string Label => "Change SmartArt Style";

    public void Apply(IDocumentCommandContext context)
    {
        if (ChartSmartArtCommandHelpers.SmartArtAt(context, paragraphIndex, runIndex) is not { } smartArt)
            return;
        _previous = smartArt.StyleId;
        smartArt.StyleId = styleId;
        _applied = true;
    }

    public void Revert(IDocumentCommandContext context)
    {
        if (!_applied || ChartSmartArtCommandHelpers.SmartArtAt(context, paragraphIndex, runIndex) is not { } smartArt)
            return;
        smartArt.StyleId = _previous;
        _applied = false;
    }
}

internal static class SmartArtCommandCopy
{
    public static SmartArt Clone(SmartArt source)
    {
        var clone = new SmartArt();
        Copy(source, clone);
        return clone;
    }

    public static void Copy(SmartArt source, SmartArt target)
    {
        target.Kind = source.Kind;
        target.WidthPt = source.WidthPt;
        target.HeightPt = source.HeightPt;
        target.RotationAngle = source.RotationAngle;
        target.FlipH = source.FlipH;
        target.FlipV = source.FlipV;
        target.Placement = source.Placement is null
            ? null
            : new FloatingPlacement
            {
                Wrapping = source.Placement.Wrapping,
                HorizontalOffsetPt = source.Placement.HorizontalOffsetPt,
                VerticalOffsetPt = source.Placement.VerticalOffsetPt,
                HorizontalAnchor = source.Placement.HorizontalAnchor,
                VerticalAnchor = source.Placement.VerticalAnchor,
                ZOrderIndex = source.Placement.ZOrderIndex,
            };
        target.LayoutId = source.LayoutId;
        target.ColorSchemeId = source.ColorSchemeId;
        target.StyleId = source.StyleId;
        CopyNodes(source.Nodes, target.Nodes);
    }

    public static void CopyNodes(IEnumerable<SmartArtNode> source, List<SmartArtNode> target)
    {
        target.Clear();
        target.AddRange(source.Select(CloneNode));
    }

    private static SmartArtNode CloneNode(SmartArtNode source) =>
        new(source.Text, source.Children.Select(CloneNode));
}

