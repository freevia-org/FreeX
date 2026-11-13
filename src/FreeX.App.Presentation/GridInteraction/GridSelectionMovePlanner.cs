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

    /// <summary>
    /// Which edge of the selection's border the pointer is on (Excel: double-clicking it jumps to the
    /// edge of the data that way, like Ctrl+Arrow), or <see cref="GridSelectionBorderEdge.None"/>.
    /// </summary>
    public static GridSelectionBorderEdge HitBorderEdge(
        ViewportModel? viewport,
        GridRange? selectedRange,
        IReadOnlyList<GridRange>? selectedRanges,
        GridPoint pointer,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double borderHitThickness = 4,
        double metricScale = 1)
    {
        if (viewport is null || selectedRange is not { } range)
            return GridSelectionBorderEdge.None;

        var layout = GridSelectionLayoutPlanner.CalculateVisibleSelectionLayout(
            viewport,
            range,
            rowHeaderWidth,
            columnHeaderHeight,
            metricScale);
        return HitBorderEdge(layout, selectedRanges, pointer, borderHitThickness);
    }

    /// <summary>The same as above for a host that already has the selection's visible layout.</summary>
    public static GridSelectionBorderEdge HitBorderEdge(
        GridSelectionLayout? layout,
        IReadOnlyList<GridRange>? selectedRanges,
        GridPoint pointer,
        double borderHitThickness = 4,
        double handleSize = 6,
        double handleHitPadding = 3)
    {
        if (!IsOnMoveBorder(layout, selectedRanges, pointer, borderHitThickness, handleSize, handleHitPadding) ||
            layout is not { } visible)
        {
            return GridSelectionBorderEdge.None;
        }

        // The nearest edge wins where two meet at a corner.
        var rect = visible.Rect;
        var candidates = new List<(GridSelectionBorderEdge Edge, double Distance)>(4);
        if (visible.HasTopEdge) candidates.Add((GridSelectionBorderEdge.Top, Math.Abs(pointer.Y - rect.Top)));
        if (visible.HasBottomEdge) candidates.Add((GridSelectionBorderEdge.Bottom, Math.Abs(pointer.Y - rect.Bottom)));
        if (visible.HasLeftEdge) candidates.Add((GridSelectionBorderEdge.Left, Math.Abs(pointer.X - rect.Left)));
        if (visible.HasRightEdge) candidates.Add((GridSelectionBorderEdge.Right, Math.Abs(pointer.X - rect.Right)));
        return candidates.Count == 0
            ? GridSelectionBorderEdge.None
            : candidates.MinBy(candidate => candidate.Distance).Edge;
    }

    /// <summary>The cell a border double-click jumps to from <paramref name="current"/>: Ctrl+Arrow's data boundary.</summary>
    public static CellAddress FindBorderJumpTarget(Sheet? sheet, CellAddress current, GridSelectionBorderEdge edge) => edge switch
    {
        GridSelectionBorderEdge.Top => ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(sheet, current, -1),
        GridSelectionBorderEdge.Bottom => ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(sheet, current, 1),
        GridSelectionBorderEdge.Left => ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(sheet, current, -1),
        GridSelectionBorderEdge.Right => ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(sheet, current, 1),
        _ => current,
    };

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

/// <summary>An edge of the selection's border (see <see cref="GridSelectionMovePlanner.HitBorderEdge"/>).</summary>
public enum GridSelectionBorderEdge
{
    None,
    Top,
    Bottom,
    Left,
    Right,
}
