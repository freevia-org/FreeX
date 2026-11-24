using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum TableCellPointerAction
{
    /// <summary>The click is not for the in-canvas table editor; leave an open cell edit alone.</summary>
    None,

    /// <summary>The click landed off any table: commit an open cell edit.</summary>
    CommitCellEdit,

    /// <summary>A single click on a table cell: make it the active cell.</summary>
    SelectCell,

    /// <summary>A double click on a table cell: make it active and open the cell editor.</summary>
    ActivateCellEdit,
}

public readonly record struct TableCellPointerPlan(
    TableCellPointerAction Action,
    uint ShapeId = 0,
    int Row = 0,
    int Col = 0)
{
    public bool HitCell => Action is TableCellPointerAction.SelectCell or TableCellPointerAction.ActivateCellEdit;
}

/// <summary>The in-canvas table-cell highlight: placement plus the shared style.</summary>
public sealed record TableCellHighlightPlan(
    InCanvasEditorPlacement Placement,
    uint StrokeArgb,
    double StrokeThickness,
    uint FillArgb)
{
    public const uint DefaultStrokeArgb = 0xFF2196F3;
    public const double DefaultStrokeThickness = 2.0;
    public const uint DefaultFillArgb = 0x182196F3;
}

/// <summary>
/// Routes pointer presses onto the in-canvas table cell editor and plans the active-cell
/// highlight, so the WPF and Avalonia editors share one click-routing rule and one highlight style.
/// </summary>
public static class TableCellPointerPlanner
{
    public static TableCellPointerPlan Plan(
        Slide? slide,
        Presentation? presentation,
        double slideX,
        double slideY,
        int clickCount)
    {
        if (slide is null || presentation is null)
            return default;

        var hitId = ShapeHitTester.HitTest(slide, presentation, slideX, slideY);
        if (!hitId.HasValue)
            return new TableCellPointerPlan(TableCellPointerAction.CommitCellEdit);

        var shape = ShapeHitTester.FindShape(slide, hitId.Value);
        if (shape?.Kind != SlideShapeKind.Table || shape.Table is null)
            return new TableCellPointerPlan(TableCellPointerAction.CommitCellEdit);

        if (TableCellHitTester.HitTest(shape, slideX, slideY) is not { } cell)
            return default;

        return new TableCellPointerPlan(
            clickCount >= 2 ? TableCellPointerAction.ActivateCellEdit : TableCellPointerAction.SelectCell,
            shape.Id,
            cell.Row,
            cell.Col);
    }

    /// <summary>Plans the highlight for the selected table's active cell, or null when none is editable.</summary>
    public static TableCellHighlightPlan? PlanHighlight(
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        SlideTransformCore transform)
    {
        if (slide is null)
            return null;

        var state = TableCellEditPlanner.PlanSelectedCell(slide, selectedShapeIds, activeCell);
        if (!state.CanEditText || state.ShapeId is null || state.Row is null || state.Col is null)
            return null;

        var shape = ShapeHitTester.FindShape(slide, state.ShapeId.Value);
        if (shape?.Kind != SlideShapeKind.Table)
            return null;

        var cellRect = TableCellHitTester.GetCellRect(shape, state.Row.Value, state.Col.Value);
        if (cellRect is null)
            return null;

        var placement = TableCellEditPlanner.PlanCellEditorPlacement(
            shape,
            cellRect.Value,
            transform,
            minimumWidth: 0,
            minimumHeight: 0);
        return new TableCellHighlightPlan(
            placement,
            TableCellHighlightPlan.DefaultStrokeArgb,
            TableCellHighlightPlan.DefaultStrokeThickness,
            TableCellHighlightPlan.DefaultFillArgb);
    }
}
