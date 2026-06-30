using FreeX.Core.Commands;
using FreeX.Core.Model;
using SharedAdvancedFilterOutputMode = FreeX.App.Presentation.Filtering.AdvancedFilterOutputMode;
using SharedAdvancedFilterPlanError = FreeX.App.Presentation.Filtering.AdvancedFilterPlanError;
using SharedAdvancedFilterPlanner = FreeX.App.Presentation.Filtering.AdvancedFilterPlanner;
using SharedAdvancedFilterPlanResult = FreeX.App.Presentation.Filtering.AdvancedFilterPlanResult;
using SharedAdvancedFilterRangeSelectionTarget = FreeX.App.Presentation.Filtering.AdvancedFilterRangeSelectionTarget;

namespace FreeX.App.Services;

public enum AdvancedFilterOutputMode
{
    FilterInPlace,
    CopyToAnotherLocation
}

public enum AdvancedFilterRangeSelectionTarget
{
    ListRange,
    CriteriaRange,
    CopyTo
}

public enum AdvancedFilterPlanError
{
    None,
    InvalidListRange,
    ListRangeRequiresDataRows,
    InvalidCriteriaRange,
    CriteriaRangeRequiresCriteriaRows,
    ListRangeTooLarge,
    CriteriaRangeTooLarge,
    CopyDestinationRequired,
    InvalidCopyDestinationRange,
    CopyDestinationRangeTooLarge,
    CopyDestinationMustBeOnListSheet
}

public sealed record AdvancedFilterPlan(
    GridRange ListRange,
    GridRange CriteriaRange,
    AdvancedFilterOutputMode OutputMode,
    bool UniqueRecordsOnly,
    GridRange? CopyToRange = null)
{
    public CellAddress? CopyToCell => CopyToRange?.Start;

    public bool HasCopyDestination => CopyToRange is not null;

    public AdvancedFilterCommand CreateCommand() =>
        new(ListRange, CriteriaRange, CopyToCell, UniqueRecordsOnly, CopyToRange);
}

public sealed record AdvancedFilterPlanResult(
    AdvancedFilterPlan? Plan,
    AdvancedFilterPlanError Error,
    string InvalidText)
{
    public bool Success => Error == AdvancedFilterPlanError.None;

    public static AdvancedFilterPlanResult Valid(AdvancedFilterPlan plan) =>
        new(plan, AdvancedFilterPlanError.None, "");

    public static AdvancedFilterPlanResult Invalid(
        AdvancedFilterPlanError error,
        string invalidText = "") =>
        new(null, error, invalidText);
}

public sealed record AdvancedFilterRangeSelectionRequest(
    AdvancedFilterRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = true);

public static class AdvancedFilterPlanner
{
    public static GridRange CreateDefaultListRange(Sheet sheet, GridRange selectedRange) =>
        SharedAdvancedFilterPlanner.CreateDefaultListRange(sheet, selectedRange);

    public static AdvancedFilterPlanResult CreatePlan(
        SheetId currentSheetId,
        string? listRangeText,
        string? criteriaRangeText,
        string? copyToRangeText,
        bool copyToAnotherLocation,
        bool uniqueRecordsOnly,
        Func<string, SheetId?>? resolveSheetId = null) =>
        CreatePlan(
            currentSheetId,
            listRangeText,
            criteriaRangeText,
            copyToRangeText,
            copyToAnotherLocation ? AdvancedFilterOutputMode.CopyToAnotherLocation : AdvancedFilterOutputMode.FilterInPlace,
            uniqueRecordsOnly,
            resolveSheetId);

    public static AdvancedFilterPlanResult CreatePlan(
        SheetId currentSheetId,
        string? listRangeText,
        string? criteriaRangeText,
        string? copyToRangeText,
        AdvancedFilterOutputMode outputMode,
        bool uniqueRecordsOnly,
        Func<string, SheetId?>? resolveSheetId = null) =>
        ToServicesPlanResult(SharedAdvancedFilterPlanner.CreatePlan(
            currentSheetId,
            listRangeText,
            criteriaRangeText,
            copyToRangeText,
            ToSharedOutputMode(outputMode),
            uniqueRecordsOnly,
            resolveSheetId));

    public static bool TryCreatePlan(
        SheetId currentSheetId,
        string? listRangeText,
        string? criteriaRangeText,
        string? copyToRangeText,
        AdvancedFilterOutputMode outputMode,
        bool uniqueRecordsOnly,
        out AdvancedFilterPlan plan,
        out AdvancedFilterPlanResult result,
        Func<string, SheetId?>? resolveSheetId = null)
    {
        result = CreatePlan(
            currentSheetId,
            listRangeText,
            criteriaRangeText,
            copyToRangeText,
            outputMode,
            uniqueRecordsOnly,
            resolveSheetId);

        if (result.Plan is { } parsedPlan)
        {
            plan = parsedPlan;
            return true;
        }

        plan = default!;
        return false;
    }

    public static bool TryParseRange(
        SheetId defaultSheetId,
        string? input,
        Func<string, SheetId?>? resolveSheetId,
        out GridRange range) =>
        SharedAdvancedFilterPlanner.TryParseRange(defaultSheetId, input, resolveSheetId, out range);

    public static bool TryParseCopyDestination(
        string? input,
        SheetId sheetId,
        out CellAddress? destination) =>
        SharedAdvancedFilterPlanner.TryParseCopyDestination(input, sheetId, out destination);

