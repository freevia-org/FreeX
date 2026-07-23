namespace Free.Shared.Shell;

/// <summary>
/// The four window arrangements offered by View ▸ Window ▸ Arrange All. Mirrors the host-side
/// <c>WorkbookWindowArrangement</c> enum but lives in the portable shell tier so the layout planner
/// stays WPF-free and reusable from non-WPF hosts (Avalonia/Linux/macOS).
/// </summary>
public enum ShellWindowArrangement
{
    Tiled,
    Horizontal,
    Vertical,
    Cascade
}

/// <summary>
/// Pure geometry for View ▸ Window ▸ Arrange All. Rectangles are relative to the work-area origin;
/// the host adds the actual work-area offset before moving the live window. WPF-free (returns the
/// neutral <see cref="ShellRect"/>) so it can be unit-tested without standing up windows and reused
/// from non-WPF hosts; WPF hosts translate each <see cref="ShellRect"/> to <c>System.Windows.Rect</c>
/// at the platform boundary.
/// </summary>
public static class ArrangeAllLayoutPlanner
{
    public const double FallbackWidth = 1024;
    public const double FallbackHeight = 768;
    public const double CascadeSizeFraction = 0.75;
    public const double CascadeOffset = 24;

    public static IReadOnlyList<ShellRect> Arrange(
        ShellWindowArrangement arrangement,
        double workAreaWidth,
        double workAreaHeight,
        int windowCount)
    {
        if (windowCount <= 0 || !Enum.IsDefined(arrangement))
            return Array.Empty<ShellRect>();

        var width = workAreaWidth > 0 ? workAreaWidth : FallbackWidth;
        var height = workAreaHeight > 0 ? workAreaHeight : FallbackHeight;

        return arrangement switch
        {
            ShellWindowArrangement.Tiled => ArrangeTiled(width, height, windowCount),
            ShellWindowArrangement.Horizontal => ArrangeHorizontal(width, height, windowCount),
            ShellWindowArrangement.Vertical => ArrangeVertical(width, height, windowCount),
            ShellWindowArrangement.Cascade => ArrangeCascade(width, height, windowCount),
            _ => Array.Empty<ShellRect>()
        };
    }

    /// <summary>
    /// Builds a row-first tiled layout with a fixed maximum number of columns. This is the neutral
    /// geometry entry point for hosts whose Arrange All policy keeps the same column count on the
    /// final, incomplete row (the existing FreeW WPF behavior uses a maximum of three columns).
    /// </summary>
    public static IReadOnlyList<ShellRect> ArrangeRowFirst(
        double workAreaWidth,
        double workAreaHeight,
        int windowCount,
        int maxColumns)
    {
        if (windowCount <= 0 || maxColumns <= 0)
            return Array.Empty<ShellRect>();

        var width = workAreaWidth > 0 ? workAreaWidth : FallbackWidth;
        var height = workAreaHeight > 0 ? workAreaHeight : FallbackHeight;
        var columns = Math.Min(windowCount, maxColumns);
        var rows = (int)Math.Ceiling((double)windowCount / columns);
        var tileWidth = width / columns;
        var tileHeight = height / rows;
        var bounds = new ShellRect[windowCount];

        for (var index = 0; index < windowCount; index++)
        {
            var column = index % columns;
            var row = index / columns;
            bounds[index] = new ShellRect(
                tileWidth * column,
                tileHeight * row,
                tileWidth,
                tileHeight);
        }

        return bounds;
    }

    private static IReadOnlyList<ShellRect> ArrangeTiled(double width, double height, int windowCount)
    {
        var columns = (int)Math.Ceiling(Math.Sqrt(windowCount));
        var rows = (int)Math.Ceiling((double)windowCount / columns);
        var bounds = new List<ShellRect>(windowCount);
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

    private static IReadOnlyList<ShellRect> ArrangeHorizontal(double width, double height, int windowCount)
    {
        var bounds = new ShellRect[windowCount];
        for (var index = 0; index < windowCount; index++)
        {
            var top = height * index / windowCount;
            var bottom = height * (index + 1) / windowCount;
            bounds[index] = FromEdges(0, top, width, bottom);
        }

        return bounds;
    }

    private static IReadOnlyList<ShellRect> ArrangeVertical(double width, double height, int windowCount)
    {
        var bounds = new ShellRect[windowCount];
        for (var index = 0; index < windowCount; index++)
        {
            var left = width * index / windowCount;
            var right = width * (index + 1) / windowCount;
            bounds[index] = FromEdges(left, 0, right, height);
        }

        return bounds;
    }

    private static IReadOnlyList<ShellRect> ArrangeCascade(double width, double height, int windowCount)
    {
        var windowWidth = width * CascadeSizeFraction;
        var windowHeight = height * CascadeSizeFraction;
        var slackX = Math.Max(0, width - windowWidth);
        var slackY = Math.Max(0, height - windowHeight);
        var step = windowCount <= 1
            ? 0
            : Math.Min(CascadeOffset, Math.Min(slackX, slackY) / (windowCount - 1));

        var bounds = new ShellRect[windowCount];
        for (var index = 0; index < windowCount; index++)
        {
            var offset = step * index;
            bounds[index] = new ShellRect(offset, offset, windowWidth, windowHeight);
        }

        return bounds;
    }

    private static ShellRect FromEdges(double left, double top, double right, double bottom) =>
        new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
}
