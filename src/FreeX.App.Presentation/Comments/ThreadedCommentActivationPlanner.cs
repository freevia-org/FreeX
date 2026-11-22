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

/// <summary>
/// Shared (both hosts) decisions for Excel's threaded-comment interaction model: selecting a cell
/// with a thread or clicking its purple indicator opens the editable card (hover stays a read-only
/// preview), the Comments pane is a real layout column to the right of the grid (the grid shrinks,
/// the pane is resizable and closes via its X button), and Show Comments toggles the pane.
/// </summary>
public static class ThreadedCommentActivationPlanner
{
    public const double MinPaneWidth = 280;
    public const double MaxPaneWidth = 400;
    public const double MaxResizablePaneWidth = 640;
    public const double PaneOwnerFraction = 0.28;

    /// <summary>The grid column keeps at least this much width while the pane is resized.</summary>
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

    /// <summary>
    /// Initial width of the docked Comments pane column for a work area <paramref name="hostWidth"/>
    /// wide (grid + pane).
    /// </summary>
    public static double DefaultPaneWidth(double hostWidth) =>
        ClampPaneWidth(Math.Clamp(hostWidth * PaneOwnerFraction, MinPaneWidth, MaxPaneWidth), hostWidth);

    /// <summary>
    /// Clamps a requested (default or user-dragged) pane column width so the pane keeps its minimum
    /// and the grid column keeps at least <see cref="MinGridWidth"/> of the work area. When the work
    /// area is too narrow for both, the pane keeps its minimum width.
    /// </summary>
    public static double ClampPaneWidth(double requestedWidth, double hostWidth)
    {
        if (double.IsNaN(requestedWidth))
            requestedWidth = MinPaneWidth;

        var max = hostWidth > 0
            ? Math.Min(MaxResizablePaneWidth, hostWidth - MinGridWidth)
            : MaxResizablePaneWidth;
        max = Math.Max(MinPaneWidth, max);
        return Math.Clamp(requestedWidth, MinPaneWidth, max);
    }

    /// <summary>Show Comments is a toggle: pressing it while the pane is open closes it.</summary>
    public static bool ShouldClosePaneOnToggle(bool paneOpen) => paneOpen;
}
