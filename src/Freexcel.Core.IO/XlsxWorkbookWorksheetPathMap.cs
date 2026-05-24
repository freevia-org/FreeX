using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal sealed class XlsxWorkbookWorksheetPathMap
{
    private XlsxWorkbookWorksheetPathMap(IReadOnlyDictionary<string, string> sheetPathsByName)
    {
        SheetPathsByName = sheetPathsByName;
    }

    public IReadOnlyDictionary<string, string> SheetPathsByName { get; }

    public static XlsxWorkbookWorksheetPathMap? TryCreate(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || workbookRelsEntry is null)
            return null;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var workbookRels = XlsxRelationshipReader.LoadTargets(
            archive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var sheetPaths = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        return new XlsxWorkbookWorksheetPathMap(sheetPaths);
    }
}
