using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookSmartTagNormalizer
{
    private static readonly HashSet<string> EmptyAttributes = [];

    private static readonly HashSet<string> SmartTagPropertyAttributes =
    [
        "embed",
        "show"
    ];

    private static readonly HashSet<string> SmartTagTypeAttributes =
    [
        "namespaceUri",
        "name",
        "url"
    ];

    private static readonly HashSet<string> SmartTagShowValues =
    [
        "all",
        "noIndicator",
        "none"
    ];

    public static bool NormalizeSmartTagPropertiesElement(XElement smartTagPr)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(smartTagPr, SmartTagPropertyAttributes);
        changed |= RemoveAllNodes(smartTagPr);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(smartTagPr, "embed", NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(smartTagPr, "show", NormalizeShow);
        return changed;
    }

    public static bool NormalizeSmartTagTypesElement(XElement smartTagTypes)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(smartTagTypes, EmptyAttributes);

        foreach (var child in smartTagTypes.Elements().ToList())
        {
            if (child.Name.LocalName != "smartTagType" ||
                child.Name.NamespaceName != smartTagTypes.Name.NamespaceName)
            {
                child.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeSmartTagType(child);
            if (child.Attribute("namespaceUri") is null || child.Attribute("name") is null)
            {
                child.Remove();
                changed = true;
            }
        }

        return changed;
    }

    public static bool ShouldRemoveSmartTagTypesElement(XElement smartTagTypes) =>
        !smartTagTypes
            .Elements(smartTagTypes.Name.Namespace + "smartTagType")
            .Any();

    private static bool NormalizeSmartTagType(XElement smartTagType)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(smartTagType, SmartTagTypeAttributes);
        changed |= RemoveAllNodes(smartTagType);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(smartTagType, "namespaceUri", NormalizeNonEmptyText);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(smartTagType, "name", NormalizeNonEmptyText);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(smartTagType, "url", NormalizeOptionalText);
        return changed;
    }

    private static bool RemoveAllNodes(XElement element)
    {
        if (!element.Nodes().Any())
            return false;

        element.RemoveNodes();
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

    private static string? NormalizeShow(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && SmartTagShowValues.Contains(trimmed)
            ? trimmed
            : null;
    }

    private static string? NormalizeNonEmptyText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
