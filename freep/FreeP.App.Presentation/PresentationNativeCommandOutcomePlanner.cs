using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;

namespace FreeP.App.Compositor;

public enum PresentationNativePrintOutcome
{
    Succeeded,
    Cancelled,
    Failed,
}

public enum PresentationNativePrintStatusProfile
{
    PresentationDialog,
    SystemPrintHandoff,
    SystemPrintHandoffWithCompletedPeriod,
}

public sealed record PresentationNativePrintPortResult(
    PresentationNativePrintOutcome Outcome,
    PresentationNativePrintStatusProfile StatusProfile,
    string? FailureReason = null)
{
    public static PresentationNativePrintPortResult Success(PresentationNativePrintStatusProfile statusProfile) =>
        new(PresentationNativePrintOutcome.Succeeded, statusProfile);

    public static PresentationNativePrintPortResult Cancel(PresentationNativePrintStatusProfile statusProfile) =>
        new(PresentationNativePrintOutcome.Cancelled, statusProfile);

    public static PresentationNativePrintPortResult Failure(
        PresentationNativePrintStatusProfile statusProfile,
        string? failureReason) =>
        new(PresentationNativePrintOutcome.Failed, statusProfile, failureReason);
}

public sealed record PresentationFileCommandFeedbackPlan(
    string? StatusText,
    PresentationFileCommandError? Error,
    bool ShowAvaloniaFileErrorDialog,
    string? UnavailableDialogTitle,
    string? UnavailableDialogMessage);

public enum PresentationVideoExportHostProfile
{
    Wpf,
    WpfWindows,
    AvaloniaLinux,
    AvaloniaWindows,
}

/// <summary>Owns renderer-neutral file-command labels, native print outcomes, and feedback copy.</summary>
public static class PresentationNativeCommandOutcomePlanner
{
    public static PresentationNativePrintPortResult BuildSystemPrintResult(
        PrintSubmissionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return BuildSystemPrintResult(
            result.Succeeded,
            result.Status == PrintSubmissionStatus.Cancelled,
            result.Succeeded || result.Status == PrintSubmissionStatus.Cancelled
                ? null
                : result.Message ?? PrintSubmissionFailureFallback);
    }

    public static PresentationNativePrintPortResult BuildSystemPrintResult(
        bool succeeded,
        bool cancelled,
        string? failureReason,
        bool completedStatusHasPeriod = false)
    {
        var profile = completedStatusHasPeriod
            ? PresentationNativePrintStatusProfile.SystemPrintHandoffWithCompletedPeriod
            : PresentationNativePrintStatusProfile.SystemPrintHandoff;
        return succeeded
            ? PresentationNativePrintPortResult.Success(profile)
            : cancelled
                ? PresentationNativePrintPortResult.Cancel(profile)
                : PresentationNativePrintPortResult.Failure(profile, failureReason);
    }

    public static string CommandText(PresentationFileCommand command) => command switch
    {
        PresentationFileCommand.Open => PresentationFileTextResources.Presentation.OpenCommand,
        PresentationFileCommand.Save or PresentationFileCommand.SaveAs =>
            PresentationFileTextResources.Presentation.SaveCommand,
        PresentationFileCommand.ExportPdf => PresentationExportPlanner.PdfExportCommandText,
        PresentationFileCommand.ExportNotesPagePdf => PresentationExportPlanner.NotesPagePdfExportCommandText,
        PresentationFileCommand.ExportImages => PresentationExportPlanner.ImageExportCommandText,
        PresentationFileCommand.Print => Resolve(PresentationShellTextCatalog.PrintCommandName),
        PresentationFileCommand.ExportVideo => PresentationExportPlanner.VideoExportCommandText,
        _ => command.ToString(),
    };

