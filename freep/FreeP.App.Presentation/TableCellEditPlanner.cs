using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum TableCellEditStartStatus
{
    Ready,
    MissingSlide,
    ShapeNotFound,
    NotTable,
    CellOutOfRange,
    MissingCellBounds,
}

public sealed record TableCellEditState(
    uint? ShapeId,
    int? Row,
    int? Col,
    bool HasSelectedTable,
    bool HasActiveCell,
    bool CanEditText,
    bool CanFormatText,
    bool CanInsertRow,
    bool CanInsertColumn,
    bool CanDeleteRow,
    bool CanDeleteColumn,
    bool CanMergeWithRight,
    bool CanMergeWithBelow,
    bool CanSplitCell)
{
    public static readonly TableCellEditState None = new(
        null,
        null,
        null,
        HasSelectedTable: false,
        HasActiveCell: false,
        CanEditText: false,
        CanFormatText: false,
        CanInsertRow: false,
        CanInsertColumn: false,
        CanDeleteRow: false,
        CanDeleteColumn: false,
        CanMergeWithRight: false,
        CanMergeWithBelow: false,
        CanSplitCell: false);
}

public sealed record TableCellEditStartPlan(
    TableCellEditStartStatus Status,
    uint ShapeId,
    int Row,
    int Col,
    TableCell? Cell,
    CellRectDip? CellRect,
    InCanvasEditorPlacement? Placement,
    TextBody? OriginalBody,
    InCanvasTableCellTextEditPlanner? EditPlanner)
{
    public bool IsReady => Status == TableCellEditStartStatus.Ready;
}

public static class TableCellEditPlanner
{
    public static TableCellEditState PlanSelectedCell(
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell)
    {
        ArgumentNullException.ThrowIfNull(selectedShapeIds);

        if (slide is null || selectedShapeIds.Count == 0)
            return TableCellEditState.None;

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == selectedShapeIds[0]);
        if (shape?.Kind != SlideShapeKind.Table || shape.Table is null)
            return TableCellEditState.None;

        if (activeCell is not { } requested)
        {
            return TableCellEditState.None with
            {
                ShapeId = shape.Id,
                HasSelectedTable = true,
                CanInsertRow = shape.Table.Rows.Count > 0,
                CanInsertColumn = shape.Table.ColumnWidthsEmu.Count > 0,
                CanDeleteRow = shape.Table.Rows.Count > 1,
                CanDeleteColumn = shape.Table.ColumnWidthsEmu.Count > 1,
            };
        }

        var normalized = NormalizeCell(shape.Table, requested.Row, requested.Col);
        if (normalized is null)
        {
            return TableCellEditState.None with
            {
                ShapeId = shape.Id,
                HasSelectedTable = true,
            };
        }

        var cell = normalized.Value.Cell;
        int row = normalized.Value.Row;
        int col = normalized.Value.Col;
        int colSpan = Math.Max(1, cell.GridSpan);
        int rowSpan = Math.Max(1, cell.RowSpan);

        return new TableCellEditState(
            shape.Id,
            row,
            col,
            HasSelectedTable: true,
            HasActiveCell: true,
            CanEditText: true,
            CanFormatText: true,
            CanInsertRow: true,
            CanInsertColumn: shape.Table.ColumnWidthsEmu.Count > 0,
            CanDeleteRow: shape.Table.Rows.Count > 1,
            CanDeleteColumn: shape.Table.ColumnWidthsEmu.Count > 1,
            CanMergeWithRight: col + colSpan < shape.Table.ColumnWidthsEmu.Count,
            CanMergeWithBelow: row + rowSpan < shape.Table.Rows.Count,
            CanSplitCell: colSpan > 1 || rowSpan > 1);
    }

    public static TableCellEditStartPlan BeginEdit(
        int slideIndex,
        Slide? slide,
        uint shapeId,
        int row,
        int col,
        SlideTransformCore transform,
        double minimumWidth,
        double minimumHeight)
    {
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(minimumHeight);

        if (slide is null)
            return NotReady(TableCellEditStartStatus.MissingSlide, shapeId, row, col);

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == shapeId);
        if (shape is null)
            return NotReady(TableCellEditStartStatus.ShapeNotFound, shapeId, row, col);
        if (shape.Kind != SlideShapeKind.Table || shape.Table is null)
            return NotReady(TableCellEditStartStatus.NotTable, shapeId, row, col);

        var normalized = NormalizeCell(shape.Table, row, col);
        if (normalized is null)
            return NotReady(TableCellEditStartStatus.CellOutOfRange, shapeId, row, col);

        var cellRect = TableCellHitTester.GetCellRect(shape, normalized.Value.Row, normalized.Value.Col);
        if (cellRect is null)
            return NotReady(TableCellEditStartStatus.MissingCellBounds, shapeId, normalized.Value.Row, normalized.Value.Col);

        var screenRect = SlideCanvasGeometryPlanner.DipBoundsToScreen(cellRect.Value, transform);
        var placement = SlideCanvasGeometryPlanner.PlanEditorPlacement(
            screenRect,
            minimumWidth,
            minimumHeight);
        var originalBody = TextBodyModelCloner.CloneTextBody(normalized.Value.Cell.TextBody);

        return new TableCellEditStartPlan(
            TableCellEditStartStatus.Ready,
            shapeId,
            normalized.Value.Row,
            normalized.Value.Col,
            normalized.Value.Cell,
            cellRect.Value,
            placement,
            originalBody,
            InCanvasTableCellTextEditPlanner.BeginRichText(
                slideIndex,
                shapeId,
                normalized.Value.Row,
                normalized.Value.Col,
                normalized.Value.Cell.TextBody));
    }

    public static InCanvasTextEditDecision CommitRichText(
        InCanvasTableCellTextEditPlanner? editPlanner,
        TextBody editedBody)
    {
        ArgumentNullException.ThrowIfNull(editedBody);

        return editPlanner?.CommitRichText(editedBody)
            ?? new InCanvasTextEditDecision(InCanvasTextEditOutcome.Unchanged, null);
    }

    public static InCanvasTextEditDecision Cancel(InCanvasTableCellTextEditPlanner? editPlanner) =>
        editPlanner?.Cancel()
        ?? new InCanvasTextEditDecision(InCanvasTextEditOutcome.Canceled, null);

    private static TableCellEditStartPlan NotReady(
        TableCellEditStartStatus status,
        uint shapeId,
        int row,
        int col) =>
        new(status, shapeId, row, col, null, null, null, null, null);

    private static (int Row, int Col, TableCell Cell)? NormalizeCell(
        TableShape table,
        int row,
        int col)
    {
        if (row < 0 || row >= table.Rows.Count)
            return null;
        if (col < 0 || col >= table.ColumnWidthsEmu.Count)
            return null;
        if (col >= table.Rows[row].Cells.Count)
            return null;

        var requestedCell = table.Rows[row].Cells[col];
        if (!requestedCell.HMerge && !requestedCell.VMerge)
            return (row, col, requestedCell);

        for (int r = 0; r < table.Rows.Count; r++)
        {
            var tableRow = table.Rows[r];
            for (int c = 0; c < tableRow.Cells.Count; c++)
            {
                var candidate = tableRow.Cells[c];
                if (candidate.HMerge || candidate.VMerge)
                    continue;

                int colSpan = Math.Max(1, candidate.GridSpan);
                int rowSpan = Math.Max(1, candidate.RowSpan);
                if (r <= row && row < r + rowSpan && c <= col && col < c + colSpan)
                    return (r, c, candidate);
            }
        }

        return null;
    }
}
