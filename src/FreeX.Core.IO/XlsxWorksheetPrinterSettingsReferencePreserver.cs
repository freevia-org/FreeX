using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPrinterSettingsReferencePreserver
{
    private const string PrinterSettingsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings";
    private const string PrinterSettingsContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.printerSettings";

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
            // R102-io-rename-worksheet-exclusion-sweep-1: sheetName is the LOAD-TIME name; this method
            // (unlike its sibling preservers) builds its own local sourceSheets/targetSheets rather
            // than sharing XlsxSourcePackagePreservationContext, but suffers the identical bug -- a
            // renamed sheet's load-time name no longer resolves in the current-name-keyed targetSheets,
            // indistinguishable from a delete, silently dropping the sheet's printer-settings binding.
            // A plain rename never changes the sheet's own worksheetN.xml part path, so fall back to
            // matching on that path when the name lookup fails.
            if (!targetSheets.TryGetValue(sheetName, out var targetWorksheetPath) &&
                !TryResolveTargetWorksheetPathByPath(sourceSheets, targetSheets, sourceWorksheetPath, out targetWorksheetPath))
            {
                continue;
            }

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
            if (string.IsNullOrWhiteSpace(targetRelId))
            {
                PruneWorksheetPrinterSettingsRelationships(
                    targetWorksheetRelsXml,
                    packageRelNs,
                    targetWorksheetPath,
                    keptRelationshipId: null,
                    keptPrinterSettingsPath: null);
                targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                    targetWorksheetRelsXml,
                    packageRelNs,
                    targetWorksheetPath,
                    printerSettingsPath,
                    PrinterSettingsRelationshipType);
            }

            PruneWorksheetPrinterSettingsRelationships(
                targetWorksheetRelsXml,
                packageRelNs,
                targetWorksheetPath,
                targetRelId,
                printerSettingsPath);
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);
            XlsxPackageXmlEditor.EnsureSpecificContentType(targetArchive, printerSettingsPath, PrinterSettingsContentType);

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

    private static bool TryResolveTargetWorksheetPathByPath(
        IReadOnlyDictionary<string, string> sourceSheets,
        IReadOnlyDictionary<string, string> targetSheets,
        string sourceWorksheetPath,
        out string targetWorksheetPath)
    {
        var normalizedSourcePath = XlsxPackagePath.NormalizePackagePath(sourceWorksheetPath);
        foreach (var (candidateName, candidatePath) in targetSheets)
        {
            // R102-io-rename-worksheet-exclusion-sweep-1-falsepositive: reject a candidate whose name
            // already existed at load time -- its path coincidence is a renumbering shift of that
            // (still-existing, matched-by-name) sheet, not evidence of a rename. See
            // XlsxRenamedSourceSheetResolver's header comment for the concrete delete+renumber repro.
            if (sourceSheets.ContainsKey(candidateName))
                continue;

            if (string.Equals(
                    XlsxPackagePath.NormalizePackagePath(candidatePath),
                    normalizedSourcePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                targetWorksheetPath = candidatePath;
                return true;
            }
        }

        targetWorksheetPath = "";
        return false;
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
        if (!string.IsNullOrWhiteSpace(relationshipId))
        {
            pageSetup!.Attribute(relNs + "id")?.Remove();
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }

        var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
        if (targetArchive.GetEntry(targetWorksheetRelsPath) is not { } targetWorksheetRelsEntry)
            return;

        var targetWorksheetRelsXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry);
        if (PruneWorksheetPrinterSettingsRelationships(
                targetWorksheetRelsXml,
                packageRelNs,
                targetWorksheetPath,
                keptRelationshipId: null,
                keptPrinterSettingsPath: null))
        {
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);
        }
    }

    private static bool PruneWorksheetPrinterSettingsRelationships(
        XDocument worksheetRelsXml,
        XNamespace packageRelNs,
        string worksheetPath,
        string? keptRelationshipId,
        string? keptPrinterSettingsPath)
    {
        var root = worksheetRelsXml.Root;
        if (root is null)
            return false;

        var changed = false;
        foreach (var relationship in root.Elements(packageRelNs + "Relationship").ToList())
        {
            if (!string.Equals(
                    relationship.Attribute("Type")?.Value,
                    PrinterSettingsRelationshipType,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ShouldKeepPrinterSettingsRelationship(
                    relationship,
                    worksheetPath,
                    keptRelationshipId,
                    keptPrinterSettingsPath))
            {
                continue;
            }

            relationship.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool ShouldKeepPrinterSettingsRelationship(
        XElement relationship,
        string worksheetPath,
        string? keptRelationshipId,
        string? keptPrinterSettingsPath)
    {
        if (string.IsNullOrWhiteSpace(keptRelationshipId) ||
            string.IsNullOrWhiteSpace(keptPrinterSettingsPath) ||
            !string.Equals(relationship.Attribute("Id")?.Value, keptRelationshipId, StringComparison.Ordinal) ||
            string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return false;

        var resolvedTarget = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
        return string.Equals(resolvedTarget, keptPrinterSettingsPath, StringComparison.OrdinalIgnoreCase);
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
        XElement? relationship = null;
        if (relationshipsXml.Root is not null)
        {
            foreach (var candidate in relationshipsXml.Root.Elements(packageRelNs + "Relationship"))
            {
                if (string.Equals(candidate.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal) &&
                    string.Equals(candidate.Attribute("Type")?.Value, PrinterSettingsRelationshipType, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(candidate.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
                {
                    relationship = candidate;
                    break;
                }
            }
        }

        var target = relationship?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return false;

        targetPath = XlsxPackagePath.ResolveRelationshipTarget(sourcePartPath, target);
        return !string.IsNullOrWhiteSpace(targetPath);
    }
}
