using System.Windows;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// Pure geometry for View > Window > Arrange All. Rectangles are relative to the work-area origin;
/// MainWindow adds the actual work-area offset before moving the live WPF window.
/// </summary>
public static class ArrangeAllLayoutPlanner
{
    public const double FallbackWidth = 1024;
    public const double FallbackHeight = 768;
    public const double CascadeSizeFraction = 0.75;
    public const double CascadeOffset = 24;

    public static IReadOnlyList<Rect> Arrange(
        WorkbookWindowArrangement arrangement,
        double workAreaWidth,
        double workAreaHeight,
        int windowCount)
    {
        if (windowCount <= 0 || !Enum.IsDefined(arrangement))
            return Array.Empty<Rect>();

        var width = workAreaWidth > 0 ? workAreaWidth : FallbackWidth;
        var height = workAreaHeight > 0 ? workAreaHeight : FallbackHeight;

        return arrangement switch
        {
            WorkbookWindowArrangement.Tiled => ArrangeTiled(width, height, windowCount),
            WorkbookWindowArrangement.Horizontal => ArrangeHorizontal(width, height, windowCount),
            WorkbookWindowArrangement.Vertical => ArrangeVertical(width, height, windowCount),
            WorkbookWindowArrangement.Cascade => ArrangeCascade(width, height, windowCount),
            _ => Array.Empty<Rect>()
        };
    }

    private static IReadOnlyList<Rect> ArrangeTiled(double width, double height, int windowCount)
    {
        var columns = (int)Math.Ceiling(Math.Sqrt(windowCount));
        var rows = (int)Math.Ceiling((double)windowCount / columns);
        var bounds = new List<Rect>(windowCount);
        var index = 0;

        for (var row = 0; row < rows && index < windowCount; row++)
        {
            var remaining = windowCount - index;
            var columnsInRow = Math.Min(columns, remaining);
            var top = height * row / rows;
            var bottom = height * (row + 1) / rows;

            for (var column = 0; column < columnsInRow; column++)
            {
                var left = width * column / columnsInRow;
                var right = width * (column + 1) / columnsInRow;
                bounds.Add(FromEdges(left, top, right, bottom));
                index++;
            }
        }

        return bounds;
    }

    private static IReadOnlyList<Rect> ArrangeHorizontal(double width, double height, int windowCount)
    {
        var bounds = new Rect[windowCount];
        for (var index = 0; index < windowCount; index++)
        {
            var top = height * index / windowCount;
            var bottom = height * (index + 1) / windowCount;
            bounds[index] = FromEdges(0, top, width, bottom);
        }

        return bounds;
    }

    private static IReadOnlyList<Rect> ArrangeVertical(double width, double height, int windowCount)
    {
        var bounds = new Rect[windowCount];
        for (var index = 0; index < windowCount; index++)
        {
            var left = width * index / windowCount;
            var right = width * (index + 1) / windowCount;
            bounds[index] = FromEdges(left, 0, right, height);
        }

        return bounds;
    }

    private static IReadOnlyList<Rect> ArrangeCascade(double width, double height, int windowCount)
    {
        var windowWidth = width * CascadeSizeFraction;
        var windowHeight = height * CascadeSizeFraction;
        var slackX = Math.Max(0, width - windowWidth);
        var slackY = Math.Max(0, height - windowHeight);
        var step = windowCount <= 1
            ? 0
            : Math.Min(CascadeOffset, Math.Min(slackX, slackY) / (windowCount - 1));

        var bounds = new Rect[windowCount];
        for (var index = 0; index < windowCount; index++)
        {
            var offset = step * index;
            bounds[index] = new Rect(offset, offset, windowWidth, windowHeight);
        }

        return bounds;
    }

    private static Rect FromEdges(double left, double top, double right, double bottom) =>
        new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
}
