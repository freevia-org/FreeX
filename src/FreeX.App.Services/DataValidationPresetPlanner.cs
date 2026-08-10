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

    public static IReadOnlyList<DataValidationRuleTypeMetadata> GetRuleTypeMetadata() =>
        DataValidationDisplayTextPlanner.GetRuleTypeMetadata();

    public static string GetDisplayName(DvType type) =>
        DataValidationDisplayTextPlanner.GetRuleTypeDisplayName(type);

    public static bool RequiresSecondFormula(DvType type, DvOperator op) =>
        DataValidationDisplayTextPlanner.RequiresSecondFormula(type, op);

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

        var activeRule = activeCell.Sheet == sheet.Id ? GetFirstApplicableRule(sheet, activeCell) : null;
        var activeRuleClone = activeRule is null ? null : CloneRule(activeRule);
        var activeCellReference = DataValidationDisplayTextPlanner.FormatCellReference(activeCell);
        var selectionReference = DataValidationDisplayTextPlanner.FormatRangeReference(selectedRange);

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
            var rule = GetFirstApplicableRule(sheet, address);
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

    private static DataValidation? GetFirstApplicableRule(Sheet sheet, CellAddress address)
    {
        foreach (var rule in DataValidationService.GetApplicable(sheet, address))
            return rule;

        return null;
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
        left.HasSameSettings(right, includeNativeMetadata: true);

    private static DataValidation CloneRule(DataValidation source) => source.Clone();

}
