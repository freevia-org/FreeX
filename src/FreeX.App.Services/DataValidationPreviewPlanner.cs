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

        var cellReference = FormatCellReference(activeCell);
        var selectionReference = FormatRangeReference(selectedRange);
        var lines = new List<string>
        {
            $"Cell: {cellReference}",
            $"Selection: {selectionReference}",
        };

        var rule = activeCell.Sheet == sheet.Id
            ? DataValidationService.GetApplicable(sheet, activeCell).FirstOrDefault()
            : null;

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
            AddListPreview(lines, rule, sheet, workbook);

        AddPromptPreview(lines, rule);
        AddErrorPreview(lines, rule);

        return new DataValidationPreviewPlan(true, string.Join(Environment.NewLine, lines));
    }

    private static void AddListPreview(List<string> lines, DataValidation rule, Sheet sheet, Workbook workbook)
    {
        if (!string.IsNullOrWhiteSpace(rule.Formula1))
            lines.Add($"Source: {FormatPreviewValue(rule.Formula1)}");

        lines.Add($"In-cell dropdown: {(rule.ShowDropdown ? "Shown" : "Hidden")}");

        var items = DataValidationService.GetListItems(rule, sheet, workbook);
        lines.Add(items.Count == 0
            ? "List items: none available"
            : $"List items: {FormatListItems(items)}");
    }

    private static void AddPromptPreview(List<string> lines, DataValidation rule)
    {
        if (!rule.ShowInputMessage)
            return;

        var prompt = FormatTitleAndMessage(rule.PromptTitle, rule.PromptMessage);
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

        var message = FormatTitleAndMessage(rule.ErrorTitle, rule.ErrorMessage);
        lines.Add(message.Length == 0
            ? $"Error alert: {FormatAlertStyle(rule.AlertStyle)}"
            : $"Error alert: {FormatAlertStyle(rule.AlertStyle)} - {message}");
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
                : $"Custom formula {FormatPreviewValue(rule.Formula1)}",
            _ => FormatScalarCriteria(rule),
        };

    private static string FormatScalarCriteria(DataValidation rule)
    {
        var type = FormatType(rule.Type);
        var comparison = FormatOperator(rule.Operator);
        var first = FormatPreviewValue(rule.Formula1);
        var second = FormatPreviewValue(rule.Formula2);

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
            .Select(FormatPreviewValue)
            .ToArray();
        var suffix = items.Count > MaxPreviewListItems
            ? $" (and {items.Count - MaxPreviewListItems} more)"
            : "";
        return string.Join(", ", visibleItems) + suffix;
    }

    private static string FormatTitleAndMessage(string? title, string? message)
    {
        var cleanTitle = FormatOptionalText(title);
        var cleanMessage = FormatOptionalText(message);
        return (cleanTitle.Length, cleanMessage.Length) switch
        {
            (> 0, > 0) => $"{cleanTitle} - {cleanMessage}",
            (> 0, _) => cleanTitle,
            (_, > 0) => cleanMessage,
            _ => ""
        };
    }

    private static string FormatOptionalText(string? value)
    {
        var text = value?.Trim() ?? "";
        return text
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    private static string FormatPreviewValue(string? value)
    {
        var text = FormatOptionalText(value);
        return text.Length == 0 ? "(blank)" : text;
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

    private static string FormatType(DvType type) =>
        type switch
        {
            DvType.Any => "Any value",
            DvType.WholeNumber => "Whole number",
            DvType.Decimal => "Decimal",
            DvType.List => "List",
            DvType.Date => "Date",
            DvType.Time => "Time",
            DvType.TextLength => "Text length",
            DvType.Custom => "Custom",
            _ => type.ToString()
        };

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

    private static string FormatAlertStyle(DvAlertStyle style) =>
        style switch
        {
            DvAlertStyle.Stop => "Stop",
            DvAlertStyle.Warning => "Warning",
            DvAlertStyle.Information => "Information",
            _ => style.ToString()
        };

    private static string FormatYesNo(bool value) =>
        value ? "Yes" : "No";
}
