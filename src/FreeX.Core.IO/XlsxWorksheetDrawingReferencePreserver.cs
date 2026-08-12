using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetDrawingReferencePreserver
{
    private const string DrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";

    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, targetArchive);
        Preserve(context);
    }

    public static void Preserve(XlsxSourcePackagePreservationContext? context)
    {
        if (context is null)
            return;

        var targetArchive = context.TargetArchive;
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
            var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceWorksheetPath);
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

            var sourceWorksheetRels = context.GetSourceRelationshipTargets(sourceWorksheetPath);
            if (!sourceWorksheetRels.TryGetValue(sourceRelId, out var drawingPath))
                continue;

            var (targetWorksheetRelsPath, targetWorksheetRelsXml) =
                context.LoadOrCreateTargetRelationships(targetWorksheetPath);
            var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                targetWorksheetRelsXml,
                context.PackageRelNs,
                targetWorksheetPath,
                drawingPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing");
            context.ReplaceTargetPartXml(targetWorksheetRelsPath, targetWorksheetRelsXml);

            targetRoot.SetAttributeValue(XNamespace.Xmlns + "r", context.RelNs.NamespaceName);
            XlsxWorksheetDrawingPlacement.SetWorksheetDrawing(targetRoot, context.WorkbookNs, context.RelNs, targetRelId);
            context.ReplaceTargetPartXml(targetWorksheetPath, targetWorksheetXml);
        }
    }

    public static void Preserve(
        XlsxSourcePackagePreservationContext? context,
        XlsxWorksheetDrawingPathMap drawingPaths)
    {
        if (context is null || drawingPaths == XlsxWorksheetDrawingPathMap.Empty)
        {
            Preserve(context);
            return;
        }

        var targetArchive = context.TargetArchive;
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

            var (targetWorksheetRelsPath, targetWorksheetRelsXml) =
                context.LoadOrCreateTargetRelationships(targetWorksheetPath);
            var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                targetWorksheetRelsXml,
                context.PackageRelNs,
                targetWorksheetPath,
                drawingPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing");
            context.ReplaceTargetPartXml(targetWorksheetRelsPath, targetWorksheetRelsXml);

            targetRoot.SetAttributeValue(XNamespace.Xmlns + "r", context.RelNs.NamespaceName);
            XlsxWorksheetDrawingPlacement.SetWorksheetDrawing(targetRoot, context.WorkbookNs, context.RelNs, targetRelId);
            context.ReplaceTargetPartXml(targetWorksheetPath, targetWorksheetXml);
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
