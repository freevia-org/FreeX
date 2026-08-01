using Free.Shared.Commands;
using Free.Shared.Drawing;

namespace FreeP.Core.Model;

// ════════════════════════════════════════════════════════════════════════════════
// TABLE EDIT COMMANDS  (Wave 9A)
//
// All commands work on a table shape identified by (slideIndex, shapeId).
// The helper FindTable() retrieves the TableShape; if the shape is not found
// or its Table payload is null the command is a no-op (safe to call).
//
// Undo/redo contract: every command captures the minimum prior state needed
// to fully revert.  Captured data is deep-cloned so later mutations cannot
// corrupt the snapshot.
//
// Merge semantics follow OOXML a:tbl:
//   • The top-left cell of the merged region is the "anchor": GridSpan/RowSpan > 1.
//   • Every other cell in the region has HMerge=true (same row) or VMerge=true
//     (rows below the first), with GridSpan=1, RowSpan=1.
//   • The compositor skips HMerge/VMerge cells and sizes the anchor cell by
//     summing its GridSpan columns and RowSpan rows.
// ════════════════════════════════════════════════════════════════════════════════

// Shared table clone/grid helpers live in PresentationModelCloneHelper.

public sealed class SetTableHeaderRowCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly bool _newValue;
    private bool _oldValue;

    public SetTableHeaderRowCommand(int slideIndex, uint shapeId, bool newValue)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newValue = newValue;
    }

    public string Label => _newValue ? "Set Header Row" : "Clear Header Row";

    public bool HasEffect(Presentation presentation)
    {
        var table = PresentationModelCloneHelper.FindTable(presentation, _slideIndex, _shapeId);
        return table is not null && table.Flags.FirstRow != _newValue;
    }

    public void Apply(Presentation presentation)
    {
        var table = PresentationModelCloneHelper.FindTable(presentation, _slideIndex, _shapeId);
        if (table is null) return;
        _oldValue = table.Flags.FirstRow;
        table.Flags.FirstRow = _newValue;
    }

    public void Revert(Presentation presentation)
    {
        var table = PresentationModelCloneHelper.FindTable(presentation, _slideIndex, _shapeId);
        if (table is null) return;
        table.Flags.FirstRow = _oldValue;
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// 1. SetTableCellTextCommand
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Replaces the <see cref="TextBody"/> of the cell at (<paramref name="row"/>, <paramref name="col"/>)
/// with <paramref name="newBody"/>. Captures the previous body for undo.
/// </summary>
public sealed class SetTableCellTextCommand : IPresentationCommand
{
    private readonly int       _slideIndex;
    private readonly uint      _shapeId;
    private readonly int       _row;
    private readonly int       _col;
    private readonly TextBody? _newBody;
    private TextBody?          _oldBody;

    public SetTableCellTextCommand(int slideIndex, uint shapeId, int row, int col, TextBody? newBody)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _row        = row;
        _col        = col;
        _newBody    = newBody is null ? null : PresentationModelCloneHelper.CloneTextBody(newBody);
    }

    public string Label => "Edit Cell Text";

    public void Apply(Presentation p)
    {
        var cell = GetCell(p);
        if (cell is null) return;
        _oldBody     = cell.TextBody is null ? null : PresentationModelCloneHelper.CloneTextBody(cell.TextBody);
        cell.TextBody = _newBody is null ? null : PresentationModelCloneHelper.CloneTextBody(_newBody);
    }

    public void Revert(Presentation p)
    {
        var cell = GetCell(p);
        if (cell is null) return;
        cell.TextBody = _oldBody is null ? null : PresentationModelCloneHelper.CloneTextBody(_oldBody);
    }

    private TableCell? GetCell(Presentation p)
    {
        var table = PresentationModelCloneHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null) return null;
        if (_row < 0 || _row >= table.Rows.Count) return null;
        var row = table.Rows[_row];
        if (_col < 0 || _col >= row.Cells.Count) return null;
        return row.Cells[_col];
    }
}

