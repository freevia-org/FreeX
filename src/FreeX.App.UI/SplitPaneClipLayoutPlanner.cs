using System.Windows;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public static class SplitPaneClipLayoutPlanner
{
    public static SplitPaneClipRects CalculateClipRects(
        ViewportModel viewport,
        double actualWidth,
        double actualHeight)
    {
        var layout = GridView.CalculateSplitDividerLayout(viewport);
        var horizontalY = layout.HorizontalY ?? actualHeight;
        var verticalX = layout.VerticalX ?? actualWidth;
        var top = GridView.ColHeaderHeight;
        var left = GridView.CalculateRowHeaderWidth(viewport);
        var right = Math.Max(verticalX, actualWidth);
        var bottom = Math.Max(horizontalY, actualHeight);

        return new SplitPaneClipRects(
            new Rect(left, top, Math.Max(0, verticalX - left), Math.Max(0, horizontalY - top)),
            new Rect(verticalX, top, Math.Max(0, right - verticalX), Math.Max(0, horizontalY - top)),
            new Rect(left, horizontalY, Math.Max(0, verticalX - left), Math.Max(0, bottom - horizontalY)),
            new Rect(verticalX, horizontalY, Math.Max(0, right - verticalX), Math.Max(0, bottom - horizontalY)));
    }
}
