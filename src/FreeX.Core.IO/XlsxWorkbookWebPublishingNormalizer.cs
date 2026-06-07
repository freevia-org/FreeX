using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookWebPublishingNormalizer
{
    private static readonly HashSet<string> WebPublishingAttributes =
    [
        "css",
        "thicket",
        "longFileNames",
        "vml",
        "allowPng",
        "targetScreenSize",
        "dpi",
        "codePage",
        "characterSet"
    ];

    private static readonly string[] BooleanAttributes =
    [
        "css",
        "thicket",
        "longFileNames",
        "vml",
        "allowPng"
    ];

    private static readonly string[] UnsignedIntAttributes =
    [
        "dpi",
        "codePage"
    ];

    private static readonly HashSet<string> TargetScreenSizeValues =
    [
        "544x376",
        "640x480",
        "720x512",
        "800x600",
        "1024x768",
        "1152x882",
        "1152x900",
        "1280x1024",
        "1600x1200",
        "1800x1440",
        "1920x1200"
    ];

    public static bool NormalizeWorkbookRoot(XElement workbookRoot, XNamespace workbookNs)
    {
        var changed = false;
        var keptWebPublishing = false;
        foreach (var webPublishing in workbookRoot.Elements(workbookNs + "webPublishing").ToList())
        {
            if (keptWebPublishing)
            {
                webPublishing.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeElement(webPublishing);
            keptWebPublishing = true;
        }

        return changed;
    }

    public static bool NormalizeElement(XElement webPublishing)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(webPublishing);
        changed |= RemoveAllNodes(webPublishing);

        foreach (var attributeName in BooleanAttributes)
            changed |= NormalizeAttribute(webPublishing, attributeName, NormalizeBoolean);
        foreach (var attributeName in UnsignedIntAttributes)
            changed |= NormalizeAttribute(webPublishing, attributeName, NormalizeUnsignedIntOrNull);

        changed |= NormalizeAttribute(webPublishing, "targetScreenSize", NormalizeTargetScreenSize);
        changed |= NormalizeAttribute(webPublishing, "characterSet", NormalizeOptionalText);

        return changed;
    }

    private static bool RemoveUnknownAttributes(XElement element)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && WebPublishingAttributes.Contains(attribute.Name.LocalName)))
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

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeTargetScreenSize(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && TargetScreenSizeValues.Contains(trimmed) ? trimmed : null;
    }

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }
}
