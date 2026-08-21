using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record DataValidationPreviewPlan(
    bool HasApplicableRule,
    string Text);

public static class DataValidationPreviewPlanner
{
    private const int MaxPreviewListItems = 8;

    public static DataValidationPreviewPlan Create(
        Workbook workbook,
        Sheet sheet,
        CellAddress activeCell,
        GridRange selectedRange)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);

        var cellReference = DataValidationDisplayTextPlanner.FormatCellReference(activeCell);
        var selectionReference = DataValidationDisplayTextPlanner.FormatRangeReference(selectedRange);
        var lines = new List<string>
        {
            $"Cell: {cellReference}",
            $"Selection: {selectionReference}",
        };

        DataValidation? rule = null;
        if (activeCell.Sheet == sheet.Id)
        {
            foreach (var candidate in DataValidationService.GetApplicable(sheet, activeCell))
            {
                rule = candidate;
                break;
            }
        }

        if (rule is null)
        {
            lines.Add("");
            lines.Add($"No data validation applies to {cellReference}.");
            return new DataValidationPreviewPlan(false, string.Join(Environment.NewLine, lines));
        }

        lines.Add("");
        lines.Add($"Rule: {FormatType(rule.Type)}");
        lines.Add($"Applies to: {FormatRuleRanges(rule, sheet.Name)}");
        lines.Add($"Criteria: {FormatCriteria(rule)}");
        lines.Add($"Ignore blank: {FormatYesNo(rule.AllowBlank)}");

        if (rule.Type == DvType.List)
            AddListPreview(lines, rule, sheet, activeCell, workbook);

        AddPromptPreview(lines, rule);
        AddErrorPreview(lines, rule);

        return new DataValidationPreviewPlan(true, string.Join(Environment.NewLine, lines));
    }

    private static void AddListPreview(List<string> lines, DataValidation rule, Sheet sheet, CellAddress activeCell, Workbook workbook)
    {
        if (!string.IsNullOrWhiteSpace(rule.Formula1))
            lines.Add($"Source: {DataValidationDisplayTextPlanner.FormatPreviewValue(rule.Formula1)}");

        lines.Add($"In-cell dropdown: {(rule.ShowDropdown ? "Shown" : "Hidden")}");

        // DataValidationService.GetListItems is gated on ShowDropdown because its other caller
        // (DataValidationDropdownPlanner) uses it to decide whether to draw the in-cell arrow.
        // The preview describes what the rule actually enforces, not whether Excel draws an
        // arrow, so when the user has hidden the dropdown we still resolve the list contents by
        // asking as if it were shown (via an unpersisted clone); ShowDropdown itself is reported
        // truthfully on the line above.
        var items = rule.ShowDropdown
            ? DataValidationService.GetListItems(rule, sheet, activeCell, workbook)
            : DataValidationService.GetListItems(CloneWithDropdownShown(rule), sheet, activeCell, workbook);
        lines.Add(items.Count == 0
            ? "List items: none available"
            : $"List items: {FormatListItems(items)}");
    }

    private static DataValidation CloneWithDropdownShown(DataValidation rule)
    {
        var clone = rule.Clone();
        clone.ShowDropdown = true;
        return clone;
    }

    private static void AddPromptPreview(List<string> lines, DataValidation rule)
    {
        if (!rule.ShowInputMessage)
            return;

        var prompt = DataValidationDisplayTextPlanner.FormatTitleAndMessage(rule.PromptTitle, rule.PromptMessage);
        if (prompt.Length > 0)
            lines.Add($"Input message: {prompt}");
    }

    private static void AddErrorPreview(List<string> lines, DataValidation rule)
    {
        if (!rule.ShowErrorMessage)
        {
            lines.Add("Error alert: Not shown");
            return;
        }

        var message = DataValidationDisplayTextPlanner.FormatTitleAndMessage(rule.ErrorTitle, rule.ErrorMessage);
        lines.Add(message.Length == 0
            ? $"Error alert: {DataValidationDisplayTextPlanner.FormatAlertStyle(rule.AlertStyle)}"
            : $"Error alert: {DataValidationDisplayTextPlanner.FormatAlertStyle(rule.AlertStyle)} - {message}");
    }

    private static string FormatRuleRanges(DataValidation rule, string sheetName)
    {
        var ranges = new List<string>
        {
            DataValidationService.FormatListSourceRange(rule.AppliesTo, sheetName, sheetName),
        };

        ranges.AddRange(rule.AdditionalRanges.Select(range =>
            DataValidationService.FormatListSourceRange(range, sheetName, sheetName)));
        return string.Join(", ", ranges);
    }

    private static string FormatCriteria(DataValidation rule) =>
        rule.Type switch
        {
            DvType.Any => "Any value",
            DvType.List => "List",
            DvType.Custom => string.IsNullOrWhiteSpace(rule.Formula1)
                ? "Custom formula"
                : $"Custom formula {DataValidationDisplayTextPlanner.FormatPreviewValue(rule.Formula1)}",
            _ => FormatScalarCriteria(rule),
        };

    private static string FormatScalarCriteria(DataValidation rule)
    {
        var type = DataValidationDisplayTextPlanner.GetRuleTypeDisplayName(rule.Type);
        var comparison = FormatOperator(rule.Operator);
        var first = DataValidationDisplayTextPlanner.FormatPreviewValue(rule.Formula1);
        var second = DataValidationDisplayTextPlanner.FormatPreviewValue(rule.Formula2);

        if (rule.Operator is DvOperator.Between or DvOperator.NotBetween)
        {
            return first == "(blank)" && second == "(blank)"
                ? $"{type} {comparison}"
                : $"{type} {comparison} {first} and {second}";
        }

        return first == "(blank)"
            ? $"{type} {comparison}"
            : $"{type} {comparison} {first}";
    }

    private static string FormatListItems(IReadOnlyList<string> items)
    {
        var visibleItems = items
            .Take(MaxPreviewListItems)
            .Select(DataValidationDisplayTextPlanner.FormatPreviewValue)
            .ToArray();
        var suffix = items.Count > MaxPreviewListItems
            ? $" (and {items.Count - MaxPreviewListItems} more)"
            : "";
        return string.Join(", ", visibleItems) + suffix;
    }

    private static string FormatType(DvType type) =>
        DataValidationDisplayTextPlanner.GetRuleTypeDisplayName(type);

    private static string FormatOperator(DvOperator op) =>
        op switch
        {
            DvOperator.Between => "between",
            DvOperator.NotBetween => "not between",
            DvOperator.Equal => "equal to",
            DvOperator.NotEqual => "not equal to",
            DvOperator.GreaterThan => "greater than",
            DvOperator.LessThan => "less than",
            DvOperator.GreaterThanOrEqual => "greater than or equal to",
            DvOperator.LessThanOrEqual => "less than or equal to",
            _ => op.ToString()
        };

    private static string FormatYesNo(bool value) =>
        value ? "Yes" : "No";
}
