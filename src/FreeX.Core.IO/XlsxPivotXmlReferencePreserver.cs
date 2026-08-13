using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxPivotXmlReferencePreserver
{
    private const string PivotCacheDefinitionRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition";
    private const string PivotTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable";

    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, targetArchive);
        Preserve(context);
    }

    public static void Preserve(XlsxSourcePackagePreservationContext? context)
    {
        if (context is null)
            return;

        PreserveWorkbookPivotCaches(context);

        var pivotWorksheetPaths = GetWorksheetPathsWithPivotTableRelationships(context);
        if (pivotWorksheetPaths.Count == 0)
            return;

        PreserveWorksheetPivotTableDefinitions(context, pivotWorksheetPaths);
    }

    private static void PreserveWorkbookPivotCaches(XlsxSourcePackagePreservationContext context)
    {
        var sourcePivotCaches = context.SourceWorkbookXml.Root?.Element(context.WorkbookNs + "pivotCaches");
        if (sourcePivotCaches is null)
            return;

        var targetWorkbookXml = context.LoadCurrentTargetWorkbookXml();
        var targetRoot = targetWorkbookXml.Root;
        if (targetRoot is null || targetRoot.Element(context.WorkbookNs + "pivotCaches") is not null)
            return;

        var remappedPivotCaches = RemapWorkbookPivotCaches(
            sourcePivotCaches,
            context);

        // Per CT_Workbook, <pivotCaches> must come after <sheets> (and customWorkbookViews) and before
        // smartTagPr/webPublishing/extLst. Inserting it before <sheets> is schema-invalid and makes
        // Excel reject the workbook and drop every PivotTable.
        XlsxPivotTableWriter.InsertWorkbookPivotCaches(targetRoot, context.WorkbookNs, remappedPivotCaches);

        context.ReplaceTargetWorkbookXml(targetWorkbookXml);
    }

    private static XElement RemapWorkbookPivotCaches(
        XElement sourcePivotCaches,
        XlsxSourcePackagePreservationContext context)
    {
        var pivotCaches = new XElement(sourcePivotCaches);
        var sourceWorkbookRels = context.SourceWorkbookRelationshipsXml?.Root?
            .Elements(context.PackageRelNs + "Relationship")
            .ToList() ?? [];
        var targetWorkbookRels = context.HasTargetWorkbookRelationshipsPart
            ? context.LoadCurrentTargetWorkbookRelationshipsXml().Root?
                .Elements(context.PackageRelNs + "Relationship")
                .ToList() ?? []
            : [];
        if (sourceWorkbookRels.Count == 0)
            return pivotCaches;

        foreach (var pivotCache in pivotCaches.Elements(sourcePivotCaches.Name.Namespace + "pivotCache"))
        {
            var sourceRelId = pivotCache.Attribute(context.RelNs + "id")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(sourceRelId))
                continue;

            var sourceTarget = FindPivotCacheDefinitionTarget(sourceWorkbookRels, sourceRelId);
            if (string.IsNullOrWhiteSpace(sourceTarget))
                continue;

            var targetRelId = FindPivotCacheDefinitionRelationshipId(targetWorkbookRels, sourceTarget);
            if (!string.IsNullOrWhiteSpace(targetRelId))
                pivotCache.SetAttributeValue(context.RelNs + "id", targetRelId);
        }

        return pivotCaches;
    }

    private static string? FindPivotCacheDefinitionTarget(
        IReadOnlyList<XElement> relationships,
        string sourceRelId)
    {
        foreach (var relationship in relationships)
        {
            if (!IsRelationshipType(relationship, PivotCacheDefinitionRelationshipType))
                continue;

            if (string.Equals(relationship.Attribute("Id")?.Value, sourceRelId, StringComparison.Ordinal))
                return ResolveRelationshipTarget("xl/workbook.xml", relationship);
        }

        return null;
    }

    private static string? FindPivotCacheDefinitionRelationshipId(
        IReadOnlyList<XElement> relationships,
        string sourceTarget)
    {
        foreach (var relationship in relationships)
        {
            if (!IsRelationshipType(relationship, PivotCacheDefinitionRelationshipType))
                continue;

            if (!string.Equals(
                ResolveRelationshipTarget("xl/workbook.xml", relationship),
                sourceTarget,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relationshipId = relationship.Attribute("Id")?.Value;
            if (!string.IsNullOrWhiteSpace(relationshipId))
                return relationshipId;
        }

        return null;
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
        XlsxSourcePackagePreservationContext context,
        IReadOnlySet<string> pivotWorksheetPaths)
    {
        var targetArchive = context.TargetArchive;
        foreach (var (sheetName, sourceWorksheetPath) in context.SourceSheets)
        {
            if (!pivotWorksheetPaths.Contains(sourceWorksheetPath))
                continue;

            // R105: rename-tolerant lookup removed as inert (proven dead this round) -- the guarded
            // code below only fires when a worksheet has a <pivotTableDefinition> as a direct child
            // element, which is not producible OOXML: a pivot definition is always its own part
            // referenced by relationship, so this gate can never be true.
            if (!context.TargetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceWorksheetPath);
            if (sourceWorksheetXml is null || targetWorksheetEntry is null)
                continue;

            var sourcePivotDefinitions = sourceWorksheetXml.Root?
                .Elements(context.WorkbookNs + "pivotTableDefinition")
                .ToList() ?? [];
            if (sourcePivotDefinitions.Count == 0)
                continue;

            var sourceWorksheetRels = context.GetSourceRelationshipTargets(sourceWorksheetPath);
            var (targetWorksheetRelsPath, targetWorksheetRelsXml) =
                context.LoadOrCreateTargetRelationships(targetWorksheetPath);

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

            context.ReplaceTargetPartXml(targetWorksheetRelsPath, targetWorksheetRelsXml);
            context.ReplaceTargetPartXml(targetWorksheetPath, targetWorksheetXml);
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

    private static HashSet<string> GetWorksheetPathsWithPivotTableRelationships(
        XlsxSourcePackagePreservationContext context)
    {
        var worksheetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceWorksheetPath in context.SourceSheets.Values)
        {
            var relationshipsXml = context.GetSourceRelationshipsXml(sourceWorksheetPath);
            if (relationshipsXml?.Root?.Elements(context.PackageRelNs + "Relationship").Any(
                    relationship => string.Equals(
                        relationship.Attribute("Type")?.Value,
                        PivotTableRelationshipType,
                        StringComparison.OrdinalIgnoreCase)) == true)
            {
                worksheetPaths.Add(sourceWorksheetPath);
            }
        }

        return worksheetPaths;
    }
}
