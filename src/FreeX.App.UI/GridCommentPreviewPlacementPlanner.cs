using System.Windows;
using FreeX.Core.Model;

namespace FreeX.App.UI;

public readonly record struct GridCommentPreviewPlacement(
    double HorizontalOffset,
    double VerticalOffset,
    double Width,
    double MaxHeight);

/// <summary>
/// The two endpoints of the connector line that anchors an always-shown (pinned) note box back to
/// the cell corner it was raised from -- matching Excel's pinned-note leader line.
/// </summary>
public readonly record struct GridCommentConnectorLine(Point Start, Point End);

public static class GridCommentPreviewPlacementPlanner
{
    public const double EdgePadding = 8;
    public const double CellGap = 6;
    public const double MinWidth = 180;
    public const double MaxWidth = 320;
    public const double MinHeight = 72;
    public const double MaxHeight = 220;

    public static GridCommentPreviewPlacement Calculate(
        Rect cellRect,
        Size viewportSize,
        CellCommentDisplay display) =>
        Calculate(cellRect, viewportSize, EstimatePreviewSize(display));

    public static GridCommentPreviewPlacement Calculate(
        Rect cellRect,
        Size viewportSize,
        Size desiredSize)
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

        return new GridCommentPreviewPlacement(x, y, width, height);
    }

    /// <summary>
    /// Computes the connector line that bridges a pinned note box back to the corner of the cell it
    /// was raised from, so two or more pinned boxes floating near each other remain unambiguous about
    /// which cell each one belongs to (Excel draws this same leader line for a persistent note box
    /// that isn't flush against its cell). The anchor corner is chosen to match whichever side
    /// <see cref="Calculate(Rect, Size, Size)"/> placed the box on (left or right of the cell), using
    /// the box's own placement result rather than re-deriving it, so the line always lands on the
    /// actual rendered box edge even after edge-of-viewport clamping.
    /// </summary>
    public static GridCommentConnectorLine CalculateConnector(Rect cellRect, GridCommentPreviewPlacement placement)
    {
        var boxCenterX = placement.HorizontalOffset + placement.Width / 2;
        var cellCenterX = cellRect.Left + cellRect.Width / 2;
        var boxIsRight = boxCenterX >= cellCenterX;

        var cellAnchor = boxIsRight
            ? new Point(cellRect.Right, cellRect.Top)
            : new Point(cellRect.Left, cellRect.Top);
        var boxAnchor = boxIsRight
            ? new Point(placement.HorizontalOffset, placement.VerticalOffset)
            : new Point(placement.HorizontalOffset + placement.Width, placement.VerticalOffset);

        return new GridCommentConnectorLine(cellAnchor, boxAnchor);
    }

    public static Size EstimatePreviewSize(CellCommentDisplay display)
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
        return new Size(width, height);
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Min(Math.Max(value, min), max);
}