    public static PresentationNativeCommandResult BuildPrintCommandResult(
        PresentationNativePrintPortResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var statusText = PrintStatusText(result);
        return result.Outcome switch
        {
            PresentationNativePrintOutcome.Succeeded => PresentationNativeCommandResult.Success(statusText),
            PresentationNativePrintOutcome.Cancelled => PresentationNativeCommandResult.Cancel(statusText),
            PresentationNativePrintOutcome.Failed => PresentationNativeCommandResult.Failure(
                statusText,
                result.FailureReason ?? Resolve(PresentationShellTextCatalog.PrintFailureFallback)),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Unsupported print outcome."),
        };
    }

    public static string BuildPrintStatusText(PresentationNativePrintPortResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var statusText = PrintStatusText(result);
        return result.Outcome == PresentationNativePrintOutcome.Failed &&
               !string.IsNullOrWhiteSpace(result.FailureReason)
            ? $"{statusText}: {result.FailureReason}"
            : statusText;
    }

    public static string BuildPrintStatusText(PrintSubmissionResult result) =>
        BuildPrintStatusText(BuildSystemPrintResult(result));

    public static string BuildPrintPackageFailureStatus(string? failureReason) =>
        string.IsNullOrWhiteSpace(failureReason)
            ? PrintPackageNotBuiltFailure
            : failureReason;

    public static string PrintPackageNotBuiltFailure =>
        Resolve(PresentationShellTextCatalog.PrintPackageNotBuiltFailure);

    public static string PrintHandoffPlanNotBuiltFailure =>
        Resolve(PresentationShellTextCatalog.PrintHandoffPlanNotBuiltFailure);

    public static string PrintSubmissionFailureFallback =>
        Resolve(PresentationShellTextCatalog.PrintSubmissionFailureFallback);

    public static string ExportCompletedStatus =>
        Resolve(PresentationShellTextCatalog.ExportCompletedStatus);

    public static PresentationVideoExportHandoffHostCapabilities BuildVideoExportHostCapabilities(
        PresentationVideoExportHostProfile profile,
        bool canEncodeMp4,
        bool canCaptureNarration,
        bool canCaptureCameraAndMedia,
        bool canMuxTimedCaptions,
        string capabilityReason)
    {
        var hostName = Resolve(profile switch
        {
            PresentationVideoExportHostProfile.Wpf => PresentationShellTextCatalog.WpfVideoExportHostName,
            PresentationVideoExportHostProfile.WpfWindows =>
                PresentationShellTextCatalog.WpfWindowsVideoExportHostName,
            PresentationVideoExportHostProfile.AvaloniaLinux =>
                PresentationShellTextCatalog.AvaloniaLinuxVideoExportHostName,
            PresentationVideoExportHostProfile.AvaloniaWindows =>
                PresentationShellTextCatalog.AvaloniaWindowsVideoExportHostName,
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unsupported video host profile."),
        });
        var unavailableReason = profile == PresentationVideoExportHostProfile.AvaloniaLinux && canEncodeMp4
            ? Resolve(canCaptureNarration
                ? PresentationShellTextCatalog.FfmpegNarrationAvailableStatus
                : PresentationShellTextCatalog.FfmpegVideoOnlyAvailableStatus)
            : capabilityReason;
        return new PresentationVideoExportHandoffHostCapabilities(
            hostName,
            canEncodeMp4,
            canCaptureNarration,
            canCaptureCameraAndMedia,
            unavailableReason,
            canMuxTimedCaptions);
    }

    public static string BuildVideoExportStatusText(
        bool succeeded,
        bool cancelled,
        int narrationTrackCount = 0,
        int cameraTrackCount = 0,
        int captionTrackCount = 0) =>
        cancelled
            ? Resolve(PresentationShellTextCatalog.VideoExportCancelledStatus)
            : !succeeded
                ? Resolve(PresentationShellTextCatalog.VideoExportFailedStatus)
                : narrationTrackCount == 0 && cameraTrackCount == 0 && captionTrackCount == 0
                    ? Resolve(PresentationShellTextCatalog.VideoExportCompletedVideoOnlyStatus)
                    : Resolve(PresentationShellTextCatalog.VideoExportCompletedWithTracksStatus(
                        narrationTrackCount,
                        cameraTrackCount,
                        captionTrackCount));

