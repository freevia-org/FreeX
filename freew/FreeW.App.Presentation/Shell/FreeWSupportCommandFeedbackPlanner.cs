using Free.Shared.AppServices;

namespace FreeW.App.Presentation.Shell;

public enum FreeWCommandFeedbackTone
{
    Information,
    Warning,
}

public sealed record FreeWCommandFeedbackPlan(
    string Title,
    string Message,
    FreeWCommandFeedbackTone Tone);

/// <summary>Owns cross-renderer feedback decisions for the Help and support command family.</summary>
public static class FreeWSupportCommandFeedbackPlanner
{
    public static FreeWCommandFeedbackPlan? PlanExternalUriLaunch(
        ExternalUriLaunchResult result,
        string title,
        string url) =>
        result == ExternalUriLaunchResult.Launched
            ? null
            : new(
                title,
                FreeWApplicationFrameTextCatalog.FormatExternalLinkFailure(title, url),
                FreeWCommandFeedbackTone.Warning);

    public static FreeWCommandFeedbackPlan PlanDiagnosticsCopy(PlatformClipboardWriteResult result) =>
        result.Status switch
        {
            PlatformClipboardWriteStatus.Success => new(
                FreeWApplicationFrameTextCatalog.CopyDiagnosticsTitle,
                FreeWApplicationFrameTextCatalog.DiagnosticsCopiedMessage,
                FreeWCommandFeedbackTone.Information),
            PlatformClipboardWriteStatus.Unavailable => new(
                FreeWApplicationFrameTextCatalog.CopyDiagnosticsTitle,
                FreeWApplicationFrameTextCatalog.ClipboardUnavailableMessage,
                FreeWCommandFeedbackTone.Warning),
            _ => new(
                FreeWApplicationFrameTextCatalog.CopyDiagnosticsTitle,
                FreeWApplicationFrameTextCatalog.FormatClipboardFailure(
                    result.ErrorMessage ?? "Clipboard write failed."),
                FreeWCommandFeedbackTone.Warning),
        };
}
