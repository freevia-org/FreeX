namespace FreeW.App.Presentation.Shell;

public sealed record FreeWFrameActionText(string Label, string HelpText);

/// <summary>
/// Canonical application-frame text shared by the native FreeW renderers.
/// </summary>
public static class FreeWApplicationFrameTextCatalog
{
    public const string HelpOnlineCommandName = "Help Online";
    public const string FeedbackCommandName = "Feedback";
    public const string CheckForUpdatesCommandName = "Check for Updates";
    public const string WebLayoutLabel = "Web Layout";
    public const string PageEditLabel = "Page Edit";
    public const string PreviousPagePairLabel = "Previous pair";
    public const string NextPagePairLabel = "Next pair";

    public static FreeWFrameActionText ReadMode { get; } = new(
        "Read Mode",
        "Toggle distraction-free Read Mode");

    public static FreeWFrameActionText PrintLayout { get; } = new(
        "Print Layout",
        "Print Layout page view");

    public static FreeWFrameActionText Draft { get; } = new(
        "Draft",
        "Draft: simplified continuous view for fast editing");
}
