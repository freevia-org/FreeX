using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookPropertiesNormalizer
{
    private static readonly HashSet<string> WorkbookPropertyAttributes =
    [
        "date1904",
        "showObjects",
        "showBorderUnselectedTables",
        "filterPrivacy",
        "promptedSolutions",
        "showInkAnnotation",
        "backupFile",
        "saveExternalLinkValues",
        "updateLinks",
        "codeName",
        "hidePivotFieldList",
        "showPivotChartFilter",
        "allowRefreshQuery",
        "publishItems",
        "checkCompatibility",
        "autoCompressPictures",
        "refreshAllConnections",
        "defaultThemeVersion"
    ];

    private static readonly string[] BooleanAttributes =
    [
        "date1904",
        "showBorderUnselectedTables",
        "filterPrivacy",
        "promptedSolutions",
        "showInkAnnotation",
        "backupFile",
        "saveExternalLinkValues",
        "hidePivotFieldList",
        "showPivotChartFilter",
        "allowRefreshQuery",
        "publishItems",
        "checkCompatibility",
        "autoCompressPictures",
        "refreshAllConnections"
    ];

    private static readonly HashSet<string> ShowObjectsValues =
    [
        "all",
        "placeholders",
        "none"
    ];

    private static readonly HashSet<string> UpdateLinksValues =
    [
        "userSet",
        "never",
        "always"
    ];

    public static bool NormalizeElement(XElement workbookPr)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(workbookPr);
        changed |= RemoveAllNodes(workbookPr);

        foreach (var attributeName in BooleanAttributes)
            changed |= NormalizeAttribute(workbookPr, attributeName, NormalizeBoolean);

        changed |= NormalizeAttribute(workbookPr, "showObjects", value => NormalizeKnownValue(value, ShowObjectsValues));
        changed |= NormalizeAttribute(workbookPr, "updateLinks", value => NormalizeKnownValue(value, UpdateLinksValues));
        changed |= NormalizeAttribute(workbookPr, "defaultThemeVersion", NormalizeUnsignedIntOrNull);
        changed |= NormalizeAttribute(workbookPr, "codeName", NormalizeOptionalText);
        return changed;
    }

    private static bool RemoveUnknownAttributes(XElement element)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && WorkbookPropertyAttributes.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveAllNodes(XElement element)
    {
        if (!element.Nodes().Any())
            return false;

        element.RemoveNodes();
        return true;
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

    private static string? NormalizeKnownValue(string? value, HashSet<string> knownValues)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && knownValues.Contains(trimmed)
            ? trimmed
            : null;
    }

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
