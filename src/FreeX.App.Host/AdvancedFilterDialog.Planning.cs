using FreeX.Core.Commands;
using FreeX.Core.Model;
using ServicesAdvancedFilterOutputMode = FreeX.App.Services.AdvancedFilterOutputMode;
using ServicesAdvancedFilterPlanError = FreeX.App.Services.AdvancedFilterPlanError;
using ServicesAdvancedFilterPlanner = FreeX.App.Services.AdvancedFilterPlanner;
using ServicesAdvancedFilterPlanResult = FreeX.App.Services.AdvancedFilterPlanResult;

namespace FreeX.App.Host;

public sealed partial class AdvancedFilterDialog
{
    public static bool TryParse(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        bool uniqueRecordsOnly,
        Func<string, SheetId?>? resolveSheetId,
        out AdvancedFilterDialogResult result,
        out string? error)
    {
        var outputMode = string.IsNullOrWhiteSpace(copyToCellText)
            ? ServicesAdvancedFilterOutputMode.FilterInPlace
            : ServicesAdvancedFilterOutputMode.CopyToAnotherLocation;

        return TryParse(
            currentSheetId,
            listRangeText,
            criteriaRangeText,
            copyToCellText,
            outputMode,
            uniqueRecordsOnly,
            resolveSheetId,
            out result,
            out error);
    }

    public static bool TryParse(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        bool uniqueRecordsOnly,
        out AdvancedFilterDialogResult result,
        out string? error) =>
        TryParse(
            currentSheetId,
            listRangeText,
            criteriaRangeText,
            copyToCellText,
            uniqueRecordsOnly,
            resolveSheetId: null,
            out result,
            out error);

    public static bool TryParse(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        bool copyToAnotherLocation,
        bool uniqueRecordsOnly,
        Func<string, SheetId?>? resolveSheetId,
        out AdvancedFilterDialogResult result,
        out string? error) =>
        TryParse(
            currentSheetId,
            listRangeText,
            criteriaRangeText,
            copyToCellText,
            copyToAnotherLocation
                ? ServicesAdvancedFilterOutputMode.CopyToAnotherLocation
                : ServicesAdvancedFilterOutputMode.FilterInPlace,
            uniqueRecordsOnly,
            resolveSheetId,
            out result,
            out error);

    public static bool TryParse(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        bool copyToAnotherLocation,
        bool uniqueRecordsOnly,
        out AdvancedFilterDialogResult result,
        out string? error) =>
        TryParse(
            currentSheetId,
            listRangeText,
            criteriaRangeText,
            copyToCellText,
            copyToAnotherLocation
                ? ServicesAdvancedFilterOutputMode.CopyToAnotherLocation
                : ServicesAdvancedFilterOutputMode.FilterInPlace,
            uniqueRecordsOnly,
            resolveSheetId: null,
            out result,
            out error);

    public static AdvancedFilterRangeSelectionRequest CreateRangeSelectionRequest(
        AdvancedFilterRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);

    private static ServicesAdvancedFilterPlanResult CreateAdvancedFilterPlan(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        ServicesAdvancedFilterOutputMode outputMode,
        bool uniqueRecordsOnly,
        Func<string, SheetId?>? resolveSheetId) =>
        ServicesAdvancedFilterPlanner.CreatePlan(
            currentSheetId,
            listRangeText,
            criteriaRangeText,
            copyToCellText,
            outputMode,
            uniqueRecordsOnly,
            resolveSheetId);

    private static bool TryParse(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        ServicesAdvancedFilterOutputMode outputMode,
        bool uniqueRecordsOnly,
        Func<string, SheetId?>? resolveSheetId,
        out AdvancedFilterDialogResult result,
        out string? error)
    {
        var planResult = CreateAdvancedFilterPlan(
            currentSheetId,
            listRangeText,
            criteriaRangeText,
            copyToCellText,
            outputMode,
            uniqueRecordsOnly,
            resolveSheetId);

        return TryCreateDialogResult(planResult, out result, out error);
    }

    private static bool TryCreateDialogResult(
        ServicesAdvancedFilterPlanResult planResult,
        out AdvancedFilterDialogResult result,
        out string? error)
    {
        if (!planResult.Success || planResult.Plan is null)
        {
            result = default!;
            error = FormatAdvancedFilterPlanError(planResult.Error);
            return false;
        }

        var plan = planResult.Plan;
        result = new AdvancedFilterDialogResult(
            plan.ListRange,
            plan.CriteriaRange,
            plan.CopyToCell,
            plan.UniqueRecordsOnly,
            plan.CopyToRange);
        error = null;
        return true;
    }

    private static string FormatAdvancedFilterPlanError(ServicesAdvancedFilterPlanError error) =>
        error switch
        {
            ServicesAdvancedFilterPlanError.InvalidListRange => UiText.Get("AdvancedFilter_EnterValidListRange"),
            ServicesAdvancedFilterPlanError.ListRangeRequiresDataRows => UiText.Get("AdvancedFilter_ListRangeMustIncludeHeaders"),
            ServicesAdvancedFilterPlanError.ListRangeTooLarge => AdvancedFilterCommand.ListRangeTooLargeMessage,
            ServicesAdvancedFilterPlanError.InvalidCriteriaRange => UiText.Get("AdvancedFilter_EnterValidCriteriaRange"),
            ServicesAdvancedFilterPlanError.CriteriaRangeRequiresCriteriaRows => UiText.Get("AdvancedFilter_CriteriaRangeMustIncludeHeaders"),
            ServicesAdvancedFilterPlanError.CriteriaRangeTooLarge => AdvancedFilterCommand.CriteriaRangeTooLargeMessage,
            ServicesAdvancedFilterPlanError.CopyDestinationRequired or
            ServicesAdvancedFilterPlanError.InvalidCopyDestinationRange => UiText.Get("AdvancedFilter_EnterValidCopyToRange"),
            ServicesAdvancedFilterPlanError.CopyDestinationRangeTooLarge => AdvancedFilterCommand.CopyOutputTooLargeMessage,
            ServicesAdvancedFilterPlanError.CopyDestinationMustBeOnListSheet => UiText.Get("AdvancedFilter_EnterValidCopyToRange"),
            _ => UiText.Get("AdvancedFilter_EnterValidFilterRanges")
        };

    private static bool IsAdvancedFilterCriteriaError(ServicesAdvancedFilterPlanError error) =>
        error is ServicesAdvancedFilterPlanError.InvalidCriteriaRange or
            ServicesAdvancedFilterPlanError.CriteriaRangeRequiresCriteriaRows or
            ServicesAdvancedFilterPlanError.CriteriaRangeTooLarge;

    private static bool IsAdvancedFilterCopyDestinationError(ServicesAdvancedFilterPlanError error) =>
        error is ServicesAdvancedFilterPlanError.CopyDestinationRequired or
            ServicesAdvancedFilterPlanError.InvalidCopyDestinationRange or
            ServicesAdvancedFilterPlanError.CopyDestinationRangeTooLarge or
            ServicesAdvancedFilterPlanError.CopyDestinationMustBeOnListSheet;
}
