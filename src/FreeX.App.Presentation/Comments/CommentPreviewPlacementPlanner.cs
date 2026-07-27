using Free.Shared.Drawing;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Comments;

public readonly record struct CommentPreviewLayoutSize(double Width, double Height);

public readonly record struct CommentPreviewPlacement(
    double HorizontalOffset,
    double VerticalOffset,
    double Width,
    double MaxHeight);

public readonly record struct CommentPreviewConnectorLine(LayoutPoint Start, LayoutPoint End);

/// <summary>
/// Platform-neutral placement and sizing math for worksheet comment/note popups.
/// Renderers convert their native rectangle and point types at the boundary.
/// </summary>
public static class CommentPreviewPlacementPlanner
{
    public const double EdgePadding = 8;
    public const double CellGap = 6;
    public const double MinWidth = 180;
    public const double MaxWidth = 320;
    public const double MinHeight = 72;
    public const double MaxHeight = 220;

    public static CommentPreviewPlacement Calculate(
        LayoutRect cellRect,
        CommentPreviewLayoutSize viewportSize,
        CellCommentDisplay display) =>
        Calculate(cellRect, viewportSize, EstimatePreviewSize(display));

    public static CommentPreviewPlacement Calculate(
        LayoutRect cellRect,
        CommentPreviewLayoutSize viewportSize,
        CommentPreviewLayoutSize desiredSize)
    {
        var availableWidth = Math.Max(1, viewportSize.Width - EdgePadding * 2);
        var minWidth = Math.Min(MinWidth, availableWidth);
        var maxWidth = Math.Max(minWidth, Math.Min(MaxWidth, availableWidth));
        var width = Clamp(desiredSize.Width, minWidth, maxWidth);

        var availableHeight = Math.Max(1, viewportSize.Height - EdgePadding * 2);
        var minHeight = Math.Min(MinHeight, availableHeight);
        var maxHeight = Math.Max(minHeight, Math.Min(MaxHeight, availableHeight));
        var height = Clamp(desiredSize.Height, minHeight, maxHeight);

        var right = cellRect.Right + CellGap;
        var left = cellRect.Left - CellGap - width;
        var x = right + width <= viewportSize.Width - EdgePadding ? right : left;
        if (x < EdgePadding)
            x = Math.Max(EdgePadding, viewportSize.Width - EdgePadding - width);
        if (x + width > viewportSize.Width - EdgePadding)
            x = Math.Max(EdgePadding, viewportSize.Width - EdgePadding - width);

        var y = cellRect.Top;
        if (y + height > viewportSize.Height - EdgePadding)
            y = viewportSize.Height - EdgePadding - height;
        if (y < EdgePadding)
            y = EdgePadding;

        return new CommentPreviewPlacement(x, y, width, height);
    }

    public static CommentPreviewConnectorLine CalculateConnector(
        LayoutRect cellRect,
        CommentPreviewPlacement placement)
    {
        var boxCenterX = placement.HorizontalOffset + placement.Width / 2;
        var cellCenterX = cellRect.Left + cellRect.Width / 2;
        var boxIsRight = boxCenterX >= cellCenterX;

        var cellAnchor = boxIsRight
            ? new LayoutPoint(cellRect.Right, cellRect.Top)
            : new LayoutPoint(cellRect.Left, cellRect.Top);
        var boxAnchor = boxIsRight
            ? new LayoutPoint(placement.HorizontalOffset, placement.VerticalOffset)
            : new LayoutPoint(placement.HorizontalOffset + placement.Width, placement.VerticalOffset);

        return new CommentPreviewConnectorLine(cellAnchor, boxAnchor);
    }

    public static CommentPreviewLayoutSize EstimatePreviewSize(CellCommentDisplay display)
    {
        var text = string.IsNullOrEmpty(display.Body)
            ? display.Title
            : display.Title + Environment.NewLine + display.Body;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var longestLine = 0;
        foreach (var line in lines)
            longestLine = Math.Max(longestLine, line.Length);

        var width = longestLine * 7.0 + 34;
        var height = lines.Length * 18.0 + 34;
        return new CommentPreviewLayoutSize(width, height);
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Min(Math.Max(value, min), max);
}
