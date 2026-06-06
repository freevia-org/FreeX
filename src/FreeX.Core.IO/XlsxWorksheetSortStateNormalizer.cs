using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetSortStateNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly HashSet<string> ValidSortMethods = ["stroke", "pinYin"];

    private static readonly HashSet<string> ValidSortByValues = ["value", "cellColor", "fontColor", "icon"];

    public static bool NormalizeElement(XElement sortState)
    {
        var changed = false;
        changed |= NormalizeAttribute(sortState, "columnSort", NormalizeBoolean);
        changed |= NormalizeAttribute(sortState, "caseSensitive", NormalizeBoolean);
        changed |= NormalizeAttribute(sortState, "sortMethod", value => NormalizeToken(value, ValidSortMethods));

        foreach (var condition in sortState.Elements(WorksheetNs + "sortCondition").ToList())
        {
            if (string.IsNullOrWhiteSpace(condition.Attribute("ref")?.Value))
            {
                condition.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeAttribute(condition, "descending", NormalizeBoolean);
            changed |= NormalizeAttribute(condition, "sortBy", value => NormalizeToken(value, ValidSortByValues));
            changed |= NormalizeAttribute(condition, "dxfId", NormalizeUnsignedIntOrNull);
            changed |= NormalizeAttribute(condition, "iconId", NormalizeUnsignedIntOrNull);
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

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NormalizeToken(string? value, IReadOnlySet<string> allowedValues)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && allowedValues.Contains(trimmed) ? trimmed : null;
    }
}
