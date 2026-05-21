using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxPivotXmlReferencePreserver
{
    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        PreserveWorkbookPivotCaches(sourceArchive, targetArchive, workbookNs);
        PreserveWorksheetPivotTableDefinitions(sourceArchive, targetArchive, workbookNs, relNs, packageRelNs);
    }

    private static void PreserveWorkbookPivotCaches(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XNamespace workbookNs)
    {
        var sourceEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var targetEntry = targetArchive.GetEntry("xl/workbook.xml");
        if (sourceEntry is null || targetEntry is null)
            return;

        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var sourcePivotCaches = sourceXml.Root?.Element(workbookNs + "pivotCaches");
        if (sourcePivotCaches is null)
            return;

        var targetXml = XlsxPackageXmlEditor.LoadXml(targetEntry);
        var targetRoot = targetXml.Root;
        if (targetRoot is null || targetRoot.Element(workbookNs + "pivotCaches") is not null)
            return;

        var sheetsElement = targetRoot.Element(workbookNs + "sheets");
        if (sheetsElement is not null)
            sheetsElement.AddBeforeSelf(new XElement(sourcePivotCaches));
        else
            targetRoot.Add(new XElement(sourcePivotCaches));

        XlsxPackageXmlEditor.ReplaceXml(targetArchive, "xl/workbook.xml", targetXml);
    }

    private static void PreserveWorksheetPivotTableDefinitions(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var sourceWorkbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var sourceWorkbookRelsEntry = sourceArchive.GetEntry("xl/_rels/workbook.xml.rels");
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookRelsEntry = targetArchive.GetEntry("xl/_rels/workbook.xml.rels");
        if (sourceWorkbookEntry is null || sourceWorkbookRelsEntry is null ||
            targetWorkbookEntry is null || targetWorkbookRelsEntry is null)
        {
            return;
        }

        var sourceWorkbookXml = XlsxPackageXmlEditor.LoadXml(sourceWorkbookEntry);
        var sourceWorkbookRels = XlsxRelationshipReader.LoadTargets(
            sourceArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var targetWorkbookXml = XlsxPackageXmlEditor.LoadXml(targetWorkbookEntry);
        var targetWorkbookRels = XlsxRelationshipReader.LoadTargets(
            targetArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);

        var sourceSheets = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(sourceWorkbookXml, sourceWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);
        var targetSheets = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(targetWorkbookXml, targetWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        foreach (var (sheetName, sourceWorksheetPath) in sourceSheets)
        {
            if (!targetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sourceWorksheetEntry = sourceArchive.GetEntry(sourceWorksheetPath);
            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (sourceWorksheetEntry is null || targetWorksheetEntry is null)
                continue;

            var sourceWorksheetXml = XlsxPackageXmlEditor.LoadXml(sourceWorksheetEntry);
            var sourcePivotDefinitions = sourceWorksheetXml.Root?
                .Elements(workbookNs + "pivotTableDefinition")
                .ToList() ?? [];
            if (sourcePivotDefinitions.Count == 0)
                continue;

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null || targetRoot.Elements(workbookNs + "pivotTableDefinition").Any())
                continue;

            foreach (var pivotDefinition in sourcePivotDefinitions)
                targetRoot.Add(new XElement(pivotDefinition));

            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }
}
