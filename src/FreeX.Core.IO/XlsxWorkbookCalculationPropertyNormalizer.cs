using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookCalculationPropertyNormalizer
{
    private static readonly HashSet<string> ValidCalculationModes =
    [
        "manual",
        "auto",
        "autoNoTable"
    ];

    private static readonly HashSet<string> ValidReferenceModes =
    [
        "A1",
        "R1C1"
    ];

    private static readonly string[] BooleanAttributes =
    [
        "fullCalcOnLoad",
        "iterate",
        "fullPrecision",
        "calcCompleted",
        "calcOnSave",
        "concurrentCalc",
        "forceFullCalc"
    ];

    private static readonly string[] UnsignedIntAttributes =
    [
        "calcId",
        "iterateCount",
        "concurrentManualCount"
    ];

    public static bool NormalizeElement(XElement calcPr)
    {
        var changed = false;
        changed |= NormalizeAttribute(calcPr, "calcMode", value => NormalizeToken(value, ValidCalculationModes));
        changed |= NormalizeAttribute(calcPr, "refMode", value => NormalizeToken(value, ValidReferenceModes));
        changed |= NormalizeAttribute(calcPr, "iterateDelta", NormalizeDouble);

        foreach (var attributeName in BooleanAttributes)
            changed |= NormalizeAttribute(calcPr, attributeName, NormalizeBoolean);
        foreach (var attributeName in UnsignedIntAttributes)
            changed |= NormalizeAttribute(calcPr, attributeName, NormalizeUnsignedIntOrNull);

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

    private static string? NormalizeDouble(string? value)
    {
        var trimmed = value?.Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed))
        {
            return null;
        }

        return parsed.ToString("G17", CultureInfo.InvariantCulture);
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
}
