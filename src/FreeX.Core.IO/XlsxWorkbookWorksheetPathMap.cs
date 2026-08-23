using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal sealed class XlsxWorkbookWorksheetPathMap
{
    private XlsxWorkbookWorksheetPathMap(
        IReadOnlyDictionary<string, string> sheetPathsByName,
        IReadOnlyList<XlsxWorkbookWorksheetPath> worksheets)
    {
        SheetPathsByName = sheetPathsByName;
        Worksheets = worksheets;
    }

    public IReadOnlyDictionary<string, string> SheetPathsByName { get; }
    public IReadOnlyList<XlsxWorkbookWorksheetPath> Worksheets { get; }

    public static XlsxWorkbookWorksheetPathMap? TryCreate(
        ZipArchive archive,
        bool rejectDuplicateRelationshipIds = false)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var workbookRelsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || workbookRelsEntry is null)
            return null;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var workbookRels = rejectDuplicateRelationshipIds
            ? XlsxRelationshipReader.LoadTargetsStrict(
                archive,
                "xl/_rels/workbook.xml.rels",
                XlsxPackagePath.NormalizeWorkbookTarget,
                packageRelNs)
            : XlsxRelationshipReader.LoadTargets(
                archive,
                "xl/_rels/workbook.xml.rels",
                "xl/workbook.xml",
                packageRelNs);
        var worksheets = XlsxWorkbookSheetPathReader
            .GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs)
            .Select(pair => new XlsxWorkbookWorksheetPath(pair.SheetName, pair.WorksheetPath))
            .ToList();
        var sheetPaths = worksheets.ToDictionary(
            pair => pair.SheetName,
            pair => pair.WorksheetPath,
            StringComparer.OrdinalIgnoreCase);

        return new XlsxWorkbookWorksheetPathMap(sheetPaths, worksheets);
    }
}

internal readonly record struct XlsxWorkbookWorksheetPath(string SheetName, string WorksheetPath);
