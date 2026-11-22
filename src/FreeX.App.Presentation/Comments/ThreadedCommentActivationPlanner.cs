using FreeX.Core.Model;

namespace FreeX.App.Presentation.Comments;

/// <summary>How the user reached a commented cell.</summary>
public enum CommentActivationSource
{
    /// <summary>Mouse pointer resting over the cell.</summary>
    Hover,

    /// <summary>The cell became the active cell (click, arrow keys, Go To, next/previous comment).</summary>
    Selection,

    /// <summary>The user clicked the corner comment indicator itself.</summary>
    IndicatorClick
}

public enum CommentActivationAction
{
    /// <summary>Nothing to show.</summary>
    None,

    /// <summary>Read-only preview popup (legacy notes and hover over threads).</summary>
    ShowPreview,

    /// <summary>Editable threaded-comment card: reply box, Resolve/Reopen, edit own comment.</summary>
    OpenCard
}

/// <summary>Where the docked Comments pane goes, in the owner window's coordinate space.</summary>
public readonly record struct CommentPaneDockPlan(
    bool IsDocked,
    double Left,
    double Top,
    double Width,
    double Height);

/// <summary>
/// Shared (both hosts) decisions for Excel's threaded-comment interaction model: selecting a cell
/// with a thread or clicking its purple indicator opens the editable card (hover stays a read-only
/// preview), the Comments pane docks to the right of the grid, and Show Comments toggles the pane.
/// </summary>
public static class ThreadedCommentActivationPlanner
{
    public const double MinPaneWidth = 280;
    public const double MaxPaneWidth = 400;
    public const double PaneOwnerFraction = 0.28;

    /// <summary>The grid keeps at least this much width; below it the pane is not docked.</summary>
    public const double MinGridWidth = 320;

    public static CommentActivationAction Decide(
        CommentActivationSource source,
        CellCommentDisplayKind? kind,
        bool inlineEditorOpen = false)
    {
        if (kind is null || inlineEditorOpen)
            return CommentActivationAction.None;

        var hasThread = kind is CellCommentDisplayKind.ThreadedComment or CellCommentDisplayKind.Mixed;
        return source switch
        {
            CommentActivationSource.Hover => CommentActivationAction.ShowPreview,
            _ when hasThread => CommentActivationAction.OpenCard,
            _ => CommentActivationAction.ShowPreview
        };
    }

    /// <summary>
    /// True when the corner indicator hit area was clicked: the top-right triangle whose legs are
    /// <paramref name="indicatorSize"/> long, in cell-local coordinates (origin at the cell's top-left).
    /// </summary>
    public static bool IsIndicatorHit(double localX, double localY, double cellWidth, double cellHeight, double indicatorSize = 8)
    {
        if (cellWidth <= 0 || cellHeight <= 0 || indicatorSize <= 0)
            return false;

        var size = Math.Min(indicatorSize, Math.Min(cellWidth, cellHeight));
        var dx = cellWidth - localX;
        return localX >= 0 && localX <= cellWidth && localY >= 0 && dx >= 0 && localY <= size && localY + dx <= size;
    }

    public static CommentPaneDockPlan PlanPaneDock(
        double ownerLeft,
        double ownerTop,
        double ownerWidth,
        double ownerHeight,
        double contentTopInset,
        double frameInset = 0)
    {
        var usableWidth = ownerWidth - (2 * frameInset);
        var width = Math.Clamp(usableWidth * PaneOwnerFraction, MinPaneWidth, MaxPaneWidth);
        var height = ownerHeight - contentTopInset - frameInset;
        if (usableWidth - width < MinGridWidth || height < 160)
            return new CommentPaneDockPlan(false, 0, 0, 0, 0);

        return new CommentPaneDockPlan(
            true,
            ownerLeft + ownerWidth - frameInset - width,
            ownerTop + contentTopInset,
            width,
            height);
    }

    /// <summary>Show Comments is a toggle: pressing it while the pane is open closes it.</summary>
    public static bool ShouldClosePaneOnToggle(bool paneOpen) => paneOpen;
}
