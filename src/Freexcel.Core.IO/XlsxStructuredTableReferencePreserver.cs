using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxStructuredTableReferencePreserver
{
    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

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
            var sourceTableParts = sourceWorksheetXml.Root?
                .Element(workbookNs + "tableParts")?
                .Elements(workbookNs + "tablePart")
                .ToList() ?? [];
            if (sourceTableParts.Count == 0)
                continue;

            var sourceWorksheetRels = XlsxRelationshipReader.LoadTargets(
                sourceArchive,
                XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath),
                sourceWorksheetPath,
                packageRelNs);
            var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
            var targetWorksheetRelsEntry = targetArchive.GetEntry(targetWorksheetRelsPath);
            var targetWorksheetRelsXml = targetWorksheetRelsEntry is not null
                ? XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));

            var preservedTableParts = new List<XElement>();
            foreach (var sourceTablePart in sourceTableParts)
            {
                var sourceRelId = sourceTablePart.Attribute(relNs + "id")?.Value;
                if (string.IsNullOrWhiteSpace(sourceRelId) ||
                    !sourceWorksheetRels.TryGetValue(sourceRelId, out var tablePath))
                {
                    continue;
                }

                var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                    targetWorksheetRelsXml,
                    packageRelNs,
                    targetWorksheetPath,
                    tablePath,
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table");
                preservedTableParts.Add(new XElement(workbookNs + "tablePart", new XAttribute(relNs + "id", targetRelId)));
            }

            if (preservedTableParts.Count == 0)
                continue;

            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            targetRoot.Elements(workbookNs + "tableParts").Remove();
            targetRoot.Add(new XElement(
                workbookNs + "tableParts",
                new XAttribute("count", preservedTableParts.Count.ToString(CultureInfo.InvariantCulture)),
                preservedTableParts));
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }

    public static void Preserve(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext? context)
    {
        if (context is null)
        {
            Preserve(sourceArchive, targetArchive);
            return;
        }

        foreach (var (sheetName, sourceWorksheetPath) in context.SourceSheets)
        {
            if (!context.TargetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sourceWorksheetEntry = sourceArchive.GetEntry(sourceWorksheetPath);
            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (sourceWorksheetEntry is null || targetWorksheetEntry is null)
                continue;

            var sourceWorksheetXml = XlsxPackageXmlEditor.LoadXml(sourceWorksheetEntry);
            var sourceTableParts = sourceWorksheetXml.Root?
                .Element(context.WorkbookNs + "tableParts")?
                .Elements(context.WorkbookNs + "tablePart")
                .ToList() ?? [];
            if (sourceTableParts.Count == 0)
                continue;

            var sourceWorksheetRels = XlsxRelationshipReader.LoadTargets(
                sourceArchive,
                XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath),
                sourceWorksheetPath,
                context.PackageRelNs);
            var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
            var targetWorksheetRelsEntry = targetArchive.GetEntry(targetWorksheetRelsPath);
            var targetWorksheetRelsXml = targetWorksheetRelsEntry is not null
                ? XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry)
                : new XDocument(new XElement(context.PackageRelNs + "Relationships"));

            var preservedTableParts = new List<XElement>();
            foreach (var sourceTablePart in sourceTableParts)
            {
                var sourceRelId = sourceTablePart.Attribute(context.RelNs + "id")?.Value;
                if (string.IsNullOrWhiteSpace(sourceRelId) ||
                    !sourceWorksheetRels.TryGetValue(sourceRelId, out var tablePath))
                {
                    continue;
                }

                var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                    targetWorksheetRelsXml,
                    context.PackageRelNs,
                    targetWorksheetPath,
                    tablePath,
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table");
                preservedTableParts.Add(new XElement(context.WorkbookNs + "tablePart", new XAttribute(context.RelNs + "id", targetRelId)));
            }

            if (preservedTableParts.Count == 0)
                continue;

            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            targetRoot.Elements(context.WorkbookNs + "tableParts").Remove();
            targetRoot.Add(new XElement(
                context.WorkbookNs + "tableParts",
                new XAttribute("count", preservedTableParts.Count.ToString(CultureInfo.InvariantCulture)),
                preservedTableParts));
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }
}
