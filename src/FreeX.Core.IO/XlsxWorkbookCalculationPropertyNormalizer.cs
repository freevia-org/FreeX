using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookCalculationPropertyNormalizer
{
    private static readonly HashSet<string> CalculationPropertyAttributes =
    [
        "calcId",
        "calcMode",
        "fullCalcOnLoad",
        "refMode",
        "iterate",
        "iterateCount",
        "iterateDelta",
        "fullPrecision",
        "calcCompleted",
        "calcOnSave",
        "concurrentCalc",
        "concurrentManualCount",
        "forceFullCalc"
    ];

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
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(calcPr, CalculationPropertyAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(calcPr);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(calcPr, "calcMode", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ValidCalculationModes));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(calcPr, "refMode", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ValidReferenceModes));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(calcPr, "iterateDelta", NormalizeDouble);

        foreach (var attributeName in BooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(calcPr, attributeName, XlsxXmlNormalizationHelpers.NormalizeBoolean);
        foreach (var attributeName in UnsignedIntAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(calcPr, attributeName, XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);

        return changed;
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

}
