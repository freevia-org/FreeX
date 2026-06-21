using System.Windows;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static class GridTextBoxPlacementPlanner
{
    public const double DefaultTextBoxWidth = DrawingInsertionPlanner.DefaultTextBoxWidth;
    public const double DefaultTextBoxHeight = DrawingInsertionPlanner.DefaultTextBoxHeight;
    public const double MinimumTextBoxSize = GridObjectDragPlanner.MinimumObjectSize;

    public static TextBoxPlacementRequest CreateRequest(
        CellAddress anchor,
        Point start,
        Point current)
    {
        if (!GridShapePlacementPlanner.IsMeaningfulDrag(start, current))
            return new TextBoxPlacementRequest(anchor, DefaultTextBoxWidth, DefaultTextBoxHeight);

        return new TextBoxPlacementRequest(
            anchor,
            Math.Max(MinimumTextBoxSize, Math.Abs(current.X - start.X)),
            Math.Max(MinimumTextBoxSize, Math.Abs(current.Y - start.Y)));
    }
}
