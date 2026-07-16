using FreeX.Core.Model;

namespace FreeX.App.Presentation.GridInteraction;

/// <summary>
/// Pure geometry and intent math for dragging a selection by its border (cut-and-move): whether the
/// pointer is on a draggable selection edge, which cell the grab is anchored to, and the destination
/// range a move produces (clamped to the worksheet bounds). Shared by the desktop hosts.
/// </summary>
public static class GridSelectionMovePlanner
{
    public static bool IsOnMoveBorder(
        ViewportModel? viewport,
        GridRange? selectedRange,
        IReadOnlyList<GridRange>? selectedRanges,
        GridPoint pointer,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double borderHitThickness = 4,
        double metricScale = 1,
        double handleSize = 6,
        double handleHitPadding = 3)
    {
        if (viewport is null || selectedRange is not { } range)
            return false;

        var layout = GridSelectionLayoutPlanner.CalculateVisibleSelectionLayout(
            viewport,
            range,
            rowHeaderWidth,
            columnHeaderHeight,
            metricScale);
        return IsOnMoveBorder(
            layout,
            selectedRanges,
            pointer,
            borderHitThickness,
            handleSize,
            handleHitPadding);
    }

    public static bool IsOnMoveBorder(
        GridSelectionLayout? selectionLayout,
        IReadOnlyList<GridRange>? selectedRanges,
        GridPoint pointer,
        double borderHitThickness = 4,
        double handleSize = 6,
        double handleHitPadding = 3)
    {
        if (selectionLayout is not { } visible)
            return false;

        if (selectedRanges is { Count: > 0 })
            return false;

        if (GridAutofillPlanner.IsOnHandle(visible, pointer, handleSize, handleHitPadding))
            return false;

        var rect = visible.Rect;
        var insideHorizontalSpan = pointer.X >= rect.Left - borderHitThickness &&
            pointer.X <= rect.Right + borderHitThickness;
        var insideVerticalSpan = pointer.Y >= rect.Top - borderHitThickness &&
            pointer.Y <= rect.Bottom + borderHitThickness;

        return (visible.HasTopEdge && insideHorizontalSpan && IsNear(pointer.Y, rect.Top, borderHitThickness)) ||
            (visible.HasBottomEdge && insideHorizontalSpan && IsNear(pointer.Y, rect.Bottom, borderHitThickness)) ||
            (visible.HasLeftEdge && insideVerticalSpan && IsNear(pointer.X, rect.Left, borderHitThickness)) ||
            (visible.HasRightEdge && insideVerticalSpan && IsNear(pointer.X, rect.Right, borderHitThickness));
    }

    public static CellAddress ClampDragStartCell(GridRange source, CellAddress dragStartCell)
    {
        var row = Math.Min(Math.Max(dragStartCell.Row, source.Start.Row), source.End.Row);
        var col = Math.Min(Math.Max(dragStartCell.Col, source.Start.Col), source.End.Col);
        return new CellAddress(source.Start.Sheet, row, col);
    }

    public static GridRange? CalculateTargetRange(
        GridRange source,
        CellAddress dragStartCell,
        CellAddress currentCell)
    {
        var anchor = ClampDragStartCell(source, dragStartCell);
        var rowDelta = (long)currentCell.Row - anchor.Row;
        var colDelta = (long)currentCell.Col - anchor.Col;

        var startRow = (long)source.Start.Row + rowDelta;
        var startCol = (long)source.Start.Col + colDelta;
        var endRow = (long)source.End.Row + rowDelta;
        var endCol = (long)source.End.Col + colDelta;

        if (startRow < 1 ||
            startCol < 1 ||
            endRow > CellAddress.MaxRow ||
            endCol > CellAddress.MaxCol)
        {
            return null;
        }

        return new GridRange(
            new CellAddress(source.Start.Sheet, (uint)startRow, (uint)startCol),
            new CellAddress(source.Start.Sheet, (uint)endRow, (uint)endCol));
    }

    private static bool IsNear(double value, double target, double tolerance) =>
        Math.Abs(value - target) <= tolerance;
}
