namespace FreeP.App.Compositor;

/// <summary>
/// Shared visual contract for the compact review comments pane.
/// Hosts own control construction and event wiring; these values keep their
/// typography and chrome geometry aligned with the WPF-authority evidence.
/// </summary>
public static class PresentationCommentPaneVisualMetrics
{
    public const double SummaryFontSize = 11;
    public const double FilterFontSize = 10;
    public const double AuthorFontSize = 11;
    public const double StatusFontSize = 10;
    public const double BodyFontSize = 11;
    public const double ReplyFontSize = 10;
    public const double MentionFontSize = 10;
    public const double CompactControlFontSize = 12;
    public const double CompactControlHeight = 22;
    public const double CloseMinimumWidth = 64;
    public const double AddCommentInputMinimumWidth = 220;
    public const double AddCommentButtonWidth = 102;
    public const double CardBottomMargin = 6;

    /// <summary>
    /// The compact toolbar fits above the selected comment card. Fixed action
    /// widths preserve the same wrapping and command order across the WPF and
    /// Avalonia host font/rendering stacks.
    /// </summary>
    public static double ToolbarActionWidth(string commandId) => commandId switch
    {
        PresentationReviewWorkflowPlanner.EditCommentCommandId => 108,
        PresentationReviewWorkflowPlanner.DeleteCommentCommandId => 124,
        PresentationReviewWorkflowPlanner.PreviousCommentCommandId => 149,
        PresentationReviewWorkflowPlanner.NextCommentCommandId => 118,
        PresentationReviewWorkflowPlanner.ResolveCommentCommandId => 137,
        PresentationReviewWorkflowPlanner.ReopenCommentCommandId => 139,
        _ => 88,
    };
}
