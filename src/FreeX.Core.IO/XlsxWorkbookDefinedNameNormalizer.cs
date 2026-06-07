using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookDefinedNameNormalizer
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly HashSet<string> NoAttributes = [];

    private static readonly HashSet<string> DefinedNameAttributes =
    [
        "name",
        "comment",
        "customMenu",
        "description",
        "help",
        "statusBar",
        "localSheetId",
        "hidden",
        "function",
        "vbProcedure",
        "xlm",
        "functionGroupId",
        "shortcutKey",
        "publishToServer",
        "workbookParameter"
    ];

    private static readonly string[] BooleanAttributes =
    [
        "hidden",
        "function",
        "vbProcedure",
        "xlm",
        "publishToServer",
        "workbookParameter"
    ];

    private static readonly string[] UnsignedIntAttributes =
    [
        "localSheetId",
        "functionGroupId"
    ];

    private static readonly string[] TextAttributes =
    [
        "name",
        "comment",
        "customMenu",
        "description",
        "help",
        "statusBar",
        "shortcutKey"
    ];

    public static bool NormalizeDefinedNamesElement(XElement definedNames)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(definedNames, NoAttributes);
        changed |= RemoveUnexpectedChildElements(definedNames, WorkbookNs + "definedName");

        foreach (var definedName in definedNames.Elements(WorkbookNs + "definedName").ToList())
        {
            changed |= NormalizeDefinedNameElement(definedName);
            if (!ShouldRemoveDefinedNameElement(definedName))
                continue;

            definedName.Remove();
            changed = true;
        }

        return changed;
    }

    public static bool ShouldRemoveDefinedNamesElement(XElement definedNames) =>
        !definedNames.Elements(WorkbookNs + "definedName").Any();

    public static bool NormalizeDefinedNameElement(XElement definedName)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(definedName, DefinedNameAttributes);
        changed |= RemoveElementChildren(definedName);

        foreach (var attributeName in TextAttributes)
            changed |= NormalizeAttribute(definedName, attributeName, NormalizeOptionalText);
        foreach (var attributeName in BooleanAttributes)
            changed |= NormalizeAttribute(definedName, attributeName, NormalizeBoolean);
        foreach (var attributeName in UnsignedIntAttributes)
            changed |= NormalizeAttribute(definedName, attributeName, NormalizeUnsignedIntOrNull);

        return changed;
    }

    private static bool ShouldRemoveDefinedNameElement(XElement definedName) =>
        string.IsNullOrWhiteSpace(definedName.Attribute("name")?.Value);

    private static bool RemoveUnknownAttributes(XElement element, IReadOnlySet<string> allowedAttributes)
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

    private static bool RemoveUnexpectedChildElements(XElement element, XName allowedChildName)
    {
        var changed = false;
        foreach (var child in element.Elements().Where(child => child.Name != allowedChildName).ToList())
        {
            child.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveElementChildren(XElement element)
    {
        var changed = false;
        foreach (var child in element.Elements().ToList())
        {
            child.Remove();
            changed = true;
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

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }
}