    public static bool TryParseCopyDestinationRange(
        string? input,
        SheetId sheetId,
        out GridRange? destination) =>
        SharedAdvancedFilterPlanner.TryParseCopyDestinationRange(input, sheetId, out destination);

    public static bool ParseUniqueRecordsOnly(string? input) =>
        SharedAdvancedFilterPlanner.ParseUniqueRecordsOnly(input);

    public static AdvancedFilterRangeSelectionRequest CreateRangeSelectionRequest(
        AdvancedFilterRangeSelectionTarget target,
        string? currentText)
    {
        var request = SharedAdvancedFilterPlanner.CreateRangeSelectionRequest(
            ToSharedRangeSelectionTarget(target),
            currentText);

        return new(
            ToServicesRangeSelectionTarget(request.Target),
            request.CurrentText,
            request.CollapseDialog);
    }

    private static AdvancedFilterPlanResult ToServicesPlanResult(SharedAdvancedFilterPlanResult result) =>
        result.Plan is { } plan
            ? AdvancedFilterPlanResult.Valid(ToServicesPlan(plan))
            : AdvancedFilterPlanResult.Invalid(ToServicesPlanError(result.Error), result.InvalidText);

    private static AdvancedFilterPlan ToServicesPlan(FreeX.App.Presentation.Filtering.AdvancedFilterPlan plan) =>
        new(
            plan.ListRange,
            plan.CriteriaRange,
            ToServicesOutputMode(plan.OutputMode),
            plan.UniqueRecordsOnly,
            plan.CopyToRange);

    private static SharedAdvancedFilterOutputMode ToSharedOutputMode(AdvancedFilterOutputMode outputMode) =>
        outputMode switch
        {
            AdvancedFilterOutputMode.FilterInPlace => SharedAdvancedFilterOutputMode.FilterInPlace,
            AdvancedFilterOutputMode.CopyToAnotherLocation => SharedAdvancedFilterOutputMode.CopyToAnotherLocation,
            _ => throw new ArgumentOutOfRangeException(nameof(outputMode), outputMode, "Unknown Advanced Filter output mode.")
        };

    private static AdvancedFilterOutputMode ToServicesOutputMode(SharedAdvancedFilterOutputMode outputMode) =>
        outputMode switch
        {
            SharedAdvancedFilterOutputMode.CopyToAnotherLocation => AdvancedFilterOutputMode.CopyToAnotherLocation,
            _ => AdvancedFilterOutputMode.FilterInPlace
        };

    private static AdvancedFilterPlanError ToServicesPlanError(SharedAdvancedFilterPlanError error) =>
        error switch
        {
            SharedAdvancedFilterPlanError.InvalidListRange => AdvancedFilterPlanError.InvalidListRange,
            SharedAdvancedFilterPlanError.ListRangeRequiresDataRows => AdvancedFilterPlanError.ListRangeRequiresDataRows,
            SharedAdvancedFilterPlanError.InvalidCriteriaRange => AdvancedFilterPlanError.InvalidCriteriaRange,
            SharedAdvancedFilterPlanError.CriteriaRangeRequiresCriteriaRows => AdvancedFilterPlanError.CriteriaRangeRequiresCriteriaRows,
            SharedAdvancedFilterPlanError.ListRangeTooLarge => AdvancedFilterPlanError.ListRangeTooLarge,
            SharedAdvancedFilterPlanError.CriteriaRangeTooLarge => AdvancedFilterPlanError.CriteriaRangeTooLarge,
            SharedAdvancedFilterPlanError.CopyDestinationRequired => AdvancedFilterPlanError.CopyDestinationRequired,
            SharedAdvancedFilterPlanError.InvalidCopyDestinationRange => AdvancedFilterPlanError.InvalidCopyDestinationRange,
            SharedAdvancedFilterPlanError.CopyDestinationRangeTooLarge => AdvancedFilterPlanError.CopyDestinationRangeTooLarge,
            SharedAdvancedFilterPlanError.CopyDestinationMustBeOnListSheet => AdvancedFilterPlanError.CopyDestinationMustBeOnListSheet,
            _ => AdvancedFilterPlanError.None
        };

    private static SharedAdvancedFilterRangeSelectionTarget ToSharedRangeSelectionTarget(
        AdvancedFilterRangeSelectionTarget target) =>
        target switch
        {
            AdvancedFilterRangeSelectionTarget.CriteriaRange => SharedAdvancedFilterRangeSelectionTarget.CriteriaRange,
            AdvancedFilterRangeSelectionTarget.CopyTo => SharedAdvancedFilterRangeSelectionTarget.CopyTo,
            _ => SharedAdvancedFilterRangeSelectionTarget.ListRange
        };

    private static AdvancedFilterRangeSelectionTarget ToServicesRangeSelectionTarget(
        SharedAdvancedFilterRangeSelectionTarget target) =>
        target switch
        {
            SharedAdvancedFilterRangeSelectionTarget.CriteriaRange => AdvancedFilterRangeSelectionTarget.CriteriaRange,
            SharedAdvancedFilterRangeSelectionTarget.CopyTo => AdvancedFilterRangeSelectionTarget.CopyTo,
            _ => AdvancedFilterRangeSelectionTarget.ListRange
        };
}
