using FreeX.App.Presentation;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record DataValidationRuleTypeMetadata(
    DvType Type,
    string DisplayName,
    bool ShowsOperator,
    bool ShowsDropdown,
    bool RequiresFormula1,
    bool RequiresFormula2);

public sealed record DataValidationAlertStyleMetadata(
    DvAlertStyle Style,
    string DisplayName);

public static class DataValidationDisplayTextPlanner
{
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

    private static readonly IReadOnlyList<DataValidationAlertStyleMetadata> AlertStyleMetadata =
    [
        new(DvAlertStyle.Stop, "Stop"),
        new(DvAlertStyle.Warning, "Warning"),
        new(DvAlertStyle.Information, "Information")
    ];

    public static IReadOnlyList<DataValidationRuleTypeMetadata> GetRuleTypeMetadata() =>
        RuleTypeMetadata;

    public static IReadOnlyList<DataValidationAlertStyleMetadata> GetAlertStyleMetadata() =>
        AlertStyleMetadata;

    public static string GetRuleTypeDisplayName(DvType type) =>
        FindRuleTypeMetadata(type)?.DisplayName ?? type.ToString();

    public static bool RequiresSecondFormula(DvType type, DvOperator op) =>
        type is not DvType.Any and not DvType.List and not DvType.Custom &&
        op is DvOperator.Between or DvOperator.NotBetween;

    public static string FormatAlertStyle(DvAlertStyle style) =>
        FindAlertStyleMetadata(style)?.DisplayName ?? style.ToString();

    public static string FormatTitleAndMessage(string? title, string? message)
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

    public static string FormatOptionalText(string? value)
    {
        var text = value?.Trim() ?? "";
        return text
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }

    public static string FormatPreviewValue(string? value)
    {
        var text = FormatOptionalText(value);
        return text.Length == 0 ? "(blank)" : text;
    }

    public static string FormatCellReference(CellAddress address) =>
        SpreadsheetDisplayFormatter.FormatCellReference(address, useR1C1ReferenceStyle: false);

    public static string FormatRangeReference(GridRange range) =>
        SpreadsheetDisplayFormatter.FormatRangeReference(range, useR1C1ReferenceStyle: false);

    private static DataValidationRuleTypeMetadata? FindRuleTypeMetadata(DvType type)
    {
        foreach (var item in RuleTypeMetadata)
        {
            if (item.Type == type)
                return item;
        }

        return null;
    }

    private static DataValidationAlertStyleMetadata? FindAlertStyleMetadata(DvAlertStyle style)
    {
        foreach (var item in AlertStyleMetadata)
        {
            if (item.Style == style)
                return item;
        }

        return null;
    }
}
