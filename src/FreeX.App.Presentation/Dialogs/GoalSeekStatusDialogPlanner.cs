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
        if (profile == GoalSeekPresentationProfile.Wpf)
        {
            var arguments = new object?[]
            {
                targetValue.ToString("G10", CultureInfo.InvariantCulture),
                actualResult.ToString("G10", CultureInfo.InvariantCulture),
                foundValue.ToString("G10", CultureInfo.InvariantCulture)
            };
            return LocalizedTextDescriptor.Resource(
                converged ? "GoalSeekStatus_SuccessSummary" : "GoalSeekStatus_FailureSummary",
                arguments);
        }

        var heading = converged
            ? "Goal Seek found a solution."
            : "Goal Seek could not find a solution.";
        return LocalizedTextDescriptor.Literal(string.Join(
            Environment.NewLine,
            heading,
            $"Target value: {targetValue.ToString("G12", CultureInfo.CurrentCulture)}",
            $"Current value: {actualResult.ToString("G12", CultureInfo.CurrentCulture)}",
            $"Changing cell value: {foundValue.ToString("G12", CultureInfo.CurrentCulture)}"));
    }

    public static LocalizedTextDescriptor DescribeExecutionFailure(
        WorkbookGoalSeekStatus status,
        string? errorMessage,
        string setCellReference,
        string changingCellReference) =>
        status switch
        {
            WorkbookGoalSeekStatus.InvalidRequest => LocalizedTextDescriptor.Literal(
                errorMessage ?? $"Goal Seek request for {setCellReference} is invalid."),
            WorkbookGoalSeekStatus.ApplyFailed => LocalizedTextDescriptor.Literal(
                errorMessage ?? $"Goal Seek result for {changingCellReference} could not be applied."),
            _ => LocalizedTextDescriptor.Literal("Goal Seek could not complete.")
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
            _ => new(LocalizedTextDescriptor.Literal(""), GoalSeekValidationFocusTarget.SetCell)
        };

    private static ValidationPresentationDescriptor<GoalSeekValidationFocusTarget> DescribeAvaloniaValidationError(
        GoalSeekRequestParseResult result) =>
        result.Error switch
        {
            GoalSeekRequestParseError.SetCellRequired => new(
                LocalizedTextDescriptor.Literal("Set cell is required."),
                GoalSeekValidationFocusTarget.SetCell),
            GoalSeekRequestParseError.InvalidSetCellAddress => new(
                LocalizedTextDescriptor.Literal($"Set cell '{result.InvalidText}' is not a valid cell reference."),
                GoalSeekValidationFocusTarget.SetCell),
            GoalSeekRequestParseError.InvalidTargetValue => new(
                LocalizedTextDescriptor.Literal("Target value must be a finite number."),
                GoalSeekValidationFocusTarget.TargetValue),
            GoalSeekRequestParseError.ChangingCellRequired => new(
                LocalizedTextDescriptor.Literal("Changing cell is required."),
                GoalSeekValidationFocusTarget.ChangingCell),
            GoalSeekRequestParseError.InvalidChangingCellAddress => new(
                LocalizedTextDescriptor.Literal($"Changing cell '{result.InvalidText}' is not a valid cell reference."),
                GoalSeekValidationFocusTarget.ChangingCell),
            GoalSeekRequestParseError.CellsMustDiffer => new(
                LocalizedTextDescriptor.Literal("Set cell and changing cell must be different."),
                GoalSeekValidationFocusTarget.ChangingCell),
            _ => new(
                LocalizedTextDescriptor.Literal("Goal Seek request is invalid."),
                GoalSeekValidationFocusTarget.SetCell)
        };
}