/// <summary>Changes the DrawingML text direction of one table cell.</summary>
public sealed class SetTableCellTextVerticalTypeCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _row;
    private readonly int _col;
    private readonly TextVerticalType _newType;
    private TextVerticalType _oldType;

    public SetTableCellTextVerticalTypeCommand(
        int slideIndex,
        uint shapeId,
        int row,
        int col,
        TextVerticalType newType)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _row = row;
        _col = col;
        _newType = newType;
    }

    public string Label => "Set Cell Text Direction";

    public bool HasEffect(Presentation presentation)
    {
        var cell = GetCell(presentation);
        return cell?.TextBody is { } body && body.VerticalType != _newType;
    }

    public void Apply(Presentation presentation)
    {
        var cell = GetCell(presentation);
        if (cell?.TextBody is not { } body)
            return;

        _oldType = body.VerticalType;
        body.VerticalType = _newType;
    }

    public void Revert(Presentation presentation)
    {
        var cell = GetCell(presentation);
        if (cell?.TextBody is not { } body)
            return;

        body.VerticalType = _oldType;
    }

    private TableCell? GetCell(Presentation presentation)
    {
        var table = PresentationModelCloneHelper.FindTable(presentation, _slideIndex, _shapeId);
        if (table is null || _row < 0 || _row >= table.Rows.Count)
            return null;

        var row = table.Rows[_row];
        return _col < 0 || _col >= row.Cells.Count ? null : row.Cells[_col];
    }
}

/// <summary>Sets or clears the explicit fill of one table cell.</summary>
public sealed class SetTableCellFillCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _row;
    private readonly int _col;
    private readonly ShapeFill? _newFill;
    private ShapeFill? _oldFill;

    public SetTableCellFillCommand(int slideIndex, uint shapeId, int row, int col, ShapeFill? newFill)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _row = row;
        _col = col;
        _newFill = newFill;
    }

    public string Label => _newFill is null or ShapeFill.None ? "Clear Cell Fill" : "Set Cell Fill";

    public bool HasEffect(Presentation presentation)
    {
        var cell = GetCell(presentation);
        return cell is not null && !ReferenceEquals(cell.Fill, _newFill);
    }

    public void Apply(Presentation presentation)
    {
        var cell = GetCell(presentation);
        if (cell is null) return;
        _oldFill = cell.Fill;
        cell.Fill = _newFill;
    }

    public void Revert(Presentation presentation)
    {
        var cell = GetCell(presentation);
        if (cell is not null)
            cell.Fill = _oldFill;
    }

    private TableCell? GetCell(Presentation presentation)
    {
        var table = PresentationModelCloneHelper.FindTable(presentation, _slideIndex, _shapeId);
        if (table is null || _row < 0 || _row >= table.Rows.Count)
            return null;

        var row = table.Rows[_row];
        return _col >= 0 && _col < row.Cells.Count ? row.Cells[_col] : null;
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// 2. InsertTableRowCommand
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>Sets or clears the explicit vertical alignment of one table cell.</summary>
public sealed class SetTableCellAnchorCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _row;
    private readonly int _col;
    private readonly TableCellAnchor? _newAnchor;
    private TableCellAnchor? _oldAnchor;

    public SetTableCellAnchorCommand(int slideIndex, uint shapeId, int row, int col, TableCellAnchor? newAnchor)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _row = row;
        _col = col;
        _newAnchor = newAnchor;
    }

    public string Label => _newAnchor is null ? "Clear Cell Alignment" : "Set Cell Alignment";

    public bool HasEffect(Presentation presentation)
    {
        var cell = GetCell(presentation);
        return cell is not null && cell.Anchor != _newAnchor;
    }

    public void Apply(Presentation presentation)
    {
        var cell = GetCell(presentation);
        if (cell is null) return;
        _oldAnchor = cell.Anchor;
        cell.Anchor = _newAnchor;
    }

    public void Revert(Presentation presentation)
    {
        var cell = GetCell(presentation);
        if (cell is not null)
            cell.Anchor = _oldAnchor;
    }

    private TableCell? GetCell(Presentation presentation)
    {
        var table = PresentationModelCloneHelper.FindTable(presentation, _slideIndex, _shapeId);
        if (table is null || _row < 0 || _row >= table.Rows.Count)
            return null;

        var row = table.Rows[_row];
        return _col >= 0 && _col < row.Cells.Count ? row.Cells[_col] : null;
    }
}

