using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxPivotXmlReferencePreserver
{
    private const string PivotCacheDefinitionRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition";
    private const string PivotTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable";

    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        PreserveWorkbookPivotCaches(sourceArchive, targetArchive, workbookNs, relNs, packageRelNs);
        PreserveWorksheetPivotTableDefinitions(sourceArchive, targetArchive, workbookNs, relNs, packageRelNs);
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

        var pivotWorksheetPaths = GetWorksheetPathsWithPivotTableRelationships(sourceArchive, context);
        if (pivotWorksheetPaths.Count == 0)
            return;

        PreserveWorkbookPivotCaches(sourceArchive, targetArchive, context);
        PreserveWorksheetPivotTableDefinitions(sourceArchive, targetArchive, context, pivotWorksheetPaths);
    }

    private static void PreserveWorkbookPivotCaches(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
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

        var remappedPivotCaches = RemapWorkbookPivotCaches(
            sourcePivotCaches,
            sourceArchive,
            targetArchive,
            relNs,
            packageRelNs);

        // Per CT_Workbook, <pivotCaches> must come after <sheets> (and customWorkbookViews) and before
        // smartTagPr/webPublishing/extLst. Inserting it before <sheets> is schema-invalid and makes
        // Excel reject the workbook and drop every PivotTable.
        XlsxPivotTableWriter.InsertWorkbookPivotCaches(targetRoot, workbookNs, remappedPivotCaches);

        XlsxPackageXmlEditor.ReplaceXml(targetArchive, "xl/workbook.xml", targetXml);
    }

    private static void PreserveWorkbookPivotCaches(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext context)
    {
        var sourcePivotCaches = context.SourceWorkbookXml.Root?.Element(context.WorkbookNs + "pivotCaches");
        if (sourcePivotCaches is null)
            return;

        var targetRoot = context.TargetWorkbookXml.Root;
        if (targetRoot is null || targetRoot.Element(context.WorkbookNs + "pivotCaches") is not null)
            return;

        var remappedPivotCaches = RemapWorkbookPivotCaches(
            sourcePivotCaches,
            sourceArchive,
            targetArchive,
            context.RelNs,
            context.PackageRelNs);

        // Per CT_Workbook, <pivotCaches> must come after <sheets> (and customWorkbookViews) and before
        // smartTagPr/webPublishing/extLst. Inserting it before <sheets> is schema-invalid and makes
        // Excel reject the workbook and drop every PivotTable.
        XlsxPivotTableWriter.InsertWorkbookPivotCaches(targetRoot, context.WorkbookNs, remappedPivotCaches);

        XlsxPackageXmlEditor.ReplaceXml(targetArchive, "xl/workbook.xml", context.TargetWorkbookXml);
    }

    private static XElement RemapWorkbookPivotCaches(
        XElement sourcePivotCaches,
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var pivotCaches = new XElement(sourcePivotCaches);
        var sourceWorkbookRels = LoadRelationshipElements(sourceArchive, "xl/_rels/workbook.xml.rels", packageRelNs);
        var targetWorkbookRels = LoadRelationshipElements(targetArchive, "xl/_rels/workbook.xml.rels", packageRelNs);
        if (sourceWorkbookRels.Count == 0)
            return pivotCaches;

        foreach (var pivotCache in pivotCaches.Elements(sourcePivotCaches.Name.Namespace + "pivotCache"))
        {
            var sourceRelId = pivotCache.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(sourceRelId))
                continue;

            var sourceTarget = sourceWorkbookRels
                .Where(relationship => IsRelationshipType(relationship, PivotCacheDefinitionRelationshipType))
                .Where(relationship => string.Equals(relationship.Attribute("Id")?.Value, sourceRelId, StringComparison.Ordinal))
                .Select(relationship => ResolveRelationshipTarget("xl/workbook.xml", relationship))
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(sourceTarget))
                continue;

            var targetRelId = targetWorkbookRels
                .Where(relationship => IsRelationshipType(relationship, PivotCacheDefinitionRelationshipType))
                .Where(relationship => string.Equals(
                    ResolveRelationshipTarget("xl/workbook.xml", relationship),
                    sourceTarget,
                    StringComparison.OrdinalIgnoreCase))
                .Select(relationship => relationship.Attribute("Id")?.Value)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
            if (!string.IsNullOrWhiteSpace(targetRelId))
                pivotCache.SetAttributeValue(relNs + "id", targetRelId);
        }

        return pivotCaches;
    }

    private static List<XElement> LoadRelationshipElements(
        ZipArchive archive,
        string relationshipsPath,
        XNamespace packageRelNs)
    {
        var entry = archive.GetEntry(relationshipsPath);
        if (entry is null)
            return [];

        var xml = XlsxPackageXmlEditor.LoadXml(entry);
        return xml.Root?.Elements(packageRelNs + "Relationship").ToList() ?? [];
    }

    private static bool IsRelationshipType(XElement relationship, string relationshipType) =>
        string.Equals(relationship.Attribute("Type")?.Value, relationshipType, StringComparison.OrdinalIgnoreCase);

    private static string ResolveRelationshipTarget(string sourcePart, XElement relationship)
    {
        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return "";

        return XlsxPackagePath.ResolveRelationshipTarget(sourcePart, target.Trim().Replace('\\', '/'));
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

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null || targetRoot.Elements(workbookNs + "pivotTableDefinition").Any())
                continue;

            foreach (var pivotDefinition in sourcePivotDefinitions)
            {
                targetRoot.Add(RemapPivotDefinitionRelationship(
                    pivotDefinition,
                    sourceWorksheetRels,
                    targetWorksheetRelsXml,
                    targetWorksheetPath,
                    relNs,
                    packageRelNs));
            }

            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }

    private static void PreserveWorksheetPivotTableDefinitions(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext context,
        IReadOnlySet<string> pivotWorksheetPaths)
    {
        foreach (var (sheetName, sourceWorksheetPath) in context.SourceSheets)
        {
            if (!pivotWorksheetPaths.Contains(sourceWorksheetPath))
                continue;

            if (!context.TargetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceArchive, sourceWorksheetPath);
            if (sourceWorksheetXml is null || targetWorksheetEntry is null)
                continue;

            var sourcePivotDefinitions = sourceWorksheetXml.Root?
                .Elements(context.WorkbookNs + "pivotTableDefinition")
                .ToList() ?? [];
            if (sourcePivotDefinitions.Count == 0)
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

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null || targetRoot.Elements(context.WorkbookNs + "pivotTableDefinition").Any())
                continue;

            foreach (var pivotDefinition in sourcePivotDefinitions)
            {
                targetRoot.Add(RemapPivotDefinitionRelationship(
                    pivotDefinition,
                    sourceWorksheetRels,
                    targetWorksheetRelsXml,
                    targetWorksheetPath,
                    context.RelNs,
                    context.PackageRelNs));
            }

            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }

    private static XElement RemapPivotDefinitionRelationship(
        XElement sourcePivotDefinition,
        IReadOnlyDictionary<string, string> sourceWorksheetRels,
        XDocument targetWorksheetRelsXml,
        string targetWorksheetPath,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var pivotDefinition = new XElement(sourcePivotDefinition);
        var sourceRelId = sourcePivotDefinition.Attribute(relNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(sourceRelId) ||
            !sourceWorksheetRels.TryGetValue(sourceRelId, out var pivotTablePath))
        {
            return pivotDefinition;
        }

        var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            targetWorksheetRelsXml,
            packageRelNs,
            targetWorksheetPath,
            pivotTablePath,
            PivotTableRelationshipType);
        pivotDefinition.SetAttributeValue(relNs + "id", targetRelId);
        return pivotDefinition;
    }

    private static bool HasWorksheetPivotTableRelationships(
        ZipArchive sourceArchive,
        XlsxSourcePackagePreservationContext context) =>
        GetWorksheetPathsWithPivotTableRelationships(sourceArchive, context).Count > 0;

    private static HashSet<string> GetWorksheetPathsWithPivotTableRelationships(
        ZipArchive sourceArchive,
        XlsxSourcePackagePreservationContext context)
    {
        var worksheetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceWorksheetPath in context.SourceSheets.Values)
        {
            var relationshipsEntry = sourceArchive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath));
            if (relationshipsEntry is null)
                continue;

            using var relationshipsStream = relationshipsEntry.Open();
            using var reader = new StreamReader(relationshipsStream);
            if (reader.ReadToEnd().Contains(PivotTableRelationshipType, StringComparison.OrdinalIgnoreCase))
                worksheetPaths.Add(sourceWorksheetPath);
        }

        return worksheetPaths;
    }
}
