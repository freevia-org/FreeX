using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public static class GridAutofillPlanner
{
    public static CellAddress ConstrainTarget(GridRange source, CellAddress target)
    {
        var verticalDistance = target.Row > source.End.Row ? target.Row - source.End.Row : 0;
        var horizontalDistance = target.Col > source.End.Col ? target.Col - source.End.Col : 0;

        return verticalDistance >= horizontalDistance
            ? new CellAddress(target.Sheet, target.Row, source.End.Col)
            : new CellAddress(target.Sheet, source.End.Row, target.Col);
    }

    public static GridAutoScrollRequest CalculateEdgeScrollIntent(
        double pointerX,
        double pointerY,
        double width,
        double height,
        double rowHeaderWidth,
        double columnHeaderHeight,
        double edgeThreshold = 24)
    {
        if (width <= 0 || height <= 0)
            return new GridAutoScrollRequest(0, 0);

        var horizontal = pointerX >= width - edgeThreshold
            ? 1
            : pointerX <= rowHeaderWidth + edgeThreshold
                ? -1
                : 0;
        var vertical = pointerY >= height - edgeThreshold
            ? 1
            : pointerY <= columnHeaderHeight + edgeThreshold
                ? -1
                : 0;

        return new GridAutoScrollRequest(horizontal, vertical);
    }
}