/// <summary>Sets or clears one explicit cell inset side, in points.</summary>
public sealed class SetTableCellInsetCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _row;
    private readonly int _col;
    private readonly TableCellInsetSide _side;
    private readonly double? _newInsetPt;
    private double? _oldLeftPt;
    private double? _oldRightPt;
    private double? _oldTopPt;
    private double? _oldBottomPt;

    public SetTableCellInsetCommand(
        int slideIndex,
        uint shapeId,
        int row,
        int col,
        TableCellInsetSide side,
        double? newInsetPt)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _row = row;
        _col = col;
        _side = side;
        _newInsetPt = newInsetPt;
    }

    public string Label => _newInsetPt is null ? "Clear Cell Inset" : "Set Cell Inset";

    public bool HasEffect(Presentation presentation)
    {
        var cell = GetCell(presentation);
        return cell is not null && (_side == TableCellInsetSide.All
            ? cell.InsetLeftPt != _newInsetPt || cell.InsetRightPt != _newInsetPt ||
              cell.InsetTopPt != _newInsetPt || cell.InsetBottomPt != _newInsetPt
            : GetSide(cell, _side) != _newInsetPt);
    }

    public void Apply(Presentation presentation)
    {
        var cell = GetCell(presentation);
        if (cell is null) return;
        _oldLeftPt = cell.InsetLeftPt;
        _oldRightPt = cell.InsetRightPt;
        _oldTopPt = cell.InsetTopPt;
        _oldBottomPt = cell.InsetBottomPt;
        SetSide(cell, _side, _newInsetPt);
    }

    public void Revert(Presentation presentation)
    {
        var cell = GetCell(presentation);
        if (cell is not null)
        {
            if (_side == TableCellInsetSide.All)
            {
                cell.InsetLeftPt = _oldLeftPt;
                cell.InsetRightPt = _oldRightPt;
                cell.InsetTopPt = _oldTopPt;
                cell.InsetBottomPt = _oldBottomPt;
            }
            else
                SetSide(cell, _side, GetOldSide(_side));
        }
    }

    private TableCell? GetCell(Presentation presentation)
    {
        var table = PresentationModelCloneHelper.FindTable(presentation, _slideIndex, _shapeId);
        if (table is null || _row < 0 || _row >= table.Rows.Count)
            return null;

        var row = table.Rows[_row];
        return _col >= 0 && _col < row.Cells.Count ? row.Cells[_col] : null;
    }

    private static double? GetSide(TableCell cell, TableCellInsetSide side) => side switch
    {
        TableCellInsetSide.Left => cell.InsetLeftPt,
        TableCellInsetSide.Right => cell.InsetRightPt,
        TableCellInsetSide.Top => cell.InsetTopPt,
        TableCellInsetSide.Bottom => cell.InsetBottomPt,
        _ => null,
    };

    private double? GetOldSide(TableCellInsetSide side) => side switch
    {
        TableCellInsetSide.Left => _oldLeftPt,
        TableCellInsetSide.Right => _oldRightPt,
        TableCellInsetSide.Top => _oldTopPt,
        TableCellInsetSide.Bottom => _oldBottomPt,
        _ => null,
    };

    private static void SetSide(TableCell cell, TableCellInsetSide side, double? value)
    {
        switch (side)
        {
            case TableCellInsetSide.All:
                cell.InsetLeftPt = value;
                cell.InsetRightPt = value;
                cell.InsetTopPt = value;
                cell.InsetBottomPt = value;
                break;
            case TableCellInsetSide.Left:
                cell.InsetLeftPt = value;
                break;
            case TableCellInsetSide.Right:
                cell.InsetRightPt = value;
                break;
            case TableCellInsetSide.Top:
                cell.InsetTopPt = value;
                break;
            case TableCellInsetSide.Bottom:
                cell.InsetBottomPt = value;
                break;
        }
    }
}

/// <summary>Sets or clears one explicit border side of a table cell.</summary>
public sealed class SetTableCellBorderCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _row;
    private readonly int _col;
    private readonly TableCellBorderSide _side;
    private readonly ShapeOutline? _newOutline;
    private TableCellBorders? _oldBorders;

    public SetTableCellBorderCommand(
        int slideIndex,
        uint shapeId,
        int row,
        int col,
        TableCellBorderSide side,
        ShapeOutline? newOutline)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _row = row;
        _col = col;
        _side = side;
        _newOutline = newOutline;
    }

    public string Label => _newOutline is null ? "Clear Cell Border" : "Set Cell Border";

    public bool HasEffect(Presentation presentation)
    {
        var cell = GetCell(presentation);
        return cell is not null && GetSide(cell.Borders, _side) != _newOutline;
    }

    public void Apply(Presentation presentation)
    {
        var cell = GetCell(presentation);
        if (cell is null) return;

        _oldBorders = cell.Borders;
        var borders = CloneBorders(cell.Borders);
        SetSide(borders, _side, _newOutline);
        cell.Borders = HasAnySide(borders) ? borders : null;
    }

    public void Revert(Presentation presentation)
    {
        var cell = GetCell(presentation);
        if (cell is not null)
            cell.Borders = _oldBorders;
    }

    private TableCell? GetCell(Presentation presentation)
    {
        var table = PresentationModelCloneHelper.FindTable(presentation, _slideIndex, _shapeId);
        if (table is null || _row < 0 || _row >= table.Rows.Count)
            return null;

        var row = table.Rows[_row];
        return _col >= 0 && _col < row.Cells.Count ? row.Cells[_col] : null;
    }

    private static TableCellBorders CloneBorders(TableCellBorders? source) => new()
    {
        Left = source?.Left,
        Right = source?.Right,
        Top = source?.Top,
        Bottom = source?.Bottom,
    };

    private static bool HasAnySide(TableCellBorders borders) =>
        borders.Left is not null || borders.Right is not null ||
        borders.Top is not null || borders.Bottom is not null;

    private static ShapeOutline? GetSide(TableCellBorders? borders, TableCellBorderSide side) => side switch
    {
        TableCellBorderSide.Left => borders?.Left,
        TableCellBorderSide.Right => borders?.Right,
        TableCellBorderSide.Top => borders?.Top,
        TableCellBorderSide.Bottom => borders?.Bottom,
        _ => null,
    };

    private static void SetSide(TableCellBorders borders, TableCellBorderSide side, ShapeOutline? outline)
    {
        switch (side)
        {
            case TableCellBorderSide.Left:
                borders.Left = outline;
                break;
            case TableCellBorderSide.Right:
                borders.Right = outline;
                break;
            case TableCellBorderSide.Top:
                borders.Top = outline;
                break;
            case TableCellBorderSide.Bottom:
                borders.Bottom = outline;
                break;
        }
    }
}

