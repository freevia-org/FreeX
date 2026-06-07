using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookSmartTagNormalizer
{
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
        changed |= RemoveUnknownAttributes(smartTagPr, SmartTagPropertyAttributes);
        changed |= RemoveAllNodes(smartTagPr);
        changed |= NormalizeAttribute(smartTagPr, "embed", NormalizeBoolean);
        changed |= NormalizeAttribute(smartTagPr, "show", NormalizeShow);
        return changed;
    }

    public static bool NormalizeSmartTagTypesElement(XElement smartTagTypes)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(smartTagTypes, []);

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
        changed |= RemoveUnknownAttributes(smartTagType, SmartTagTypeAttributes);
        changed |= RemoveAllNodes(smartTagType);
        changed |= NormalizeAttribute(smartTagType, "namespaceUri", NormalizeNonEmptyText);
        changed |= NormalizeAttribute(smartTagType, "name", NormalizeNonEmptyText);
        changed |= NormalizeAttribute(smartTagType, "url", NormalizeOptionalText);
        return changed;
    }

    private static bool RemoveUnknownAttributes(XElement element, HashSet<string> allowedAttributes)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && allowedAttributes.Contains(attribute.Name.LocalName)))
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
