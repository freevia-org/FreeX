using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum DataValidationSelectionState
{
    None,
    Uniform,
    Partial,
    Mixed,
    TooLargeToSummarize
}

public sealed record DataValidationRuleTypeMetadata(
    DvType Type,
    string DisplayName,
    bool ShowsOperator,
    bool ShowsDropdown,
    bool RequiresFormula1,
    bool RequiresFormula2);

public sealed record DataValidationSelectionSummary(
    DataValidationSelectionState State,
    string ActiveCellReference,
    string SelectionReference,
    DataValidation? ActiveCellRule,
    string Text,
    int ScannedCellCount,
    long TotalCellCount)
{
    public bool HasActiveCellRule => ActiveCellRule is not null;
    public bool IsComplete => ScannedCellCount == TotalCellCount;
}

public static class DataValidationPresetPlanner
{
    public const int DefaultSelectionScanLimit = 4096;

    private static readonly IReadOnlyList<DataValidationRuleTypeMetadata> RuleTypeMetadata =
    [
        new(DvType.Any, "Any value", ShowsOperator: false, ShowsDropdown: false, RequiresFormula1: false, RequiresFormula2: false),
        new(DvType.WholeNumber, "Whole number", ShowsOperator: true, ShowsDropdown: false, RequiresFormula1: true, RequiresFormula2: true),
        new(DvType.Decimal, "Decimal", ShowsOperator: true, ShowsDropdown: false, RequiresFormula1: true, RequiresFormula2: true),
        new(DvType.List, "List", ShowsOperator: false, ShowsDropdown: true, RequiresFormula1: true, RequiresFormula2: false),
        new(DvType.Date, "Date", ShowsOperator: true, ShowsDropdown: false, RequiresFormula1: true, RequiresFormula2: true),
        new(DvType.Time, "Time", ShowsOperator: true, ShowsDropdown: false, RequiresFormula1: true, RequiresFormula2: true),
        new(DvType.TextLength, "Text length", ShowsOperator: true, ShowsDropdown: false, RequiresFormula1: true, RequiresFormula2: true),
        new(DvType.Custom, "Custom", ShowsOperator: false, ShowsDropdown: false, RequiresFormula1: true, RequiresFormula2: false)
    ];

    public static IReadOnlyList<DataValidationRuleTypeMetadata> GetRuleTypeMetadata() =>
        RuleTypeMetadata;

    public static string GetDisplayName(DvType type) =>
        RuleTypeMetadata.FirstOrDefault(item => item.Type == type)?.DisplayName ?? type.ToString();

    public static bool RequiresSecondFormula(DvType type, DvOperator op) =>
        type is not DvType.Any and not DvType.List and not DvType.Custom &&
        op is DvOperator.Between or DvOperator.NotBetween;

    public static DataValidation CreateDefaultRule(DvType type, GridRange selectedRange)
    {
        var op = DvOperator.Between;
        return new DataValidation
        {
            AppliesTo = selectedRange,
            Type = type,
            Operator = op,
            Formula1 = "",
            Formula2 = RequiresSecondFormula(type, op) ? "" : "",
            AllowBlank = true,
            ShowDropdown = type == DvType.List,
            AlertStyle = DvAlertStyle.Stop,
            ShowInputMessage = true,
            ShowErrorMessage = true,
            ErrorTitle = "",
            ErrorMessage = "",
            PromptTitle = "",
            PromptMessage = ""
        };
    }

