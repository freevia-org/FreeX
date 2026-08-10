using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.App.Presentation.Localization;

namespace FreeX.App.Presentation.Filtering;

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

public enum AdvancedFilterErrorFocusTarget
{
    ListRange,
    CriteriaRange,
    CopyTo
}

public enum AdvancedFilterErrorPresentationKind
{
    WarningDialog,
    InlineValidation
}

public sealed record AdvancedFilterErrorDescriptor(
    LocalizedTextDescriptor Message,
    AdvancedFilterErrorFocusTarget FocusTarget);

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

public sealed record AdvancedFilterDialogResult(
    GridRange ListRange,
    GridRange CriteriaRange,
    CellAddress? CopyToCell,
    bool UniqueRecordsOnly,
    GridRange? CopyToRange = null);

public sealed record AdvancedFilterReapplyState(
    GridRange ListRange,
    GridRange CriteriaRange,
    bool UniqueRecordsOnly);

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
        WorkbookRangeTextCodec.TryParse(
            defaultSheetId,
            NormalizeInput(input),
            resolveSheetId ?? (static _ => null),
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

        if (!CellReferenceInputParser.TryParseCell(normalized, sheetId, out var parsed))
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

        if (!WorkbookRangeTextCodec.TryParseOnCurrentSheet(sheetId, normalized, out var parsed))
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

    public static AdvancedFilterDialogResult CreateDialogResult(AdvancedFilterPlan plan) =>
        new(
            plan.ListRange,
            plan.CriteriaRange,
            plan.CopyToCell,
            plan.UniqueRecordsOnly,
            plan.CopyToRange);

    public static bool TryCreateDialogResult(
        AdvancedFilterPlanResult planResult,
        out AdvancedFilterDialogResult result)
    {
        if (!planResult.Success || planResult.Plan is null)
        {
            result = default!;
            return false;
        }

        result = CreateDialogResult(planResult.Plan);
        return true;
    }

    public static AdvancedFilterErrorFocusTarget FocusTargetForPlanError(AdvancedFilterPlanError error) =>
        error switch
        {
            AdvancedFilterPlanError.InvalidCriteriaRange or
            AdvancedFilterPlanError.CriteriaRangeRequiresCriteriaRows or
            AdvancedFilterPlanError.CriteriaRangeTooLarge => AdvancedFilterErrorFocusTarget.CriteriaRange,

            AdvancedFilterPlanError.CopyDestinationRequired or
            AdvancedFilterPlanError.InvalidCopyDestinationRange or
            AdvancedFilterPlanError.CopyDestinationRangeTooLarge or
            AdvancedFilterPlanError.CopyDestinationMustBeOnListSheet => AdvancedFilterErrorFocusTarget.CopyTo,

            _ => AdvancedFilterErrorFocusTarget.ListRange
        };

    public static AdvancedFilterErrorDescriptor DescribeError(
        AdvancedFilterPlanResult result,
        AdvancedFilterErrorPresentationKind presentationKind = AdvancedFilterErrorPresentationKind.WarningDialog)
    {
        ArgumentNullException.ThrowIfNull(result);
        var message = presentationKind == AdvancedFilterErrorPresentationKind.InlineValidation
            ? DescribeInlineError(result)
            : DescribeWarningError(result.Error);

        return new AdvancedFilterErrorDescriptor(message, FocusTargetForPlanError(result.Error));
    }

    private static LocalizedTextDescriptor DescribeWarningError(AdvancedFilterPlanError error) =>
        error switch
        {
            AdvancedFilterPlanError.InvalidListRange =>
                LocalizedTextDescriptor.Resource("AdvancedFilter_EnterValidListRange"),
            AdvancedFilterPlanError.ListRangeRequiresDataRows =>
                LocalizedTextDescriptor.Resource("AdvancedFilter_ListRangeMustIncludeHeaders"),
            AdvancedFilterPlanError.ListRangeTooLarge =>
                LocalizedTextDescriptor.Literal(AdvancedFilterCommand.ListRangeTooLargeMessage),
            AdvancedFilterPlanError.InvalidCriteriaRange =>
                LocalizedTextDescriptor.Resource("AdvancedFilter_EnterValidCriteriaRange"),
            AdvancedFilterPlanError.CriteriaRangeRequiresCriteriaRows =>
                LocalizedTextDescriptor.Resource("AdvancedFilter_CriteriaRangeMustIncludeHeaders"),
            AdvancedFilterPlanError.CriteriaRangeTooLarge =>
                LocalizedTextDescriptor.Literal(AdvancedFilterCommand.CriteriaRangeTooLargeMessage),
            AdvancedFilterPlanError.CopyDestinationRequired or
            AdvancedFilterPlanError.InvalidCopyDestinationRange or
            AdvancedFilterPlanError.CopyDestinationMustBeOnListSheet =>
                LocalizedTextDescriptor.Resource("AdvancedFilter_EnterValidCopyToRange"),
            AdvancedFilterPlanError.CopyDestinationRangeTooLarge =>
                LocalizedTextDescriptor.Literal(AdvancedFilterCommand.CopyOutputTooLargeMessage),
            _ => LocalizedTextDescriptor.Resource("AdvancedFilter_EnterValidFilterRanges")
        };

    private static LocalizedTextDescriptor DescribeInlineError(AdvancedFilterPlanResult result)
    {
        var message = result.Error switch
        {
            AdvancedFilterPlanError.None => "Ready to run Advanced Filter.",
            AdvancedFilterPlanError.InvalidListRange => "Enter a valid list range.",
            AdvancedFilterPlanError.ListRangeRequiresDataRows => "List range must include headers and at least one data row.",
            AdvancedFilterPlanError.ListRangeTooLarge => AdvancedFilterCommand.ListRangeTooLargeMessage,
            AdvancedFilterPlanError.InvalidCriteriaRange => "Enter a valid criteria range.",
            AdvancedFilterPlanError.CriteriaRangeRequiresCriteriaRows => "Criteria range must include headers and at least one criteria row.",
            AdvancedFilterPlanError.CriteriaRangeTooLarge => AdvancedFilterCommand.CriteriaRangeTooLargeMessage,
            AdvancedFilterPlanError.CopyDestinationRequired => "Enter a copy-to range.",
            AdvancedFilterPlanError.InvalidCopyDestinationRange => "Enter a valid one-row copy-to range on the active sheet.",
            AdvancedFilterPlanError.CopyDestinationRangeTooLarge => AdvancedFilterCommand.CopyOutputTooLargeMessage,
            AdvancedFilterPlanError.CopyDestinationMustBeOnListSheet => "Copy-to range must be on the list sheet.",
            _ => "Advanced Filter request is invalid."
        };

        return LocalizedTextDescriptor.Literal(string.IsNullOrWhiteSpace(result.InvalidText)
            ? message
            : $"{message} ({result.InvalidText})");
    }

    private static string NormalizeInput(string? input) => input?.Trim() ?? "";
}

public static class AdvancedFilterReapplyPlanner
{
    public static AdvancedFilterReapplyState? CreateState(AdvancedFilterPlan plan) =>
        CreateState(
            plan.ListRange,
            plan.CriteriaRange,
            plan.OutputMode == AdvancedFilterOutputMode.FilterInPlace,
            plan.UniqueRecordsOnly);

    public static AdvancedFilterReapplyState? CreateState(
        GridRange listRange,
        GridRange criteriaRange,
        bool filterInPlace,
        bool uniqueRecordsOnly) =>
        filterInPlace
            ? new AdvancedFilterReapplyState(listRange, criteriaRange, uniqueRecordsOnly)
            : null;

    public static AdvancedFilterPlan CreatePlan(AdvancedFilterReapplyState state) =>
        new(
            state.ListRange,
            state.CriteriaRange,
            AdvancedFilterOutputMode.FilterInPlace,
            state.UniqueRecordsOnly);
}
