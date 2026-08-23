using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPackageEditTraversal
{
    public static void EditSourceMapped(
        Stream packageStream,
        Workbook workbook,
        Func<Sheet, bool> shouldEdit,
        Action<Sheet, XElement> editWorksheet)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relationshipsEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var relationshipTargets = relationshipsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(element => element.Attribute("Id") is not null && element.Attribute("Target") is not null)
            .ToDictionary(
                element => element.Attribute("Id")!.Value,
                element => XlsxPackagePath.NormalizeWorkbookTarget(element.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sheetsByName = workbook.Sheets.ToDictionary(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var sheetElement in workbookXml.Root?.Element(workbookNs + "sheets")?.Elements(workbookNs + "sheet") ?? [])
        {
            var name = sheetElement.Attribute("name")?.Value;
            var relationshipId = sheetElement.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(relationshipId) ||
                !sheetsByName.TryGetValue(name, out var sheet) ||
                !shouldEdit(sheet) ||
                !relationshipTargets.TryGetValue(relationshipId, out var worksheetPath))
            {
                continue;
            }

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            editWorksheet(sheet, root);
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    public static void Edit(
        Stream packageStream,
        Workbook workbook,
        Action<XlsxWorksheetXmlEditSession, Sheet, XlsxWorksheetXmlEdit> editWorksheet)
    {
        XlsxWorkbookWorksheetPathMap? worksheetPathMap;
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true))
            worksheetPathMap = XlsxWorkbookWorksheetPathMap.TryCreate(archive);

        Edit(packageStream, workbook, worksheetPathMap, editWorksheet);
    }

    public static void Edit(
        Stream packageStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap,
        Action<XlsxWorksheetXmlEditSession, Sheet, XlsxWorksheetXmlEdit> editWorksheet)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(packageStream, worksheetPathMap);
        Edit(session, workbook, editWorksheet);
    }

    public static void Edit(
        XlsxWorksheetXmlEditSession session,
        Workbook workbook,
        Action<XlsxWorksheetXmlEditSession, Sheet, XlsxWorksheetXmlEdit> editWorksheet)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (session.TryGetWorksheet(sheet, out var edit))
                editWorksheet(session, sheet, edit);
        }
    }
}
