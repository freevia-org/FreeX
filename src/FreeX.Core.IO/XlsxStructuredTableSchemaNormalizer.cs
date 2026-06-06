using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxStructuredTableSchemaNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly HashSet<string> ValidTableTypes = ["worksheet", "xml", "queryTable"];

    private static readonly HashSet<string> ValidTotalsRowFunctions =
    [
        "none",
        "sum",
        "min",
        "max",
        "average",
        "count",
        "countNums",
        "stdDev",
        "var",
        "custom"
    ];

    private static readonly string[] TableUnsignedIntAttributes =
    [
        "headerRowCount",
        "totalsRowCount",
        "headerRowDxfId",
        "dataDxfId",
        "totalsRowDxfId",
        "headerRowBorderDxfId",
        "tableBorderDxfId",
        "totalsRowBorderDxfId",
        "connectionId"
    ];

    private static readonly string[] TableBooleanAttributes =
    [
        "insertRow",
        "insertRowShift",
        "totalsRowShown",
        "published"
    ];

    private static readonly string[] TableColumnUnsignedIntAttributes =
    [
        "queryTableFieldId",
        "headerRowDxfId",
        "dataDxfId",
        "totalsRowDxfId"
    ];

    private static readonly string[] TableStyleInfoBooleanAttributes =
    [
        "showFirstColumn",
        "showLastColumn",
        "showRowStripes",
        "showColumnStripes"
    ];

    public static bool NormalizeElement(XElement table, string? tablePath = null)
    {
        var changed = false;

        changed |= NormalizeRequiredUnsignedIntAttribute(table, "id", ExtractTrailingNumber(tablePath));
        changed |= NormalizeAttribute(table, "tableType", value => NormalizeToken(value, ValidTableTypes));
        foreach (var attributeName in TableUnsignedIntAttributes)
            changed |= NormalizeAttribute(table, attributeName, NormalizeUnsignedIntOrNull);
        foreach (var attributeName in TableBooleanAttributes)
            changed |= NormalizeAttribute(table, attributeName, NormalizeBoolean);

        var tableColumns = table.Element(WorksheetNs + "tableColumns");
        if (tableColumns is not null)
            changed |= NormalizeTableColumnsElement(tableColumns);

        var tableStyleInfo = table.Element(WorksheetNs + "tableStyleInfo");
        if (tableStyleInfo is not null)
            changed |= NormalizeTableStyleInfoElement(tableStyleInfo);

        return changed;
    }

    private static bool NormalizeTableColumnsElement(XElement tableColumns)
    {
        var changed = false;
        var columns = tableColumns.Elements(WorksheetNs + "tableColumn").ToArray();
        changed |= SetAttributeIfChanged(
            tableColumns,
            "count",
            columns.Length.ToString(CultureInfo.InvariantCulture));

        for (var index = 0; index < columns.Length; index++)
            changed |= NormalizeTableColumnElement(columns[index], index + 1);

        return changed;
    }

    private static bool NormalizeTableColumnElement(XElement tableColumn, int fallbackId)
    {
        var changed = false;

        changed |= NormalizeRequiredUnsignedIntAttribute(tableColumn, "id", fallbackId);
        changed |= NormalizeAttribute(
            tableColumn,
            "totalsRowFunction",
            value => NormalizeToken(value, ValidTotalsRowFunctions));
        foreach (var attributeName in TableColumnUnsignedIntAttributes)
            changed |= NormalizeAttribute(tableColumn, attributeName, NormalizeUnsignedIntOrNull);

        foreach (var formula in tableColumn.Elements(WorksheetNs + "calculatedColumnFormula"))
            changed |= NormalizeAttribute(formula, "array", NormalizeBoolean);
        foreach (var formula in tableColumn.Elements(WorksheetNs + "totalsRowFormula"))
            changed |= NormalizeAttribute(formula, "array", NormalizeBoolean);

        return changed;
    }

    private static bool NormalizeTableStyleInfoElement(XElement tableStyleInfo)
    {
        var changed = false;
        foreach (var attributeName in TableStyleInfoBooleanAttributes)
            changed |= NormalizeAttribute(tableStyleInfo, attributeName, NormalizeBoolean);

        return changed;
    }

    private static bool NormalizeRequiredUnsignedIntAttribute(
        XElement element,
        string attributeName,
        int fallbackValue)
    {
        var normalized = NormalizeUnsignedIntOrNull(element.Attribute(attributeName)?.Value) ??
            Math.Max(1, fallbackValue).ToString(CultureInfo.InvariantCulture);
        return SetAttributeIfChanged(element, attributeName, normalized);
    }

    private static bool NormalizeAttribute(
        XElement element,
        string attributeName,
        Func<string?, string?> normalize)
    {
        var attribute = element.Attribute(attributeName);
        var normalized = normalize(attribute?.Value);
        if (normalized is null)
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        return SetAttributeIfChanged(element, attributeName, normalized);
    }

    private static bool SetAttributeIfChanged(XElement element, string attributeName, string value)
    {
        var attribute = element.Attribute(attributeName);
        if (attribute is not null && string.Equals(attribute.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, value);
        return true;
    }

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed switch
        {
            "0" or "1" => trimmed,
            "true" or "false" => trimmed,
            _ => null
        };
    }

    private static string? NormalizeToken(string? value, IReadOnlySet<string> allowedValues)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && allowedValues.Contains(trimmed) ? trimmed : null;
    }

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static int ExtractTrailingNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 1;

        var start = text.Length;
        while (start > 0 && char.IsDigit(text[start - 1]))
            start--;

        var value = 0;
        for (var index = start; index < text.Length; index++)
        {
            var digit = text[index] - '0';
            if (value > (int.MaxValue - digit) / 10)
                return 1;

            value = (value * 10) + digit;
        }

        return value > 0 ? value : 1;
    }
}
