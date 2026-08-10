using FreeX.Core.Commands;
using FreeX.Core.Model;
using AdvancedFilterDialogResult = FreeX.App.Presentation.Filtering.AdvancedFilterDialogResult;
using AdvancedFilterRangeSelectionRequest = FreeX.App.Presentation.Filtering.AdvancedFilterRangeSelectionRequest;
using AdvancedFilterRangeSelectionTarget = FreeX.App.Presentation.Filtering.AdvancedFilterRangeSelectionTarget;
using SharedAdvancedFilterOutputMode = FreeX.App.Presentation.Filtering.AdvancedFilterOutputMode;
using SharedAdvancedFilterPlanError = FreeX.App.Presentation.Filtering.AdvancedFilterPlanError;
using SharedAdvancedFilterPlanner = FreeX.App.Presentation.Filtering.AdvancedFilterPlanner;
using SharedAdvancedFilterPlanResult = FreeX.App.Presentation.Filtering.AdvancedFilterPlanResult;

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
            ? SharedAdvancedFilterOutputMode.FilterInPlace
            : SharedAdvancedFilterOutputMode.CopyToAnotherLocation;

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
                ? SharedAdvancedFilterOutputMode.CopyToAnotherLocation
                : SharedAdvancedFilterOutputMode.FilterInPlace,
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
                ? SharedAdvancedFilterOutputMode.CopyToAnotherLocation
                : SharedAdvancedFilterOutputMode.FilterInPlace,
            uniqueRecordsOnly,
            resolveSheetId: null,
            out result,
            out error);

    public static AdvancedFilterRangeSelectionRequest CreateRangeSelectionRequest(
        AdvancedFilterRangeSelectionTarget target,
        string currentText) =>
        SharedAdvancedFilterPlanner.CreateRangeSelectionRequest(target, currentText);

    private static SharedAdvancedFilterPlanResult CreateAdvancedFilterPlan(
        SheetId currentSheetId,
        string listRangeText,
        string criteriaRangeText,
        string? copyToCellText,
        SharedAdvancedFilterOutputMode outputMode,
        bool uniqueRecordsOnly,
        Func<string, SheetId?>? resolveSheetId) =>
        SharedAdvancedFilterPlanner.CreatePlan(
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
        SharedAdvancedFilterOutputMode outputMode,
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
        SharedAdvancedFilterPlanResult planResult,
        out AdvancedFilterDialogResult result,
        out string? error)
    {
        if (!SharedAdvancedFilterPlanner.TryCreateDialogResult(planResult, out result))
        {
            error = SharedAdvancedFilterPlanner
                .DescribeError(planResult)
                .Message
                .Resolve(UiText.Get, UiText.Format);
            return false;
        }

        error = null;
        return true;
    }

}
