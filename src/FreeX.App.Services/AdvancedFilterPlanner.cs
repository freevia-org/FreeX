using FreeX.Core.Commands;
using FreeX.Core.Model;

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
    public static GridRange CreateDefaultListRange(Sheet sheet, GridRange selectedRange)
    {
        if (selectedRange.RowCount == 1 &&
            selectedRange.ColCount == 1 &&
            SelectionRangeService.GetCurrentRegion(sheet, selectedRange.Start) is { } currentRegion &&
            currentRegion.RowCount > 1)
        {
            return currentRegion;
        }

        if (selectedRange.RowCount == 1 &&
            selectedRange.ColCount == 1 &&
            sheet.GetUsedRange() is { } usedRange &&
            usedRange.RowCount > 1 &&
            usedRange.Contains(selectedRange.Start))
        {
            return usedRange;
        }

        return selectedRange;
    }

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
        Func<string, SheetId?>? resolveSheetId = null)
    {
        if (outputMode is not AdvancedFilterOutputMode.FilterInPlace and
            not AdvancedFilterOutputMode.CopyToAnotherLocation)
            throw new ArgumentOutOfRangeException(nameof(outputMode), outputMode, "Unknown Advanced Filter output mode.");

        resolveSheetId ??= static _ => null;

        if (!TryParseRange(currentSheetId, listRangeText, resolveSheetId, out var listRange))
            return AdvancedFilterPlanResult.Invalid(
                AdvancedFilterPlanError.InvalidListRange,
                NormalizeInput(listRangeText));

        if (listRange.RowCount < 2)
            return AdvancedFilterPlanResult.Invalid(
                AdvancedFilterPlanError.ListRangeRequiresDataRows,
                NormalizeInput(listRangeText));

        if (!AdvancedFilterCommand.IsListRangeWithinSupportedBounds(listRange))
            return AdvancedFilterPlanResult.Invalid(
                AdvancedFilterPlanError.ListRangeTooLarge,
                NormalizeInput(listRangeText));

        if (!TryParseRange(currentSheetId, criteriaRangeText, resolveSheetId, out var criteriaRange))
            return AdvancedFilterPlanResult.Invalid(
                AdvancedFilterPlanError.InvalidCriteriaRange,
                NormalizeInput(criteriaRangeText));

        if (criteriaRange.RowCount < 2)
            return AdvancedFilterPlanResult.Invalid(
                AdvancedFilterPlanError.CriteriaRangeRequiresCriteriaRows,
                NormalizeInput(criteriaRangeText));

        if (!AdvancedFilterCommand.IsCriteriaRangeWithinSupportedBounds(criteriaRange))
            return AdvancedFilterPlanResult.Invalid(
                AdvancedFilterPlanError.CriteriaRangeTooLarge,
                NormalizeInput(criteriaRangeText));

        GridRange? copyToRange = null;
        if (outputMode == AdvancedFilterOutputMode.CopyToAnotherLocation)
        {
            var copyInput = NormalizeInput(copyToRangeText);
            if (copyInput.Length == 0)
                return AdvancedFilterPlanResult.Invalid(AdvancedFilterPlanError.CopyDestinationRequired);

            if (!TryParseCopyDestinationRange(copyInput, currentSheetId, out copyToRange))
                return AdvancedFilterPlanResult.Invalid(
                    AdvancedFilterPlanError.InvalidCopyDestinationRange,
                    copyInput);

            if (copyToRange is { } parsedCopyRange &&
                parsedCopyRange.ColCount > AdvancedFilterCommand.MaxListColumns)
            {
                return AdvancedFilterPlanResult.Invalid(
                    AdvancedFilterPlanError.CopyDestinationRangeTooLarge,
                    copyInput);
            }

            if (copyToRange is { } destinationRange &&
                destinationRange.Start.Sheet != listRange.Start.Sheet)
            {
                return AdvancedFilterPlanResult.Invalid(
                    AdvancedFilterPlanError.CopyDestinationMustBeOnListSheet,
                    copyInput);
            }
        }

        return AdvancedFilterPlanResult.Valid(new AdvancedFilterPlan(
            listRange,
            criteriaRange,
            outputMode,
            uniqueRecordsOnly,
            copyToRange));
    }

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
        WorkbookReferenceNavigator.TryParseReferenceRange(
            NormalizeInput(input),
            defaultSheetId,
            resolveSheetId ?? (static _ => null),
            definedNames: null,
            out range);

    public static bool TryParseCopyDestination(
        string? input,
        SheetId sheetId,
        out CellAddress? destination)
    {
        destination = null;
        var normalized = NormalizeInput(input);
        if (normalized.Length == 0)
            return true;

        if (!WorkbookReferenceNavigator.TryParseAddress(normalized, sheetId, out var parsed))
            return false;

        destination = parsed;
        return true;
    }

    public static bool TryParseCopyDestinationRange(
        string? input,
        SheetId sheetId,
        out GridRange? destination)
    {
        destination = null;
        var normalized = NormalizeInput(input);
        if (normalized.Length == 0)
            return true;

        if (!WorkbookReferenceNavigator.TryParseReferenceRange(
                normalized,
                sheetId,
                static _ => null,
                definedNames: null,
                out var parsed))
            return false;

        if (parsed.Start.Row != parsed.End.Row)
            return false;

        destination = parsed;
        return true;
    }

    public static bool ParseUniqueRecordsOnly(string? input)
    {
        var normalized = NormalizeInput(input);
        return normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("y", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public static AdvancedFilterRangeSelectionRequest CreateRangeSelectionRequest(
        AdvancedFilterRangeSelectionTarget target,
        string? currentText) =>
        new(target, NormalizeInput(currentText), CollapseDialog: true);

    private static string NormalizeInput(string? input) => input?.Trim() ?? "";
}
