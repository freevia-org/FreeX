using System.Windows;
using FreeX.Core.Model;
using PresentationPlanner = FreeX.App.Presentation.GridInteraction.GridSelectionMovePlanner;
using GridPoint = FreeX.App.Presentation.GridInteraction.GridPoint;

namespace FreeX.App.UI;

/// <summary>
/// Thin WPF adapter over the shared <see cref="PresentationPlanner"/>: converts the host's
/// <see cref="Point"/> to the portable <see cref="GridPoint"/> and delegates all selection-move
/// math (border hit-testing, grab-cell clamping, destination-range computation) to the canonical
/// Presentation type.
/// </summary>
public static class GridSelectionMovePlanner
{
    public static bool IsOnMoveBorder(
        ViewportModel? viewport,
        GridRange? selectedRange,
        IReadOnlyList<GridRange>? selectedRanges,
        Point pointer,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double borderHitThickness = 4) =>
        PresentationPlanner.IsOnMoveBorder(
            viewport,
            selectedRange,
            selectedRanges,
            new GridPoint(pointer.X, pointer.Y),
            rowHeaderWidth,
            columnHeaderHeight,
            borderHitThickness);

    public static CellAddress ClampDragStartCell(GridRange source, CellAddress dragStartCell) =>
        PresentationPlanner.ClampDragStartCell(source, dragStartCell);

    public static GridRange? CalculateTargetRange(
        GridRange source,
        CellAddress dragStartCell,
        CellAddress currentCell) =>
        PresentationPlanner.CalculateTargetRange(source, dragStartCell, currentCell);
}
