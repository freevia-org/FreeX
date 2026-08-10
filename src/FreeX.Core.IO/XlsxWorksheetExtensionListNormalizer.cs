using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetExtensionListNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot) =>
        XlsxExtensionListNormalizer.NormalizeRoot(worksheetRoot, WorksheetNs);

    public static bool NormalizeExtensionListElement(XElement extensionList) =>
        XlsxExtensionListNormalizer.NormalizeElement(extensionList, WorksheetNs);

    public static bool ShouldRemoveExtensionListElement(XElement extensionList) =>
        XlsxExtensionListNormalizer.ShouldRemove(extensionList, WorksheetNs);

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
