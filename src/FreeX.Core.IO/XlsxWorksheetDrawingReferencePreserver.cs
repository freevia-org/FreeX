using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetDrawingReferencePreserver
{
    private const string DrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";

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
            var sourceDrawing = sourceWorksheetXml.Root?.Element(workbookNs + "drawing");
            var sourceRelId = sourceDrawing?.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(sourceRelId))
                continue;

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null || targetRoot.Element(workbookNs + "drawing") is not null)
                continue;

            var sourceWorksheetRels = XlsxRelationshipReader.LoadTargets(
                sourceArchive,
                XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath),
                sourceWorksheetPath,
                packageRelNs);
            if (!sourceWorksheetRels.TryGetValue(sourceRelId, out var drawingPath))
                continue;

            var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
            var targetWorksheetRelsEntry = targetArchive.GetEntry(targetWorksheetRelsPath);
            var targetWorksheetRelsXml = targetWorksheetRelsEntry is not null
                ? XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                targetWorksheetRelsXml,
                packageRelNs,
                targetWorksheetPath,
                drawingPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing");
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);

            targetRoot.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
            XlsxWorksheetDrawingPlacement.SetWorksheetDrawing(targetRoot, workbookNs, relNs, targetRelId);
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
            // R102-io-rename-worksheet-exclusion-sweep-1: sheetName is the LOAD-TIME name; resolve
            // via the shared fallback so a renamed sheet (same physical worksheet part, new name)
            // isn't treated as deleted.
            if (!XlsxRenamedSourceSheetResolver.TryResolveTargetWorksheetPath(
                    context, sheetName, sourceWorksheetPath, out var targetWorksheetPath))
            {
                continue;
            }

            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceArchive, sourceWorksheetPath);
            if (sourceWorksheetXml is null || targetWorksheetEntry is null)
                continue;

            var sourceDrawing = sourceWorksheetXml.Root?.Element(context.WorkbookNs + "drawing");
            var sourceRelId = sourceDrawing?.Attribute(context.RelNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(sourceRelId))
                continue;

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null || targetRoot.Element(context.WorkbookNs + "drawing") is not null)
                continue;

            var sourceWorksheetRels = XlsxRelationshipReader.LoadTargets(
                sourceArchive,
                XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath),
                sourceWorksheetPath,
                context.PackageRelNs);
            if (!sourceWorksheetRels.TryGetValue(sourceRelId, out var drawingPath))
                continue;

            var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
            var targetWorksheetRelsEntry = targetArchive.GetEntry(targetWorksheetRelsPath);
            var targetWorksheetRelsXml = targetWorksheetRelsEntry is not null
                ? XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry)
                : new XDocument(new XElement(context.PackageRelNs + "Relationships"));
            var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                targetWorksheetRelsXml,
                context.PackageRelNs,
                targetWorksheetPath,
                drawingPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing");
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);

            targetRoot.SetAttributeValue(XNamespace.Xmlns + "r", context.RelNs.NamespaceName);
            XlsxWorksheetDrawingPlacement.SetWorksheetDrawing(targetRoot, context.WorkbookNs, context.RelNs, targetRelId);
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }

    public static void Preserve(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext? context,
        XlsxWorksheetDrawingPathMap drawingPaths)
    {
        if (context is null || drawingPaths == XlsxWorksheetDrawingPathMap.Empty)
        {
            Preserve(sourceArchive, targetArchive, context);
            return;
        }

        foreach (var (sheetName, drawingPath) in drawingPaths.SourceDrawingPaths)
        {
            if (drawingPaths.TargetDrawingPaths.ContainsKey(sheetName))
                continue;
            // sheetName here is the LOAD-TIME name key drawingPaths was built with (see
            // XlsxWorksheetDrawingPartMerger.MergeAndGetDrawingPaths); resolve via the same
            // rename-tolerant fallback rather than a raw name lookup.
            if (!context.SourceSheets.TryGetValue(sheetName, out var sourceWorksheetPathForFallback) ||
                !XlsxRenamedSourceSheetResolver.TryResolveTargetWorksheetPath(
                    context, sheetName, sourceWorksheetPathForFallback, out var targetWorksheetPath))
            {
                continue;
            }

            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (targetWorksheetEntry is null)
                continue;

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null || targetRoot.Element(context.WorkbookNs + "drawing") is not null)
                continue;

            var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
            var targetWorksheetRelsEntry = targetArchive.GetEntry(targetWorksheetRelsPath);
            var targetWorksheetRelsXml = targetWorksheetRelsEntry is not null
                ? XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry)
                : new XDocument(new XElement(context.PackageRelNs + "Relationships"));
            var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                targetWorksheetRelsXml,
                context.PackageRelNs,
                targetWorksheetPath,
                drawingPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing");
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);

            targetRoot.SetAttributeValue(XNamespace.Xmlns + "r", context.RelNs.NamespaceName);
            XlsxWorksheetDrawingPlacement.SetWorksheetDrawing(targetRoot, context.WorkbookNs, context.RelNs, targetRelId);
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }

        var activeDrawingPaths = NormalizeWorksheetDrawingRelationships(targetArchive, context);
        RemoveShadowedSourceDrawingParts(targetArchive, drawingPaths, activeDrawingPaths);
    }

    private static HashSet<string> NormalizeWorksheetDrawingRelationships(
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext context)
    {
        var activeDrawingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var targetWorksheetPath in context.TargetSheets.Values)
        {
            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (targetWorksheetEntry is null)
                continue;

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var activeDrawingRelId = targetWorksheetXml.Root?
                .Element(context.WorkbookNs + "drawing")?
                .Attribute(context.RelNs + "id")?
                .Value;
            if (string.IsNullOrWhiteSpace(activeDrawingRelId))
                continue;

            var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
            var targetWorksheetRelsEntry = targetArchive.GetEntry(targetWorksheetRelsPath);
            if (targetWorksheetRelsEntry is null)
                continue;

            var targetWorksheetRelsXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry);
            var root = targetWorksheetRelsXml.Root;
            if (root is null)
                continue;

            var drawingRelationships = root
                .Elements(context.PackageRelNs + "Relationship")
                .Where(IsDrawingRelationship)
                .ToList();
            XElement? activeDrawingRelationship = null;
            foreach (var relationship in drawingRelationships)
            {
                if (string.Equals(relationship.Attribute("Id")?.Value, activeDrawingRelId, StringComparison.Ordinal))
                {
                    activeDrawingRelationship = relationship;
                    break;
                }
            }

            if (activeDrawingRelationship is null)
                continue;

            var activeTarget = activeDrawingRelationship.Attribute("Target")?.Value;
            if (!string.IsNullOrWhiteSpace(activeTarget))
            {
                activeDrawingPaths.Add(XlsxPackagePath.ResolveRelationshipTarget(targetWorksheetPath, activeTarget));
            }

            var redundantDrawingRelationships = drawingRelationships
                .Where(relationship =>
                    !string.Equals(relationship.Attribute("Id")?.Value, activeDrawingRelId, StringComparison.Ordinal))
                .ToList();
            if (redundantDrawingRelationships.Count == 0)
                continue;

            foreach (var redundantRelationship in redundantDrawingRelationships)
                redundantRelationship.Remove();

            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);
        }

        return activeDrawingPaths;
    }

    private static void RemoveShadowedSourceDrawingParts(
        ZipArchive targetArchive,
        XlsxWorksheetDrawingPathMap drawingPaths,
        IReadOnlySet<string> activeDrawingPaths)
    {
        foreach (var (sheetName, sourceDrawingPath) in drawingPaths.SourceDrawingPaths)
        {
            if (!drawingPaths.TargetDrawingPaths.TryGetValue(sheetName, out var targetDrawingPath))
                continue;

            var normalizedSourceDrawingPath = XlsxPackagePath.NormalizePackagePath(sourceDrawingPath);
            if (string.Equals(
                    normalizedSourceDrawingPath,
                    XlsxPackagePath.NormalizePackagePath(targetDrawingPath),
                    StringComparison.OrdinalIgnoreCase) ||
                activeDrawingPaths.Contains(normalizedSourceDrawingPath))
            {
                continue;
            }

            targetArchive.GetEntry(normalizedSourceDrawingPath)?.Delete();
            targetArchive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(normalizedSourceDrawingPath))?.Delete();
        }
    }

    private static bool IsDrawingRelationship(XElement relationship) =>
        string.Equals(
            relationship.Attribute("Type")?.Value,
            DrawingRelationshipType,
            StringComparison.OrdinalIgnoreCase);
}
