using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public sealed record PageLayoutCommandStatusPlan(
    string SuccessResourceKey,
    string FailureResourceKey);

public sealed record PageLayoutViewModeStatusPlan(
    WorksheetViewMode TargetViewMode,
    PageLayoutCommandStatusPlan Status);

/// <summary>
/// Shared status text policy for Page Layout actions. UI shells still resolve resources and render the
/// result, while this planner owns the success/failure keys and the command-result adoption rule.
/// </summary>
public static class PageLayoutStatusPlanner
{
    public const string PageSetupUpdatedResourceKey = "ShellLoc_PageSetupUpdated";
    public const string PageSetupFailedResourceKey = "ShellLoc_PageSetupFailed";
    public const string PageSetupInvalidResourceKey = "ShellLoc_PageSetupInvalid";
    public const string PageBreakPreviewOnResourceKey = "ShellLoc_PageBreakPreviewOn";
    public const string PageBreakPreviewOffResourceKey = "ShellLoc_PageBreakPreviewOff";
    public const string PrintAreaSetResourceKey = "RibbonWire_PrintAreaSet";
    public const string PrintAreaSetFailedResourceKey = "RibbonWire_PrintAreaSetFailed";
    public const string PrintAreaClearedResourceKey = "RibbonWire_PrintAreaCleared";
    public const string PrintAreaClearFailedResourceKey = "RibbonWire_PrintAreaClearFailed";
    public const string BackgroundSetResourceKey = "RibbonWire_BackgroundSet";
    public const string BackgroundClearedResourceKey = "RibbonWire_BackgroundDeleted";
    public const string PageBreakFailedResourceKey = "PageBreak_Failed";
    public const string PrintOptionsFailedResourceKey = "ShellLoc_CouldNotUpdatePrintOptions";

    public static PageLayoutCommandStatusPlan PageSetupSubmission { get; } =
        new(PageSetupUpdatedResourceKey, PageSetupFailedResourceKey);

    public static PageLayoutCommandStatusPlan PrintAreaSet { get; } =
        new(PrintAreaSetResourceKey, PrintAreaSetFailedResourceKey);

    public static PageLayoutCommandStatusPlan PrintAreaClear { get; } =
        new(PrintAreaClearedResourceKey, PrintAreaClearFailedResourceKey);

    public static PageLayoutCommandStatusPlan BackgroundSet { get; } =
        new(BackgroundSetResourceKey, BackgroundSetResourceKey);

    public static PageLayoutCommandStatusPlan BackgroundClear { get; } =
        new(BackgroundClearedResourceKey, BackgroundClearedResourceKey);

    public static PageLayoutCommandStatusPlan PageBreaks { get; } =
        new(PageBreakFailedResourceKey, PageBreakFailedResourceKey);

    public static PageLayoutCommandStatusPlan PrintOptions { get; } =
        new(PrintOptionsFailedResourceKey, PrintOptionsFailedResourceKey);

    public static PageLayoutCommandStatusPlan ForPreset<T>(PageLayoutPresetCommandPlan<T> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new PageLayoutCommandStatusPlan(plan.StatusResourceKey, plan.StatusResourceKey);
    }

    public static PageLayoutViewModeStatusPlan PlanPageBreakPreviewToggle(WorksheetViewMode currentViewMode)
    {
        var targetViewMode = currentViewMode == WorksheetViewMode.PageBreakPreview
            ? WorksheetViewMode.Normal
            : WorksheetViewMode.PageBreakPreview;
        var successKey = targetViewMode == WorksheetViewMode.PageBreakPreview
            ? PageBreakPreviewOnResourceKey
            : PageBreakPreviewOffResourceKey;

        return new PageLayoutViewModeStatusPlan(
            targetViewMode,
            new PageLayoutCommandStatusPlan(successKey, PageBreakPreviewOffResourceKey));
    }

    public static string ResolveCommandStatus(
        PageLayoutCommandStatusPlan plan,
        bool success,
        string? errorMessage,
        Func<string, string> textResolver)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(textResolver);

        if (success)
            return textResolver(plan.SuccessResourceKey);

        return string.IsNullOrWhiteSpace(errorMessage)
            ? textResolver(plan.FailureResourceKey)
            : errorMessage!;
    }

    public static string ResolveCommandStatus(
        PageLayoutCommandExecutionPlan plan,
        bool success,
        string? errorMessage,
        Func<string, string> textResolver)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(textResolver);

        if (success)
        {
            if (!string.IsNullOrWhiteSpace(plan.SuccessStatusText))
                return plan.SuccessStatusText!;

            return plan.Status is not null
                ? textResolver(plan.Status.SuccessResourceKey)
                : string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
            return errorMessage!;
        if (!string.IsNullOrWhiteSpace(plan.FailureStatusText))
            return plan.FailureStatusText!;

        return plan.Status is not null
            ? textResolver(plan.Status.FailureResourceKey)
            : string.Empty;
    }

    public static string ResolvePageSetupValidationIssue(
        PageSetupSubmissionValidation validation,
        Func<string, string> textResolver)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(textResolver);

        return validation.Message.Resolve(textResolver, PageSetupInvalidResourceKey);
    }
}
