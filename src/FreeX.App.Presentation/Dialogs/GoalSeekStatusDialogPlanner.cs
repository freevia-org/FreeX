using System.Globalization;
using Free.Shared.Localization;
using FreeX.App.Services;

namespace FreeX.App.Presentation.Dialogs;

public enum GoalSeekPresentationProfile
{
    Wpf,
    Avalonia
}

public enum GoalSeekValidationFocusTarget
{
    SetCell,
    TargetValue,
    ChangingCell
}

/// <summary>
/// Shared geometry for the compact Goal Seek Status dialog. WPF remains the visual authority;
/// Avalonia consumes these values so the status workflow keeps one cross-shell layout contract.
/// </summary>
public static class GoalSeekStatusDialogPlanner
{
    public const double WindowWidth = 380;
    public const double ConvergedWindowHeight = 190;
    public const double NotConvergedWindowHeight = 170;
    public const double ContentMargin = 16;
    public const double SummaryLineHeight = 32;
    public const double SummaryTopCompensation = -5;
    public const double SummaryBottomMargin = 5;
    public const double ButtonHeight = 20;
    public const double ButtonGap = 8;
    public const double KeepResultButtonWidth = 104;
    public const double RestoreOriginalValuesButtonWidth = 152;
    public const double OkButtonWidth = 76;

    public static double WindowHeight(bool converged) =>
        converged ? ConvergedWindowHeight : NotConvergedWindowHeight;

    public static ValidationPresentationDescriptor<GoalSeekValidationFocusTarget> DescribeValidationError(
        GoalSeekRequestParseResult result,
        GoalSeekPresentationProfile profile) =>
        profile == GoalSeekPresentationProfile.Wpf
            ? DescribeWpfValidationError(result)
            : DescribeAvaloniaValidationError(result);

    public static LocalizedTextDescriptor DescribeStatus(
        bool converged,
        double targetValue,
        double actualResult,
        double foundValue,
        GoalSeekPresentationProfile profile)
    {
        var format = profile == GoalSeekPresentationProfile.Wpf ? "G10" : "G12";
        var culture = profile == GoalSeekPresentationProfile.Wpf
            ? CultureInfo.InvariantCulture
            : CultureInfo.CurrentCulture;
        return LocalizedTextDescriptor.Resource(
            converged ? "GoalSeekStatus_SuccessSummary" : "GoalSeekStatus_FailureSummary",
            targetValue.ToString(format, culture),
            actualResult.ToString(format, culture),
            foundValue.ToString(format, culture));
    }

    public static LocalizedTextDescriptor DescribeExecutionFailure(
        WorkbookGoalSeekStatus status,
        string? errorMessage,
        string setCellReference,
        string changingCellReference) =>
        !string.IsNullOrWhiteSpace(errorMessage)
            ? LocalizedTextDescriptor.Literal(errorMessage)
            : status switch
            {
                WorkbookGoalSeekStatus.InvalidRequest => LocalizedTextDescriptor.Resource(
                    "GoalSeek_InvalidRequestFormat",
                    setCellReference),
                WorkbookGoalSeekStatus.ApplyFailed => LocalizedTextDescriptor.Resource(
                    "GoalSeek_ResultCouldNotBeAppliedFormat",
                    changingCellReference),
                _ => LocalizedTextDescriptor.Resource("GoalSeek_CouldNotComplete")
            };

    private static ValidationPresentationDescriptor<GoalSeekValidationFocusTarget> DescribeWpfValidationError(
        GoalSeekRequestParseResult result) =>
        result.Error switch
        {
            GoalSeekRequestParseError.SetCellRequired => new(
                LocalizedTextDescriptor.Resource("GoalSeek_SetCellRequiredMessage"),
                GoalSeekValidationFocusTarget.SetCell),
            GoalSeekRequestParseError.InvalidSetCellAddress => new(
                LocalizedTextDescriptor.Resource("GoalSeek_InvalidCellAddressMessage", result.InvalidText),
                GoalSeekValidationFocusTarget.SetCell),
            GoalSeekRequestParseError.InvalidTargetValue => new(
                LocalizedTextDescriptor.Resource("GoalSeek_InvalidNumberMessage", result.InvalidText),
                GoalSeekValidationFocusTarget.TargetValue),
            GoalSeekRequestParseError.ChangingCellRequired => new(
                LocalizedTextDescriptor.Resource("GoalSeek_ByChangingCellRequiredMessage"),
                GoalSeekValidationFocusTarget.ChangingCell),
            GoalSeekRequestParseError.InvalidChangingCellAddress => new(
                LocalizedTextDescriptor.Resource("GoalSeek_InvalidCellAddressMessage", result.InvalidText),
                GoalSeekValidationFocusTarget.ChangingCell),
            GoalSeekRequestParseError.CellsMustDiffer => new(
                LocalizedTextDescriptor.Resource("GoalSeek_CellsMustDifferMessage"),
                GoalSeekValidationFocusTarget.ChangingCell),
            _ => new(
                LocalizedTextDescriptor.Resource("GoalSeek_RequestInvalid"),
                GoalSeekValidationFocusTarget.SetCell)
        };

    private static ValidationPresentationDescriptor<GoalSeekValidationFocusTarget> DescribeAvaloniaValidationError(
        GoalSeekRequestParseResult result) =>
        result.Error switch
        {
            GoalSeekRequestParseError.SetCellRequired => new(
                LocalizedTextDescriptor.Resource("GoalSeek_SetCellRequiredMessage"),
                GoalSeekValidationFocusTarget.SetCell),
            GoalSeekRequestParseError.InvalidSetCellAddress => new(
                LocalizedTextDescriptor.Resource("GoalSeek_InvalidCellAddressMessage", result.InvalidText),
                GoalSeekValidationFocusTarget.SetCell),
            GoalSeekRequestParseError.InvalidTargetValue => new(
                LocalizedTextDescriptor.Resource("GoalSeek_InvalidNumberMessage", result.InvalidText),
                GoalSeekValidationFocusTarget.TargetValue),
            GoalSeekRequestParseError.ChangingCellRequired => new(
                LocalizedTextDescriptor.Resource("GoalSeek_ByChangingCellRequiredMessage"),
                GoalSeekValidationFocusTarget.ChangingCell),
            GoalSeekRequestParseError.InvalidChangingCellAddress => new(
                LocalizedTextDescriptor.Resource("GoalSeek_InvalidCellAddressMessage", result.InvalidText),
                GoalSeekValidationFocusTarget.ChangingCell),
            GoalSeekRequestParseError.CellsMustDiffer => new(
                LocalizedTextDescriptor.Resource("GoalSeek_CellsMustDifferMessage"),
                GoalSeekValidationFocusTarget.ChangingCell),
            _ => new(
                LocalizedTextDescriptor.Resource("GoalSeek_RequestInvalid"),
                GoalSeekValidationFocusTarget.SetCell)
        };
}