/// <summary>
/// Inserts a new blank row at <paramref name="atRow"/> (rows at and after shift down).
/// The new row gets the same height as the adjacent row (or a default if the table is empty).
/// Grid integrity: one cell per column, all GridSpan=1 RowSpan=1.
/// Captures a full table snapshot for undo.
/// </summary>
/// <summary>Sets the height of one table row, or restores automatic row height with zero.</summary>
public sealed class SetTableRowHeightCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _rowIndex;
    private readonly long _newHeightEmu;
    private long _oldHeightEmu;

    public SetTableRowHeightCommand(
        int slideIndex,
        uint shapeId,
        int rowIndex,
        long newHeightEmu)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _rowIndex = rowIndex;
        _newHeightEmu = newHeightEmu;
    }

    public string Label => _newHeightEmu <= 0 ? "Set Automatic Row Height" : "Set Row Height";

    public bool HasEffect(Presentation presentation)
    {
        var row = GetRow(presentation);
        return row is not null && row.HeightEmu != _newHeightEmu;
    }

    public void Apply(Presentation presentation)
    {
        var row = GetRow(presentation);
        if (row is null) return;
        _oldHeightEmu = row.HeightEmu;
        row.HeightEmu = Math.Max(0, _newHeightEmu);
    }

    public void Revert(Presentation presentation)
    {
        var row = GetRow(presentation);
        if (row is not null)
            row.HeightEmu = _oldHeightEmu;
    }

    private TableRow? GetRow(Presentation presentation)
    {
        var table = PresentationModelCloneHelper.FindTable(presentation, _slideIndex, _shapeId);
        return table is not null && _rowIndex >= 0 && _rowIndex < table.Rows.Count
            ? table.Rows[_rowIndex]
            : null;
    }
}

/// <summary>Sets the width of one table grid column, preserving the prior width for undo.</summary>
public sealed class SetTableColumnWidthCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly int _columnIndex;
    private readonly long _newWidthEmu;
    private long _oldWidthEmu;

    public SetTableColumnWidthCommand(
        int slideIndex,
        uint shapeId,
        int columnIndex,
        long newWidthEmu)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _columnIndex = columnIndex;
        _newWidthEmu = Math.Max(1, newWidthEmu);
    }

    public string Label => "Set Column Width";

    public bool HasEffect(Presentation presentation)
    {
        var table = PresentationModelCloneHelper.FindTable(presentation, _slideIndex, _shapeId);
        return table is not null &&
               _columnIndex >= 0 &&
               _columnIndex < table.ColumnWidthsEmu.Count &&
               table.ColumnWidthsEmu[_columnIndex] != _newWidthEmu;
    }

    public void Apply(Presentation presentation)
    {
        var table = PresentationModelCloneHelper.FindTable(presentation, _slideIndex, _shapeId);
        if (table is null || _columnIndex < 0 || _columnIndex >= table.ColumnWidthsEmu.Count)
            return;

        _oldWidthEmu = table.ColumnWidthsEmu[_columnIndex];
        table.ColumnWidthsEmu[_columnIndex] = _newWidthEmu;
    }

    public void Revert(Presentation presentation)
    {
        var table = PresentationModelCloneHelper.FindTable(presentation, _slideIndex, _shapeId);
        if (table is null || _columnIndex < 0 || _columnIndex >= table.ColumnWidthsEmu.Count)
            return;

        table.ColumnWidthsEmu[_columnIndex] = _oldWidthEmu;
    }
}