    public static DataValidationSelectionSummary CreateSelectionSummary(
        Workbook workbook,
        Sheet sheet,
        CellAddress activeCell,
        GridRange selectedRange,
        int maxCellsToScan = DefaultSelectionScanLimit)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        if (maxCellsToScan <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCellsToScan), maxCellsToScan, "Scan limit must be positive.");

        var activeRule = activeCell.Sheet == sheet.Id
            ? DataValidationService.GetApplicable(sheet, activeCell).FirstOrDefault()
            : null;
        var activeRuleClone = activeRule is null ? null : CloneRule(activeRule);
        var activeCellReference = FormatCellReference(activeCell);
        var selectionReference = FormatRangeReference(selectedRange);

        if (selectedRange.Start.Sheet != sheet.Id)
        {
            return new DataValidationSelectionSummary(
                DataValidationSelectionState.None,
                activeCellReference,
                selectionReference,
                activeRuleClone,
                $"No data validation applies to {selectionReference}.",
                ScannedCellCount: 0,
                TotalCellCount: selectedRange.CellCount);
        }

        if (selectedRange.CellCount > maxCellsToScan)
        {
            var activeText = activeRule is null
                ? "the active cell has no data validation"
                : $"the active cell uses {GetDisplayName(activeRule.Type)} data validation";
            return new DataValidationSelectionSummary(
                DataValidationSelectionState.TooLargeToSummarize,
                activeCellReference,
                selectionReference,
                activeRuleClone,
                $"Selection {selectionReference} is too large to summarize exactly; {activeText}.",
                ScannedCellCount: 0,
                TotalCellCount: selectedRange.CellCount);
        }

        var scannedCellCount = 0;
        var cellsWithRule = 0;
        DataValidation? firstRule = null;
        var hasMixedRules = false;

        foreach (var address in selectedRange.AllCells())
        {
            scannedCellCount++;
            var rule = DataValidationService.GetApplicable(sheet, address).FirstOrDefault();
            if (rule is null)
                continue;

            cellsWithRule++;
            firstRule ??= rule;
            if (!HaveSameSettings(firstRule, rule))
                hasMixedRules = true;
        }

        var state = GetSelectionState(scannedCellCount, cellsWithRule, hasMixedRules);
        var text = CreateSummaryText(state, selectionReference, firstRule, cellsWithRule, scannedCellCount);
        return new DataValidationSelectionSummary(
            state,
            activeCellReference,
            selectionReference,
            activeRuleClone,
            text,
            scannedCellCount,
            selectedRange.CellCount);
    }

    private static DataValidationSelectionState GetSelectionState(
        int scannedCellCount,
        int cellsWithRule,
        bool hasMixedRules)
    {
        if (cellsWithRule == 0)
            return DataValidationSelectionState.None;

        if (hasMixedRules)
            return DataValidationSelectionState.Mixed;

        return cellsWithRule == scannedCellCount
            ? DataValidationSelectionState.Uniform
            : DataValidationSelectionState.Partial;
    }

    private static string CreateSummaryText(
        DataValidationSelectionState state,
        string selectionReference,
        DataValidation? firstRule,
        int cellsWithRule,
        int scannedCellCount)
    {
        var ruleName = firstRule is null ? "" : GetDisplayName(firstRule.Type);
        return state switch
        {
            DataValidationSelectionState.None => $"No data validation applies to {selectionReference}.",
            DataValidationSelectionState.Uniform => $"Selection {selectionReference} uses {ruleName} data validation.",
            DataValidationSelectionState.Partial => $"{cellsWithRule} of {scannedCellCount} selected cells use {ruleName} data validation.",
            DataValidationSelectionState.Mixed => $"Selection {selectionReference} has mixed data validation rules.",
            _ => $"Selection {selectionReference} is too large to summarize exactly."
        };
    }

    private static bool HaveSameSettings(DataValidation left, DataValidation right) =>
        left.Type == right.Type &&
        left.Operator == right.Operator &&
        string.Equals(left.Formula1, right.Formula1, StringComparison.Ordinal) &&
        string.Equals(left.Formula2, right.Formula2, StringComparison.Ordinal) &&
        left.AllowBlank == right.AllowBlank &&
        left.ShowDropdown == right.ShowDropdown &&
        left.AlertStyle == right.AlertStyle &&
        left.ShowInputMessage == right.ShowInputMessage &&
        left.ShowErrorMessage == right.ShowErrorMessage &&
        string.Equals(left.ErrorTitle, right.ErrorTitle, StringComparison.Ordinal) &&
        string.Equals(left.ErrorMessage, right.ErrorMessage, StringComparison.Ordinal) &&
        string.Equals(left.PromptTitle, right.PromptTitle, StringComparison.Ordinal) &&
        string.Equals(left.PromptMessage, right.PromptMessage, StringComparison.Ordinal) &&
        DictionaryEquals(left.NativeAttributes, right.NativeAttributes) &&
        SequenceEquals(left.NativeChildXmls, right.NativeChildXmls) &&
        DictionaryEquals(left.NativeContainerAttributes, right.NativeContainerAttributes) &&
        SequenceEquals(left.NativeContainerChildXmls, right.NativeContainerChildXmls);

    private static DataValidation CloneRule(DataValidation source)
    {
        var clone = new DataValidation
        {
            Id = source.Id,
            AppliesTo = source.AppliesTo,
            Type = source.Type,
            Operator = source.Operator,
            Formula1 = source.Formula1,
            Formula2 = source.Formula2,
            AllowBlank = source.AllowBlank,
            ShowDropdown = source.ShowDropdown,
            AlertStyle = source.AlertStyle,
            ShowInputMessage = source.ShowInputMessage,
            ShowErrorMessage = source.ShowErrorMessage,
            ErrorTitle = source.ErrorTitle,
            ErrorMessage = source.ErrorMessage,
            PromptTitle = source.PromptTitle,
            PromptMessage = source.PromptMessage,
            NativeAttributes = source.NativeAttributes,
            NativeChildXmls = source.NativeChildXmls,
            NativeContainerAttributes = source.NativeContainerAttributes,
            NativeContainerChildXmls = source.NativeContainerChildXmls
        };
        clone.AdditionalRanges.AddRange(source.AdditionalRanges);
        return clone;
    }

    private static bool DictionaryEquals(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null || left.Count != right.Count)
            return false;

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var rightValue) ||
                !string.Equals(value, rightValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SequenceEquals<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null || left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
                return false;
        }

        return true;
    }

    private static string FormatCellReference(CellAddress address) =>
        CellAddress.NumberToColumnName(address.Col) + address.Row.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatRangeReference(GridRange range)
    {
        var start = FormatCellReference(range.Start);
        var end = FormatCellReference(range.End);
        return string.Equals(start, end, StringComparison.Ordinal)
            ? start
            : $"{start}:{end}";
    }
}
