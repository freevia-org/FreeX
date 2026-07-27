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
    public const double AddCommentButtonMinimumWidth = 96;
    public const double CardBottomMargin = 6;
}
