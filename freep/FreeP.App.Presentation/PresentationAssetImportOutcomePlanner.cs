using Free.Shared.AppServices;

namespace FreeP.App.Compositor;

public enum PresentationAssetImportFailureSurface
{
    Status,
    ModalError,
    None,
}

public enum PresentationAssetImportFailureTextProfile
{
    CommandFailed,
    SmartArtPicture,
}

public sealed record PresentationAssetImportOutcomePolicy(
    bool ShowInsertedStatus = false,
    string? SuccessStatusText = null,
    PresentationAssetImportFailureSurface FailureSurface =
        PresentationAssetImportFailureSurface.Status,
    PresentationAssetImportFailureTextProfile FailureTextProfile =
        PresentationAssetImportFailureTextProfile.CommandFailed)
{
    public static PresentationAssetImportOutcomePolicy ModalError { get; } =
        new(FailureSurface: PresentationAssetImportFailureSurface.ModalError);

    public static PresentationAssetImportOutcomePolicy SmartArtPane { get; } =
        new(FailureTextProfile: PresentationAssetImportFailureTextProfile.SmartArtPicture);
}

public sealed record PresentationAssetImportOutcomePresentation(
    string? StatusText = null,
    UserMessageRequest? Message = null)
{
    public static PresentationAssetImportOutcomePresentation Empty { get; } = new();
}

/// <summary>
/// Maps portable asset-import outcomes to renderer-neutral status or modal feedback.
/// Native hosts retain ownership of status controls, message services, and modality.
/// </summary>
public static class PresentationAssetImportOutcomePlanner
{
    public static PresentationAssetImportOutcomePresentation Plan(
        PresentationAssetImportResult result,
        SisterAppFileTextSpec fileText,
        PresentationAssetImportOutcomePolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(fileText);

        policy ??= new PresentationAssetImportOutcomePolicy();
        return result.Status switch
        {
            PresentationAssetImportStatus.Succeeded => PlanSuccess(result, fileText, policy),
            PresentationAssetImportStatus.Unavailable => PlanUnavailable(result, fileText, policy),
            PresentationAssetImportStatus.Failed => PlanFailure(result, fileText, policy),
            PresentationAssetImportStatus.Cancelled or PresentationAssetImportStatus.NotApplied =>
                PresentationAssetImportOutcomePresentation.Empty,
            _ => throw new ArgumentOutOfRangeException(
                nameof(result),
                result.Status,
                "Unknown presentation asset import status."),
        };
    }

    private static PresentationAssetImportOutcomePresentation PlanSuccess(
        PresentationAssetImportResult result,
        SisterAppFileTextSpec fileText,
        PresentationAssetImportOutcomePolicy policy)
    {
        if (policy.SuccessStatusText is not null)
            return new PresentationAssetImportOutcomePresentation(policy.SuccessStatusText);

        return policy.ShowInsertedStatus && result.SourceName is not null
            ? new PresentationAssetImportOutcomePresentation(
                SisterAppFileTextPlanner.FormatInserted(fileText, result.SourceName))
            : PresentationAssetImportOutcomePresentation.Empty;
    }

    private static PresentationAssetImportOutcomePresentation PlanUnavailable(
        PresentationAssetImportResult result,
        SisterAppFileTextSpec fileText,
        PresentationAssetImportOutcomePolicy policy) =>
        policy.FailureSurface == PresentationAssetImportFailureSurface.Status
            ? new PresentationAssetImportOutcomePresentation(
                SisterAppFileTextPlanner.FormatCommandUnavailable(
                    fileText,
                    result.Request.CommandName))
            : PresentationAssetImportOutcomePresentation.Empty;

    private static PresentationAssetImportOutcomePresentation PlanFailure(
        PresentationAssetImportResult result,
        SisterAppFileTextSpec fileText,
        PresentationAssetImportOutcomePolicy policy) =>
        policy.FailureSurface switch
        {
            PresentationAssetImportFailureSurface.Status =>
                new PresentationAssetImportOutcomePresentation(
                    BuildFailureStatus(result, fileText, policy.FailureTextProfile)),
            PresentationAssetImportFailureSurface.ModalError =>
                new PresentationAssetImportOutcomePresentation(
                    Message: new UserMessageRequest(
                        result.Message,
                        result.Request.CommandName,
                        UserMessageButtons.Ok,
                        UserMessageIcon.Error)),
            PresentationAssetImportFailureSurface.None =>
                PresentationAssetImportOutcomePresentation.Empty,
            _ => throw new ArgumentOutOfRangeException(
                nameof(policy),
                policy.FailureSurface,
                "Unknown presentation asset import failure surface."),
        };

    private static string BuildFailureStatus(
        PresentationAssetImportResult result,
        SisterAppFileTextSpec fileText,
        PresentationAssetImportFailureTextProfile profile) =>
        profile switch
        {
            PresentationAssetImportFailureTextProfile.CommandFailed =>
                SisterAppFileTextPlanner.FormatCommandFailed(
                    fileText,
                    result.Request.CommandName,
                    result.Message ?? string.Empty),
            PresentationAssetImportFailureTextProfile.SmartArtPicture =>
                PresentationShellTextCatalog.Resolve(
                    PresentationShellTextCatalog.SmartArtPictureFailureStatus(
                        result.Message ?? string.Empty)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(profile),
                profile,
                "Unknown presentation asset import failure text profile."),
        };
}