    public static PresentationNativeCommandResult BuildVideoExportCommandResult(
        bool succeeded,
        bool cancelled,
        string? failureReason,
        int narrationTrackCount,
        int cameraTrackCount,
        int captionTrackCount)
    {
        var statusText = BuildVideoExportStatusText(
            succeeded,
            cancelled,
            narrationTrackCount,
            cameraTrackCount,
            captionTrackCount);
        return succeeded
            ? PresentationNativeCommandResult.Success(statusText)
            : cancelled
                ? PresentationNativeCommandResult.Cancel(statusText)
                : PresentationNativeCommandResult.Failure(
                    statusText,
                    failureReason ?? PresentationFileTextResources.VideoExportFailed);
    }

    public static PresentationFileCommandFeedbackPlan BuildFileFeedback(
        PresentationFileCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Cancelled)
            return new(null, null, false, null, null);

        var error = result.Error;

        if (result.Succeeded)
        {
            return new(
                string.IsNullOrWhiteSpace(result.Message) ? null : result.Message,
                error,
                false,
                null,
                null);
        }

        if (result.Status == PresentationFileCommandStatus.Unavailable ||
            result.Status == PresentationFileCommandStatus.Invalid && result.Path is null)
        {
            var status = result.Message ?? Resolve(
                PresentationShellTextCatalog.PresentationCommandUnavailableStatus);
            return new(
                status,
                error,
                false,
                result.Status == PresentationFileCommandStatus.Unavailable
                    ? Resolve(PresentationShellTextCatalog.PresentationCommandUnavailableDialogTitle)
                    : null,
                result.Status == PresentationFileCommandStatus.Unavailable ? status : null);
        }

        var message = error?.Exception.Message ??
            result.Message ??
            Resolve(PresentationShellTextCatalog.PresentationCommandFailureFallback);
        var showAvaloniaFileError = error is not null &&
            result.Command is PresentationFileCommand.Open or
                PresentationFileCommand.Save or
                PresentationFileCommand.SaveAs;
        return new(
            SisterAppFileTextPlanner.FormatCommandFailed(
                PresentationFileTextResources.Presentation,
                CommandText(result.Command),
                message),
            error,
            showAvaloniaFileError,
            null,
            null);
    }

    private static string PrintStatusText(PresentationNativePrintPortResult result) =>
        (result.StatusProfile, result.Outcome) switch
        {
            (PresentationNativePrintStatusProfile.PresentationDialog, PresentationNativePrintOutcome.Succeeded) =>
                Resolve(PresentationShellTextCatalog.PrintDialogSucceededStatus),
            (PresentationNativePrintStatusProfile.PresentationDialog, PresentationNativePrintOutcome.Cancelled) =>
                Resolve(PresentationShellTextCatalog.PrintDialogCancelledStatus),
            (PresentationNativePrintStatusProfile.PresentationDialog, PresentationNativePrintOutcome.Failed) =>
                Resolve(PresentationShellTextCatalog.PrintDialogFailedStatus),
            (PresentationNativePrintStatusProfile.SystemPrintHandoff, PresentationNativePrintOutcome.Succeeded) =>
                Resolve(PresentationShellTextCatalog.SystemPrintHandoffSucceededStatus),
            (PresentationNativePrintStatusProfile.SystemPrintHandoffWithCompletedPeriod,
                PresentationNativePrintOutcome.Succeeded) =>
                Resolve(PresentationShellTextCatalog.SystemPrintHandoffSucceededWithPeriodStatus),
            (_, PresentationNativePrintOutcome.Cancelled) =>
                Resolve(PresentationShellTextCatalog.SystemPrintHandoffCancelledStatus),
            (_, PresentationNativePrintOutcome.Failed) =>
                Resolve(PresentationShellTextCatalog.SystemPrintHandoffFailedStatus),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unsupported print outcome profile."),
        };

    private static string Resolve(Free.Shared.Localization.LocalizedTextDescriptor descriptor) =>
        PresentationShellTextCatalog.Resolve(descriptor);
}
