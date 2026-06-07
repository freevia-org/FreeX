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
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(definedNames, NoAttributes);
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
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(definedName, DefinedNameAttributes);
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(definedName);

        foreach (var attributeName in TextAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(definedName, attributeName, NormalizeOptionalText);
        foreach (var attributeName in BooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(definedName, attributeName, XlsxXmlNormalizationHelpers.NormalizeBoolean);
        foreach (var attributeName in UnsignedIntAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(definedName, attributeName, XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);

        return changed;
    }

    private static bool ShouldRemoveDefinedNameElement(XElement definedName) =>
        string.IsNullOrWhiteSpace(definedName.Attribute("name")?.Value);

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

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
