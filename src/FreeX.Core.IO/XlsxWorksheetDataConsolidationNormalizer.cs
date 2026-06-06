using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetDataConsolidationNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly HashSet<string> ValidFunctions =
    [
        "average",
        "count",
        "countNums",
        "max",
        "min",
        "product",
        "stdDev",
        "stdDevp",
        "sum",
        "var",
        "varp"
    ];

    public static bool NormalizeElement(XElement dataConsolidate)
    {
        var changed = false;
        changed |= NormalizeAttribute(dataConsolidate, "function", value => NormalizeToken(value, ValidFunctions));
        changed |= NormalizeAttribute(dataConsolidate, "leftLabels", NormalizeBoolean);
        changed |= NormalizeAttribute(dataConsolidate, "topLabels", NormalizeBoolean);
        changed |= NormalizeAttribute(dataConsolidate, "link", NormalizeBoolean);

        foreach (var dataRefs in dataConsolidate.Elements(WorksheetNs + "dataRefs"))
        {
            var count = dataRefs.Elements(WorksheetNs + "dataRef").Count().ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(dataRefs.Attribute("count")?.Value, count, StringComparison.Ordinal))
            {
                dataRefs.SetAttributeValue("count", count);
                changed = true;
            }
        }

        return changed;
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

        if (attribute is not null && string.Equals(attribute.Value, normalized, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, normalized);
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
}
