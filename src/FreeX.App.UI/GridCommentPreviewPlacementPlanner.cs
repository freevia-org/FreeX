using System.Windows;
using Free.Shared.Drawing;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Model;

using SharedCommentPreviewLayoutSize = FreeX.App.Presentation.Comments.CommentPreviewLayoutSize;
using SharedCommentPreviewPlacement = FreeX.App.Presentation.Comments.CommentPreviewPlacement;
using SharedCommentPreviewPlacementPlanner = FreeX.App.Presentation.Comments.CommentPreviewPlacementPlanner;

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

/// <summary>
/// WPF boundary adapter for the platform-neutral comment preview placement planner.
/// </summary>
public static class GridCommentPreviewPlacementPlanner
{
    public const double EdgePadding = SharedCommentPreviewPlacementPlanner.EdgePadding;
    public const double CellGap = SharedCommentPreviewPlacementPlanner.CellGap;
    public const double MinWidth = SharedCommentPreviewPlacementPlanner.MinWidth;
    public const double MaxWidth = SharedCommentPreviewPlacementPlanner.MaxWidth;
    public const double MinHeight = SharedCommentPreviewPlacementPlanner.MinHeight;
    public const double MaxHeight = SharedCommentPreviewPlacementPlanner.MaxHeight;

    public static GridCommentPreviewPlacement Calculate(
        Rect cellRect,
        Size viewportSize,
        CellCommentDisplay display) =>
        ToWpf(SharedCommentPreviewPlacementPlanner.Calculate(
            ToLayout(cellRect),
            ToLayoutSize(viewportSize),
            display));

    public static GridCommentPreviewPlacement Calculate(
        Rect cellRect,
        Size viewportSize,
        Size desiredSize) =>
        ToWpf(SharedCommentPreviewPlacementPlanner.Calculate(
            ToLayout(cellRect),
            ToLayoutSize(viewportSize),
            ToLayoutSize(desiredSize)));

    /// <summary>
    /// Converts the shared connector coordinates to WPF points while preserving the native API.
    /// </summary>
    public static GridCommentConnectorLine CalculateConnector(Rect cellRect, GridCommentPreviewPlacement placement)
    {
        var connector = SharedCommentPreviewPlacementPlanner.CalculateConnector(
            ToLayout(cellRect),
            new SharedCommentPreviewPlacement(
                placement.HorizontalOffset,
                placement.VerticalOffset,
                placement.Width,
                placement.MaxHeight));

        return new GridCommentConnectorLine(
            new Point(connector.Start.X, connector.Start.Y),
            new Point(connector.End.X, connector.End.Y));
    }

    public static Size EstimatePreviewSize(CellCommentDisplay display)
    {
        var size = SharedCommentPreviewPlacementPlanner.EstimatePreviewSize(display);
        return new Size(size.Width, size.Height);
    }

    private static LayoutRect ToLayout(Rect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static SharedCommentPreviewLayoutSize ToLayoutSize(Size size) =>
        new(size.Width, size.Height);

    private static GridCommentPreviewPlacement ToWpf(SharedCommentPreviewPlacement placement) =>
        new(placement.HorizontalOffset, placement.VerticalOffset, placement.Width, placement.MaxHeight);
}
