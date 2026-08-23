using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetExtensionListNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot) =>
        NormalizeChildren(worksheetRoot);

    public static bool NormalizeChildren(XElement parent) =>
        XlsxExtensionListNormalizer.NormalizeRoot(parent, WorksheetNs);

    public static bool NormalizeExtensionListElement(XElement extensionList) =>
        XlsxExtensionListNormalizer.NormalizeElement(extensionList, WorksheetNs);

    public static bool ShouldRemoveExtensionListElement(XElement extensionList) =>
        XlsxExtensionListNormalizer.ShouldRemove(extensionList, WorksheetNs);

    public static bool NormalizeChild(XElement extensionList, ref bool keptExtensionList)
    {
        if (keptExtensionList)
        {
            extensionList.Remove();
            return true;
        }

        var changed = NormalizeExtensionListElement(extensionList);
        if (ShouldRemoveExtensionListElement(extensionList))
        {
            extensionList.Remove();
            return true;
        }

        keptExtensionList = true;
        return changed;
    }

    public static bool RemoveDuplicateChildren(XElement parent, string localName)
    {
        var changed = false;
        var seen = false;
        foreach (var child in parent.Elements(WorksheetNs + localName).ToList())
        {
            if (!seen)
            {
                seen = true;
                continue;
            }

            child.Remove();
            changed = true;
        }

        return changed;
    }

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

}
