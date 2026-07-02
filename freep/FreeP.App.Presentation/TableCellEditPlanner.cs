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

public enum TableCellTextFormatKind
{
    Bold,
    Italic,
    Underline,
}

public enum TableCellTextFormatStatus
{
    Ready,
    MissingSlide,
    ShapeNotFound,
    NotTable,
    MissingActiveCell,
    CellOutOfRange,
    MissingTextBody,
    NoTextRuns,
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

public sealed record TableCellTextFormatPlan(
    TableCellTextFormatStatus Status,
    uint? ShapeId,
    int? Row,
    int? Col,
    TableCellTextFormatKind Kind,
    bool? TargetValue,
    IPresentationCommand? Command)
{
    public bool IsReady => Status == TableCellTextFormatStatus.Ready && Command is not null;
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

    public static TableCellTextFormatPlan PlanTextFormat(
        int slideIndex,
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        TableCellTextFormatKind kind)
    {
        ArgumentNullException.ThrowIfNull(selectedShapeIds);

        if (slide is null)
            return DisabledFormat(TableCellTextFormatStatus.MissingSlide, kind);
        if (selectedShapeIds.Count == 0)
            return DisabledFormat(TableCellTextFormatStatus.ShapeNotFound, kind);

        var shape = slide.Shapes.FirstOrDefault(s => s.Id == selectedShapeIds[0]);
        if (shape is null)
            return DisabledFormat(TableCellTextFormatStatus.ShapeNotFound, kind);
        if (shape.Kind != SlideShapeKind.Table || shape.Table is null)
            return DisabledFormat(TableCellTextFormatStatus.NotTable, kind, shape.Id);
        if (activeCell is not { } requested)
            return DisabledFormat(TableCellTextFormatStatus.MissingActiveCell, kind, shape.Id);

        var normalized = NormalizeCell(shape.Table, requested.Row, requested.Col);
        if (normalized is null)
            return DisabledFormat(TableCellTextFormatStatus.CellOutOfRange, kind, shape.Id);

        var (row, col, cell) = normalized.Value;
        if (cell.TextBody is null)
            return DisabledFormat(TableCellTextFormatStatus.MissingTextBody, kind, shape.Id, row, col);

        var runs = cell.TextBody.Paragraphs.SelectMany(p => p.Runs).ToList();
        if (runs.Count == 0)
            return DisabledFormat(TableCellTextFormatStatus.NoTextRuns, kind, shape.Id, row, col);

        bool targetValue = !runs.All(run => GetRunFormat(run, kind));
        var editedBody = TextBodyModelCloner.CloneTextBody(cell.TextBody)!;
        foreach (var run in editedBody.Paragraphs.SelectMany(p => p.Runs))
            SetRunFormat(run, kind, targetValue);

        return new TableCellTextFormatPlan(
            TableCellTextFormatStatus.Ready,
            shape.Id,
            row,
            col,
            kind,
            targetValue,
            new SetTableCellTextCommand(slideIndex, shape.Id, row, col, editedBody));
    }

    private static TableCellEditStartPlan NotReady(
        TableCellEditStartStatus status,
        uint shapeId,
        int row,
        int col) =>
        new(status, shapeId, row, col, null, null, null, null, null);

    private static TableCellTextFormatPlan DisabledFormat(
        TableCellTextFormatStatus status,
        TableCellTextFormatKind kind,
        uint? shapeId = null,
        int? row = null,
        int? col = null) =>
        new(status, shapeId, row, col, kind, null, null);

    private static bool GetRunFormat(Run run, TableCellTextFormatKind kind) => kind switch
    {
        TableCellTextFormatKind.Bold => run.Bold,
        TableCellTextFormatKind.Italic => run.Italic,
        TableCellTextFormatKind.Underline => run.Underline,
        _ => false,
    };

    private static void SetRunFormat(Run run, TableCellTextFormatKind kind, bool value)
    {
        switch (kind)
        {
            case TableCellTextFormatKind.Bold:
                run.Bold = value;
                run.BoldSet = true;
                break;
            case TableCellTextFormatKind.Italic:
                run.Italic = value;
                run.ItalicSet = true;
                break;
            case TableCellTextFormatKind.Underline:
                run.Underline = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

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
