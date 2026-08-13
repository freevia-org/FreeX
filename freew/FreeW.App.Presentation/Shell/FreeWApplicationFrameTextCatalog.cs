namespace FreeW.App.Presentation.Shell;

public sealed record FreeWFrameActionText(string Label, string HelpText);

public sealed record FreeWSemanticIdentity(string AutomationId, string AutomationName);

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
    public const string CopyDiagnosticsTitle = "Copy Diagnostics";
    public const string ClipboardUnavailableMessage = "FreeW could not access the clipboard.";
    public const string DiagnosticsCopiedMessage = "FreeW diagnostics were copied to the clipboard.";

    public static FreeWSemanticIdentity PreviousPagePairSemantic { get; } = new(
        "FreeW.SideToSide.Previouspair",
        "Previous Side-to-Side page pair");

    public static FreeWSemanticIdentity NextPagePairSemantic { get; } = new(
        "FreeW.SideToSide.Nextpair",
        "Next Side-to-Side page pair");

    public const string PagePairStatusAutomationId = "FreeW.SideToSidePagePairStatus";

    public static string FormatExternalLinkFailure(string title, string url) =>
        $"FreeW could not open {title}. The link is:\n\n{url}";

    public static string FormatClipboardFailure(string errorMessage) =>
        $"FreeW could not access the clipboard: {errorMessage}";

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
