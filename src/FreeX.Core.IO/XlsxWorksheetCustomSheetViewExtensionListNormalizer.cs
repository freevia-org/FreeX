using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetCustomSheetViewExtensionListNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var changed = false;
        foreach (var customSheetView in worksheetRoot
                     .Elements(WorksheetNs + "customSheetViews")
                     .Elements(WorksheetNs + "customSheetView"))
        {
            changed |= NormalizeExtensionLists(customSheetView);
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

    private static bool NormalizeExtensionLists(XElement customSheetView)
    {
        var changed = false;
        var keptExtensionList = false;
        foreach (var extensionList in customSheetView.Elements(WorksheetNs + "extLst").ToList())
        {
            if (keptExtensionList)
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxWorksheetExtensionListNormalizer.NormalizeExtensionListElement(extensionList);
            if (XlsxWorksheetExtensionListNormalizer.ShouldRemoveExtensionListElement(extensionList))
            {
                extensionList.Remove();
                changed = true;
                continue;
            }

            keptExtensionList = true;
        }

        return changed;
    }

}
