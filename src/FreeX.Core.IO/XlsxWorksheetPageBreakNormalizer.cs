using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPageBreakNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly HashSet<string> PageBreaksAttributes = ["count", "manualBreakCount"];
    private static readonly HashSet<string> BreakAttributes = ["id", "min", "max", "man", "pt"];

    public static bool NormalizeElement(XElement pageBreaks)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(pageBreaks, PageBreaksAttributes);
        changed |= RemoveUnexpectedChildren(pageBreaks);

        foreach (var breakElement in pageBreaks.Elements(WorksheetNs + "brk").ToList())
            changed |= NormalizeBreakElement(breakElement);

        var breakCount = pageBreaks.Elements(WorksheetNs + "brk").Count();
        var manualBreakCount = pageBreaks
            .Elements(WorksheetNs + "brk")
            .Count(element => !string.Equals(element.Attribute("man")?.Value, "0", StringComparison.Ordinal));

        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(pageBreaks, "count", breakCount.ToString(CultureInfo.InvariantCulture));
        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(pageBreaks, "manualBreakCount", manualBreakCount.ToString(CultureInfo.InvariantCulture));
        return changed;
    }

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var changed = false;
        if (worksheetRoot.Element(WorksheetNs + "rowBreaks") is { } rowBreaks)
            changed |= NormalizeElement(rowBreaks);
        if (worksheetRoot.Element(WorksheetNs + "colBreaks") is { } columnBreaks)
            changed |= NormalizeElement(columnBreaks);
        return changed;
    }

    private static bool NormalizeBreakElement(XElement breakElement)
    {
        var normalizedId = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull(breakElement.Attribute("id")?.Value);
        if (normalizedId is null)
        {
            breakElement.Remove();
            return true;
        }

        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(breakElement, BreakAttributes);
        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(breakElement, "id", normalizedId);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(breakElement, "min", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(breakElement, "max", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(breakElement, "man", XlsxXmlNormalizationHelpers.NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(breakElement, "pt", XlsxXmlNormalizationHelpers.NormalizeBoolean);
        return changed;
    }

    private static bool RemoveUnexpectedChildren(XElement pageBreaks)
    {
        var changed = false;
        foreach (var child in pageBreaks.Elements().ToList())
        {
            if (child.Name == WorksheetNs + "brk")
                continue;

            child.Remove();
            changed = true;
        }

        return changed;
    }

}
