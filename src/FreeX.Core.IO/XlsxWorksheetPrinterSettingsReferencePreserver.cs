using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPrinterSettingsReferencePreserver
{
    private const string PrinterSettingsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings";

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
        var targetWorkbookXml = XlsxPackageXmlEditor.LoadXml(targetWorkbookEntry);
        var sourceWorkbookRels = XlsxRelationshipReader.LoadTargets(
            sourceArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
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
            var sourcePageSetup = sourceWorksheetXml.Root?.Element(workbookNs + "pageSetup");
            var sourceRelId = sourcePageSetup?.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(sourceRelId))
            {
                RemoveInvalidPageSetupRelationshipId(
                    targetArchive,
                    targetWorksheetPath,
                    workbookNs,
                    relNs,
                    packageRelNs);
                continue;
            }

            if (!TryGetPrinterSettingsTarget(
                    sourceArchive,
                    XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath),
                    sourceWorksheetPath,
                    sourceRelId,
                    packageRelNs,
                    out var printerSettingsPath) ||
                !printerSettingsPath.StartsWith("xl/printerSettings/", StringComparison.OrdinalIgnoreCase) ||
                targetArchive.GetEntry(printerSettingsPath) is null)
            {
                RemoveInvalidPageSetupRelationshipId(
                    targetArchive,
                    targetWorksheetPath,
                    workbookNs,
                    relNs,
                    packageRelNs);
                continue;
            }

            var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
            var targetWorksheetRelsXml = targetArchive.GetEntry(targetWorksheetRelsPath) is { } targetWorksheetRelsEntry
                ? XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                targetWorksheetRelsXml,
                packageRelNs,
                targetWorksheetPath,
                printerSettingsPath,
                PrinterSettingsRelationshipType);
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            var targetPageSetup = targetRoot.Element(workbookNs + "pageSetup");
            if (targetPageSetup is null)
            {
                targetPageSetup = new XElement(workbookNs + "pageSetup");
                targetRoot.Add(targetPageSetup);
            }

            targetRoot.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
            targetPageSetup.SetAttributeValue(relNs + "id", targetRelId);
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }

    private static void RemoveInvalidPageSetupRelationshipId(
        ZipArchive targetArchive,
        string targetWorksheetPath,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
        if (targetWorksheetEntry is null)
            return;

        var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
        var pageSetup = targetWorksheetXml.Root?.Element(workbookNs + "pageSetup");
        var relationshipId = pageSetup?.Attribute(relNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(relationshipId))
            return;

        if (TargetRelationshipIsPrinterSettings(
                targetArchive,
                targetWorksheetPath,
                relationshipId,
                packageRelNs))
        {
            return;
        }

        pageSetup!.Attribute(relNs + "id")?.Remove();
        XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
    }

    private static bool TargetRelationshipIsPrinterSettings(
        ZipArchive archive,
        string worksheetPath,
        string relationshipId,
        XNamespace packageRelNs)
    {
        if (!TryGetPrinterSettingsTarget(
                archive,
                XlsxPackagePath.GetRelationshipPartPath(worksheetPath),
                worksheetPath,
                relationshipId,
                packageRelNs,
                out var printerSettingsPath))
        {
            return false;
        }

        return printerSettingsPath.StartsWith("xl/printerSettings/", StringComparison.OrdinalIgnoreCase) &&
               archive.GetEntry(printerSettingsPath) is not null;
    }

    private static bool TryGetPrinterSettingsTarget(
        ZipArchive archive,
        string relationshipsPath,
        string sourcePartPath,
        string relationshipId,
        XNamespace packageRelNs,
        out string targetPath)
    {
        targetPath = "";
        var relationshipsEntry = archive.GetEntry(relationshipsPath);
        if (relationshipsEntry is null)
            return false;

        var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
        var relationship = relationshipsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal) &&
                string.Equals(candidate.Attribute("Type")?.Value, PrinterSettingsRelationshipType, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candidate.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase));
        var target = relationship?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return false;

        targetPath = XlsxPackagePath.ResolveRelationshipTarget(sourcePartPath, target);
        return !string.IsNullOrWhiteSpace(targetPath);
    }
}
