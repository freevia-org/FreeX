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
        changed |= RemoveUnknownAttributes(pageBreaks, PageBreaksAttributes);
        changed |= RemoveUnexpectedChildren(pageBreaks);

        foreach (var breakElement in pageBreaks.Elements(WorksheetNs + "brk").ToList())
            changed |= NormalizeBreakElement(breakElement);

        var breakCount = pageBreaks.Elements(WorksheetNs + "brk").Count();
        var manualBreakCount = pageBreaks
            .Elements(WorksheetNs + "brk")
            .Count(element => !string.Equals(element.Attribute("man")?.Value, "0", StringComparison.Ordinal));

        changed |= SetAttributeIfChanged(pageBreaks, "count", breakCount.ToString(CultureInfo.InvariantCulture));
        changed |= SetAttributeIfChanged(pageBreaks, "manualBreakCount", manualBreakCount.ToString(CultureInfo.InvariantCulture));
        return changed;
    }

    private static bool NormalizeBreakElement(XElement breakElement)
    {
        var normalizedId = NormalizeUnsignedIntOrNull(breakElement.Attribute("id")?.Value);
        if (normalizedId is null)
        {
            breakElement.Remove();
            return true;
        }

        var changed = false;
        changed |= RemoveUnknownAttributes(breakElement, BreakAttributes);
        changed |= SetAttributeIfChanged(breakElement, "id", normalizedId);
        changed |= NormalizeAttribute(breakElement, "min", NormalizeUnsignedIntOrNull);
        changed |= NormalizeAttribute(breakElement, "max", NormalizeUnsignedIntOrNull);
        changed |= NormalizeAttribute(breakElement, "man", NormalizeBoolean);
        changed |= NormalizeAttribute(breakElement, "pt", NormalizeBoolean);
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

        return SetAttributeIfChanged(element, attributeName, normalized);
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

    private static bool RemoveUnknownAttributes(XElement element, IReadOnlySet<string> allowedNames)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && allowedNames.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
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

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }
}