public sealed class InsertTableRowCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _atRow;
    private TableShape?   _snapshot;

    public InsertTableRowCommand(int slideIndex, uint shapeId, int atRow)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _atRow      = atRow;
    }

    public string Label => "Insert Row";

    public void Apply(Presentation p)
    {
        var table = PresentationModelCloneHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null) return;

        // Snapshot before mutation.
        _snapshot = PresentationModelCloneHelper.CloneTable(table);

        int cols = table.ColumnWidthsEmu.Count;
        // Default height: match previous row if available, else next row, else 457200 EMU (~0.5 inch).
        int idx = Math.Clamp(_atRow, 0, table.Rows.Count);
        long height = idx > 0
            ? table.Rows[idx - 1].HeightEmu
            : (table.Rows.Count > 0 ? table.Rows[0].HeightEmu : 457200L);

        // W5: In FreeP, row.Cells[c] is the cell for grid column c.
        // For each grid column c, check if the insertion row falls STRICTLY INSIDE a vertical
        // span anchored in a row above (anchorRow < idx <= anchorRow + RowSpan - 1).
        // If so, insert a VMerge continuation for that column and widen the anchor's RowSpan.
        // Otherwise insert an independent blank cell.
        var newRow = new TableRow { HeightEmu = height };
        for (int c = 0; c < cols; c++)
        {
            bool insideVSpan = false;
            // Walk upward from idx-1 to find the nearest non-VMerge cell in column c.
            for (int r = idx - 1; r >= 0; r--)
            {
                var candidateRow = table.Rows[r];
                if (c >= candidateRow.Cells.Count) break;
                var candidateCell = candidateRow.Cells[c];
                if (candidateCell.VMerge)
                    continue; // this row is itself a continuation — keep scanning upward
                // Found the anchor (or independent cell) for column c.
                if (candidateCell.RowSpan > 1 && r + candidateCell.RowSpan - 1 >= idx)
                {
                    // Insertion is strictly inside this anchor's vertical span — widen it.
                    candidateCell.RowSpan++;
                    insideVSpan = true;
                }
                break;
            }
            var newCell = new TableCell();
            if (insideVSpan)
                newCell.VMerge = true;
            newRow.Cells.Add(newCell);
        }

        table.Rows.Insert(idx, newRow);
    }

    public void Revert(Presentation p)
    {
        var table = PresentationModelCloneHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null || _snapshot is null) return;
        PresentationModelCloneHelper.RestoreTableState(table, _snapshot);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// 3. DeleteTableRowCommand
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Deletes the row at <paramref name="atRow"/>. No-op if that would leave the table with zero rows.
/// Captures a full table snapshot for undo.
/// </summary>
public sealed class DeleteTableRowCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _atRow;
    private TableShape?   _snapshot;

    public DeleteTableRowCommand(int slideIndex, uint shapeId, int atRow)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _atRow      = atRow;
    }

    public string Label => "Delete Row";

    public void Apply(Presentation p)
    {
        var table = PresentationModelCloneHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null) return;
        if (table.Rows.Count <= 1) return; // keep at least one row
        if (_atRow < 0 || _atRow >= table.Rows.Count) return;

        _snapshot = PresentationModelCloneHelper.CloneTable(table);

        int gridCols = table.ColumnWidthsEmu.Count;
        var deletedRow = table.Rows[_atRow];

        // W3: In FreeP, row.Cells[c] is the cell for grid column c.
        // For each grid column, examine the deleted row's cell:
        //   • Anchor (RowSpan>1, not VMerge): promote the next row's cell in the same column to
        //     the new anchor (clear VMerge, adopt RowSpan-1 and content).
        //   • VMerge continuation: find the anchor above and decrement its RowSpan.
        //   • Independent cell: nothing to adjust.
        for (int c = 0; c < gridCols; c++)
        {
            if (c >= deletedRow.Cells.Count) continue;
            var cell = deletedRow.Cells[c];

            if (!cell.VMerge && cell.RowSpan > 1)
            {
                // Anchor being deleted: push anchor role down to the next row's same column.
                if (_atRow + 1 < table.Rows.Count)
                {
                    var nextRow = table.Rows[_atRow + 1];
                    if (c < nextRow.Cells.Count)
                    {
                        var nextCell = nextRow.Cells[c];
                        nextCell.VMerge   = false;
                        nextCell.RowSpan  = cell.RowSpan - 1;
                        nextCell.GridSpan = cell.GridSpan;
                        if (nextCell.TextBody is null && cell.TextBody is not null)
                            nextCell.TextBody = PresentationModelCloneHelper.CloneTextBody(cell.TextBody);

                        // X1 (2D merge fix): if the promoted anchor has a horizontal span
                        // (GridSpan > 1), the cells at columns c+1..c+GridSpan-1 in the next
                        // row are still VMerge=true from the original 2D merge.  They must be
                        // relabeled to HMerge=true so the promoted row's horizontal span is
                        // consistent and PowerPoint does not see an orphan vMerge.
                        // We only touch cells that are VMerge at those exact grid positions
                        // (cells belonging to a different independent anchor would not have
                        // VMerge set here, so the guard is sufficient).
                        int promotedGridSpan = nextCell.GridSpan;
                        for (int k = 1; k < promotedGridSpan; k++)
                        {
                            int kc = c + k;
                            if (kc < nextRow.Cells.Count && nextRow.Cells[kc].VMerge)
                            {
                                nextRow.Cells[kc].VMerge = false;
                                nextRow.Cells[kc].HMerge = true;
                            }
                        }
                    }
                }
            }
            else if (cell.VMerge)
            {
                // VMerge continuation: find the anchor above and decrement its RowSpan.
                for (int r = _atRow - 1; r >= 0; r--)
                {
                    var candidateRow = table.Rows[r];
                    if (c >= candidateRow.Cells.Count) break;
                    var anchorCandidate = candidateRow.Cells[c];
                    if (!anchorCandidate.VMerge)
                    {
                        if (anchorCandidate.RowSpan > 1)
                            anchorCandidate.RowSpan--;
                        break;
                    }
                }
            }
        }

        table.Rows.RemoveAt(_atRow);
    }

    public void Revert(Presentation p)
    {
        var table = PresentationModelCloneHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null || _snapshot is null) return;
        PresentationModelCloneHelper.RestoreTableState(table, _snapshot);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// 4. InsertTableColumnCommand
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Inserts a new blank column at <paramref name="atCol"/>.
/// The new column gets the same width as the adjacent column (or a default).
/// Captures a full table snapshot for undo.
/// </summary>
public sealed class InsertTableColumnCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _atCol;
    private TableShape?   _snapshot;

    public InsertTableColumnCommand(int slideIndex, uint shapeId, int atCol)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _atCol      = atCol;
    }

    public string Label => "Insert Column";

    public void Apply(Presentation p)
    {
        var table = PresentationModelCloneHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null) return;

        _snapshot = PresentationModelCloneHelper.CloneTable(table);

        int idx = Math.Clamp(_atCol, 0, table.ColumnWidthsEmu.Count);
        // Default width: match adjacent column or one inch.
        long width = idx > 0
            ? table.ColumnWidthsEmu[idx - 1]
            : (table.ColumnWidthsEmu.Count > 0 ? table.ColumnWidthsEmu[0] : DrawingMlCoordinateUnits.EmuPerInch);

        table.ColumnWidthsEmu.Insert(idx, width);

        // W4: In FreeP, row.Cells[gridCol] is always the cell for that grid column (one cell
        // per grid column, HMerge cells stay in the list).
        //
        // If the cell at idx is an HMerge continuation, the insertion falls STRICTLY INSIDE
        // the anchor's horizontal span → find the anchor, widen its GridSpan, and insert a new
        // HMerge continuation immediately after the anchor slot.
        //
        // Otherwise (the cell at idx is an anchor or independent cell, i.e. not HMerge), the
        // insertion is at a span boundary → insert an independent new cell at idx.
        foreach (var row in table.Rows)
        {
            if (idx >= row.Cells.Count)
            {
                // Inserting at or beyond the end of the row — append an independent cell.
                row.Cells.Add(new TableCell());
                continue;
            }

            if (row.Cells[idx].HMerge)
            {
                // Inside an HMerge span.  Walk left to find the anchor.
                int ai = idx - 1;
                while (ai >= 0 && row.Cells[ai].HMerge)
                    ai--;
                // ai is now the anchor's cell-list index.
                row.Cells[ai].GridSpan = Math.Max(1, row.Cells[ai].GridSpan) + 1;
                // Insert a new HMerge continuation immediately after the anchor slot.
                row.Cells.Insert(ai + 1, new TableCell { HMerge = true });
            }
            else
            {
                // Span boundary — insert an independent cell at idx.
                row.Cells.Insert(idx, new TableCell());
            }
        }
    }

    public void Revert(Presentation p)
    {
        var table = PresentationModelCloneHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null || _snapshot is null) return;
        PresentationModelCloneHelper.RestoreTableState(table, _snapshot);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// 5. DeleteTableColumnCommand
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Deletes the column at <paramref name="atCol"/>. No-op if that would leave the table with zero columns.
/// Captures a full table snapshot for undo.
/// </summary>
public sealed class DeleteTableColumnCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _atCol;
    private TableShape?   _snapshot;

    public DeleteTableColumnCommand(int slideIndex, uint shapeId, int atCol)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _atCol      = atCol;
    }

    public string Label => "Delete Column";

    public void Apply(Presentation p)
    {
        var table = PresentationModelCloneHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null) return;
        if (table.ColumnWidthsEmu.Count <= 1) return; // keep at least one column
        if (_atCol < 0 || _atCol >= table.ColumnWidthsEmu.Count) return;

        _snapshot = PresentationModelCloneHelper.CloneTable(table);
        table.ColumnWidthsEmu.RemoveAt(_atCol);

        // W2: In FreeP, row.Cells[_atCol] is always the cell for grid column _atCol.
        //
        // Case 1: The cell at _atCol is an HMerge continuation → find its anchor (scan left),
        //         decrement the anchor's GridSpan, then remove the continuation.
        // Case 2: The cell at _atCol is an anchor with GridSpan>1 (it spans multiple columns) →
        //         promote the next cell (HMerge continuation) to become the new anchor
        //         (clear HMerge, adopt RowSpan and content), then remove the anchor slot.
        // Case 3: Independent cell (GridSpan==1, not HMerge) → just remove it.
        foreach (var row in table.Rows)
        {
            if (_atCol >= row.Cells.Count) continue;
            var cell = row.Cells[_atCol];

            if (cell.HMerge)
            {
                // HMerge continuation: decrement the owning anchor's GridSpan.
                for (int ai = _atCol - 1; ai >= 0; ai--)
                {
                    if (!row.Cells[ai].HMerge)
                    {
                        if (row.Cells[ai].GridSpan > 1)
                            row.Cells[ai].GridSpan--;
                        break;
                    }
                }
                row.Cells.RemoveAt(_atCol);
            }
            else if (cell.GridSpan > 1)
            {
                // Anchor being deleted — promote its first HMerge continuation to become
                // the new anchor so the remaining span is preserved.
                var nextCell = row.Cells[_atCol + 1]; // must exist since GridSpan > 1
                nextCell.HMerge   = false;
                nextCell.GridSpan = cell.GridSpan - 1;
                nextCell.RowSpan  = cell.RowSpan;
                if (nextCell.TextBody is null && cell.TextBody is not null)
                    nextCell.TextBody = PresentationModelCloneHelper.CloneTextBody(cell.TextBody);
                row.Cells.RemoveAt(_atCol); // remove the old anchor
            }
            else
            {
                // Independent single-column cell.
                row.Cells.RemoveAt(_atCol);
            }
        }
    }

    public void Revert(Presentation p)
    {
        var table = PresentationModelCloneHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null || _snapshot is null) return;
        PresentationModelCloneHelper.RestoreTableState(table, _snapshot);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// 6. MergeTableCellsCommand
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Merges the rectangular region [r1,c1]..[r2,c2] (inclusive, order-independent).
/// The top-left cell becomes the anchor (GridSpan = colCount, RowSpan = rowCount).
/// All other cells in the region are marked HMerge/VMerge.
/// Text from all merged cells is concatenated into the anchor (newlines between non-empty cells).
/// Captures a full table snapshot for undo.
/// </summary>
public sealed class MergeTableCellsCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _r1, _c1, _r2, _c2;
    private TableShape?   _snapshot;

    /// <param name="r1">Row of first corner.</param>
    /// <param name="c1">Column of first corner.</param>
    /// <param name="r2">Row of second corner.</param>
    /// <param name="c2">Column of second corner.</param>
    public MergeTableCellsCommand(int slideIndex, uint shapeId, int r1, int c1, int r2, int c2)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        // Normalise so r1 <= r2, c1 <= c2.
        _r1 = Math.Min(r1, r2);
        _c1 = Math.Min(c1, c2);
        _r2 = Math.Max(r1, r2);
        _c2 = Math.Max(c1, c2);
    }

    public string Label => "Merge Cells";

    public bool HasEffect(Presentation p)
    {
        var table = PresentationModelCloneHelper.FindTable(p, _slideIndex, _shapeId);
        return table is not null
            && _r1 >= 0
            && _c1 >= 0
            && _r2 < table.Rows.Count
            && _c2 < table.ColumnWidthsEmu.Count
            && (_r1 != _r2 || _c1 != _c2);
    }

    public void Apply(Presentation p)
    {
        var table = PresentationModelCloneHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null) return;
        if (_r2 >= table.Rows.Count || _c2 >= table.ColumnWidthsEmu.Count) return;
        if (_r1 == _r2 && _c1 == _c2) return; // nothing to merge

        _snapshot = PresentationModelCloneHelper.CloneTable(table);

        int gridSpan = _c2 - _c1 + 1;
        int rowSpan  = _r2 - _r1 + 1;

        // Collect text from all cells in the region to put in the anchor.
        var texts = new List<string>();
        for (int r = _r1; r <= _r2; r++)
        {
            for (int c = _c1; c <= _c2; c++)
            {
                var cell = table.Rows[r].Cells[c];
                var cellText = GetPlainText(cell.TextBody);
                if (!string.IsNullOrWhiteSpace(cellText))
                    texts.Add(cellText);
            }
        }

        // Set all cells to HMerge/VMerge first.
        for (int r = _r1; r <= _r2; r++)
        {
            for (int c = _c1; c <= _c2; c++)
            {
                var cell = table.Rows[r].Cells[c];
                if (r == _r1 && c == _c1)
                {
                    // Anchor cell.
                    cell.GridSpan = gridSpan;
                    cell.RowSpan  = rowSpan;
                    cell.HMerge   = false;
                    cell.VMerge   = false;
                    // Put merged text into anchor.
                    if (texts.Count > 0)
                        cell.TextBody = MakeTextBody(string.Join("\n", texts));
                }
                else
                {
                    cell.GridSpan = 1;
                    cell.RowSpan  = 1;
                    cell.HMerge   = (r == _r1); // same row as anchor → HMerge
                    cell.VMerge   = (r > _r1);  // rows below anchor → VMerge
                    cell.TextBody = null;
                }
            }
        }
    }

    public void Revert(Presentation p)
    {
        var table = PresentationModelCloneHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null || _snapshot is null) return;
        PresentationModelCloneHelper.RestoreTableState(table, _snapshot);
    }

    private static string GetPlainText(TextBody? body)
    {
        if (body is null) return string.Empty;
        return string.Join("\n", body.Paragraphs.SelectMany(pa => pa.Runs).Select(r => r.Text));
    }

    private static TextBody MakeTextBody(string text)
    {
        var body = new TextBody { Wrap = true };
        foreach (var line in text.Split('\n'))
        {
            var para = new Paragraph();
            para.Runs.Add(new Run { Text = line });
            body.Paragraphs.Add(para);
        }
        return body;
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// 7. SplitTableCellCommand
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Splits the merged cell at (<paramref name="row"/>, <paramref name="col"/>) back into individual cells.
/// If the target cell is not an anchor (GridSpan=1, RowSpan=1) the command is a no-op.
/// The anchor's TextBody is kept in the anchor; all newly-split cells are blank.
/// Captures a full table snapshot for undo.
/// </summary>
public sealed class SplitTableCellCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _shapeId;
    private readonly int  _row;
    private readonly int  _col;
    private TableShape?   _snapshot;

    public SplitTableCellCommand(int slideIndex, uint shapeId, int row, int col)
    {
        _slideIndex = slideIndex;
        _shapeId    = shapeId;
        _row        = row;
        _col        = col;
    }

    public string Label => "Split Cell";

    /// <summary>No effect unless the target cell exists and is actually merged.</summary>
    public bool HasEffect(Presentation p)
    {
        var table = PresentationModelCloneHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null || _row < 0 || _row >= table.Rows.Count) return false;
        var anchor = table.Rows[_row].Cells.ElementAtOrDefault(_col);
        return anchor is not null && (anchor.GridSpan > 1 || anchor.RowSpan > 1);
    }

    public void Apply(Presentation p)
    {
        var table = PresentationModelCloneHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null) return;
        if (_row < 0 || _row >= table.Rows.Count) return;
        var anchor = table.Rows[_row].Cells.ElementAtOrDefault(_col);
        if (anchor is null) return;

        bool isMerged = anchor.GridSpan > 1 || anchor.RowSpan > 1;
        if (!isMerged) return;

        _snapshot = PresentationModelCloneHelper.CloneTable(table);

        int gridSpan = anchor.GridSpan;
        int rowSpan  = anchor.RowSpan;

        // Clear the merge on the anchor.
        anchor.GridSpan = 1;
        anchor.RowSpan  = 1;

        // Restore all covered cells to blank/unmerged.
        for (int r = _row; r < _row + rowSpan && r < table.Rows.Count; r++)
        {
            for (int c = _col; c < _col + gridSpan && c < table.ColumnWidthsEmu.Count; c++)
            {
                if (r == _row && c == _col) continue; // anchor already fixed
                var cell = table.Rows[r].Cells.ElementAtOrDefault(c);
                if (cell is null) continue;
                cell.GridSpan = 1;
                cell.RowSpan  = 1;
                cell.HMerge   = false;
                cell.VMerge   = false;
                // Leave TextBody null (blank cell after split).
            }
        }
    }

    public void Revert(Presentation p)
    {
        var table = PresentationModelCloneHelper.FindTable(p, _slideIndex, _shapeId);
        if (table is null || _snapshot is null) return;
        PresentationModelCloneHelper.RestoreTableState(table, _snapshot);
    }
}
