using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookProtectionNormalizer
{
    private static readonly HashSet<string> WorkbookProtectionAttributes =
    [
        "workbookPassword",
        "revisionsPassword",
        "lockStructure",
        "lockWindows",
        "lockRevision",
        "revisionsAlgorithmName",
        "revisionsHashValue",
        "revisionsSaltValue",
        "revisionsSpinCount",
        "workbookAlgorithmName",
        "workbookHashValue",
        "workbookSaltValue",
        "workbookSpinCount"
    ];

    private static readonly string[] BooleanAttributes =
    [
        "lockStructure",
        "lockWindows",
        "lockRevision"
    ];

    private static readonly string[] UnsignedIntAttributes =
    [
        "workbookSpinCount",
        "revisionsSpinCount"
    ];

    public static bool NormalizeElement(XElement workbookProtection)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(workbookProtection);
        changed |= RemoveAllNodes(workbookProtection);

        foreach (var attributeName in BooleanAttributes)
            changed |= NormalizeAttribute(workbookProtection, attributeName, NormalizeBoolean);
        foreach (var attributeName in UnsignedIntAttributes)
            changed |= NormalizeAttribute(workbookProtection, attributeName, NormalizeUnsignedIntOrNull);

        return changed;
    }

    private static bool RemoveUnknownAttributes(XElement element)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && WorkbookProtectionAttributes.Contains(attribute.Name.LocalName)))
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

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }
}
