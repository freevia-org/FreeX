using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPrinterSettingsReferencePreserver
{
    private const string PrinterSettingsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings";
    private const string PrinterSettingsContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.printerSettings";

    public static void Preserve(XlsxSourcePackagePreservationContext? context)
    {
        if (context is null)
            return;

        var sourceArchive = context.SourceArchive;
        var targetArchive = context.TargetArchive;
        var workbookNs = context.WorkbookNs;
        var relNs = context.RelNs;
        var packageRelNs = context.PackageRelNs;
        var sourceSheets = context.SourceSheets;
        var targetSheets = context.TargetSheets;

        foreach (var (sheetName, sourceWorksheetPath) in sourceSheets)
        {
            // R105: rename-tolerant lookup removed as inert (proven dead this round) --
            // XlsxPackageMetadataMerger.MergeRelationshipParts merges each worksheet .rels keyed by
            // part path (rename-stable), so the relationship arrives immune to this bug class; the
            // pageSetup/@r:id attribute is separately carried by the metadata preserver's own
            // already-correct native-attribute merge.
            if (!targetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sourceWorksheetEntry = sourceArchive.GetEntry(sourceWorksheetPath);
            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (sourceWorksheetEntry is null || targetWorksheetEntry is null)
                continue;

            var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceWorksheetPath)!;
            var sourcePageSetup = sourceWorksheetXml.Root?.Element(workbookNs + "pageSetup");
            var sourceRelId = sourcePageSetup?.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(sourceRelId))
            {
                RemoveInvalidPageSetupRelationshipId(
                    context,
                    targetWorksheetPath,
                    workbookNs,
                    relNs);
                continue;
            }

            if (!context.TryGetSourceRelationshipTarget(
                    sourceWorksheetPath,
                    sourceRelId,
                    PrinterSettingsRelationshipType,
                    out var printerSettingsPath) ||
                !printerSettingsPath.StartsWith("xl/printerSettings/", StringComparison.OrdinalIgnoreCase) ||
                targetArchive.GetEntry(printerSettingsPath) is null)
            {
                RemoveInvalidPageSetupRelationshipId(
                    context,
                    targetWorksheetPath,
                    workbookNs,
                    relNs);
                continue;
            }

            var (targetWorksheetRelsPath, targetWorksheetRelsXml) =
                context.LoadOrCreateTargetRelationships(targetWorksheetPath);
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
            context.ReplaceTargetPartXml(targetWorksheetRelsPath, targetWorksheetRelsXml);
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
            context.ReplaceTargetPartXml(targetWorksheetPath, targetWorksheetXml);
        }
    }

    private static void RemoveInvalidPageSetupRelationshipId(
        XlsxSourcePackagePreservationContext context,
        string targetWorksheetPath,
        XNamespace workbookNs,
        XNamespace relNs)
    {
        var targetArchive = context.TargetArchive;
        var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
        if (targetWorksheetEntry is null)
            return;

        var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
        var pageSetup = targetWorksheetXml.Root?.Element(workbookNs + "pageSetup");
        var relationshipId = pageSetup?.Attribute(relNs + "id")?.Value;
        if (!string.IsNullOrWhiteSpace(relationshipId))
        {
            pageSetup!.Attribute(relNs + "id")?.Remove();
            context.ReplaceTargetPartXml(targetWorksheetPath, targetWorksheetXml);
        }

        var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
        if (targetArchive.GetEntry(targetWorksheetRelsPath) is not { } targetWorksheetRelsEntry)
            return;

        var targetWorksheetRelsXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry);
        if (PruneWorksheetPrinterSettingsRelationships(
                targetWorksheetRelsXml,
                context.PackageRelNs,
                targetWorksheetPath,
                keptRelationshipId: null,
                keptPrinterSettingsPath: null))
        {
            context.ReplaceTargetPartXml(targetWorksheetRelsPath, targetWorksheetRelsXml);
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

}
