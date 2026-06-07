using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetExtensionListNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly HashSet<string> NoAttributes = [];
    private static readonly HashSet<string> ExtensionAttributes = ["uri"];

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var changed = false;
        var keptExtensionList = false;
        var seenUris = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extensionList in worksheetRoot.Elements(WorksheetNs + "extLst").ToList())
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
        !extensionList.Elements(WorksheetNs + "ext").Any();

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static bool NormalizeExtensionListElement(XElement extensionList, HashSet<string> seenUris)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(extensionList, NoAttributes);
        changed |= RemoveUnexpectedChildElements(extensionList, WorksheetNs + "ext");

        foreach (var extension in extensionList.Elements(WorksheetNs + "ext").ToList())
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

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry) =>
        entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
        entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
        !entry.FullName.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
}
