using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookExtensionListNormalizer
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly HashSet<string> NoAttributes = [];
    private static readonly HashSet<string> ExtensionAttributes = ["uri"];

    public static bool NormalizeWorkbookRoot(XElement workbookRoot, XNamespace workbookNs)
    {
        var changed = false;
        var keptExtensionList = false;
        var seenUris = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extensionList in workbookRoot.Elements(workbookNs + "extLst").ToList())
        {
            if (keptExtensionList)
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeExtensionListElement(extensionList, seenUris);
            if (ShouldRemoveExtensionListElement(extensionList))
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            keptExtensionList = true;
        }

        return changed;
    }

    public static bool NormalizeExtensionListElement(XElement extensionList)
    {
        var seenUris = new HashSet<string>(StringComparer.Ordinal);
        return NormalizeExtensionListElement(extensionList, seenUris);
    }

    public static bool ShouldRemoveExtensionListElement(XElement extensionList) =>
        !extensionList.Elements(WorkbookNs + "ext").Any();

    private static bool NormalizeExtensionListElement(XElement extensionList, HashSet<string> seenUris)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(extensionList, NoAttributes);
        changed |= RemoveUnexpectedChildElements(extensionList, WorkbookNs + "ext");

        foreach (var extension in extensionList.Elements(WorkbookNs + "ext").ToList())
        {
            changed |= NormalizeExtensionElement(extension);
            var uri = extension.Attribute("uri")?.Value;
            if (string.IsNullOrWhiteSpace(uri) || !seenUris.Add(uri))
            {
                extension.Remove();
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeExtensionElement(XElement extension)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(extension, ExtensionAttributes);
        changed |= NormalizeUri(extension);
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

    private static bool NormalizeUri(XElement extension)
    {
        var attribute = extension.Attribute("uri");
        var trimmed = attribute?.Value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        if (attribute is not null && string.Equals(attribute.Value, trimmed, StringComparison.Ordinal))
            return false;

        extension.SetAttributeValue("uri", trimmed);
        return true;
    }
}
