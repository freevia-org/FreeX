using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetDrawingPartMerger
{
    public static void Merge(ZipArchive sourceArchive, ZipArchive targetArchive)
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

            var sourceDrawingPath = GetWorksheetDrawingPath(sourceArchive, sourceWorksheetPath, workbookNs, relNs, packageRelNs);
            var targetDrawingPath = GetWorksheetDrawingPath(targetArchive, targetWorksheetPath, workbookNs, relNs, packageRelNs);
            if (string.IsNullOrWhiteSpace(sourceDrawingPath) || string.IsNullOrWhiteSpace(targetDrawingPath))
                continue;

            MergeDrawingPart(sourceArchive, targetArchive, sourceDrawingPath, targetDrawingPath, relNs, packageRelNs);
        }
    }

    public static void Merge(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext? context)
    {
        _ = MergeAndGetDrawingPaths(sourceArchive, targetArchive, context);
    }

    public static XlsxWorksheetDrawingPathMap MergeAndGetDrawingPaths(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext? context)
    {
        if (context is null)
        {
            Merge(sourceArchive, targetArchive);
            return XlsxWorksheetDrawingPathMap.Empty;
        }

        var sourceDrawingPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var targetDrawingPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sheetName, sourceWorksheetPath) in context.SourceSheets)
        {
            if (!context.TargetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sourceDrawingPath = GetWorksheetDrawingPath(sourceArchive, sourceWorksheetPath, context.WorkbookNs, context.RelNs, context.PackageRelNs, context);
            var targetDrawingPath = GetWorksheetDrawingPath(targetArchive, targetWorksheetPath, context.WorkbookNs, context.RelNs, context.PackageRelNs);
            if (!string.IsNullOrWhiteSpace(sourceDrawingPath))
                sourceDrawingPaths[sheetName] = sourceDrawingPath;
            if (!string.IsNullOrWhiteSpace(targetDrawingPath))
                targetDrawingPaths[sheetName] = targetDrawingPath;
            if (string.IsNullOrWhiteSpace(sourceDrawingPath) || string.IsNullOrWhiteSpace(targetDrawingPath))
                continue;

            MergeDrawingPart(sourceArchive, targetArchive, sourceDrawingPath, targetDrawingPath, context.RelNs, context.PackageRelNs);
        }

        return new XlsxWorksheetDrawingPathMap(sourceDrawingPaths, targetDrawingPaths);
    }

    private static string? GetWorksheetDrawingPath(
        ZipArchive archive,
        string worksheetPath,
        XNamespace worksheetNs,
        XNamespace relNs,
        XNamespace packageRelNs,
        XlsxSourcePackagePreservationContext? sourceContext = null)
    {
        var worksheetXml = sourceContext?.GetSourceWorksheetXml(archive, worksheetPath);
        if (worksheetXml is null)
        {
            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                return null;

            worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        }
        var drawingRelId = worksheetXml.Root?
            .Element(worksheetNs + "drawing")?
            .Attribute(relNs + "id")?
            .Value;
        if (string.IsNullOrWhiteSpace(drawingRelId))
            return null;

        var worksheetRels = XlsxRelationshipReader.LoadTargets(
            archive,
            XlsxPackagePath.GetRelationshipPartPath(worksheetPath),
            worksheetPath,
            packageRelNs);
        return worksheetRels.TryGetValue(drawingRelId, out var drawingPath)
            ? drawingPath
            : null;
    }

    private static void MergeDrawingPart(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sourceDrawingPath,
        string targetDrawingPath,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var sourceDrawingEntry = sourceArchive.GetEntry(sourceDrawingPath);
        var targetDrawingEntry = targetArchive.GetEntry(targetDrawingPath);
        if (sourceDrawingEntry is null || targetDrawingEntry is null)
            return;

        var sourceDrawingXml = XlsxPackageXmlEditor.LoadXml(sourceDrawingEntry);
        var targetDrawingXml = XlsxPackageXmlEditor.LoadXml(targetDrawingEntry);
        if (sourceDrawingXml.Root is null || targetDrawingXml.Root is null)
            return;

        var relIdMap = MergeDrawingRelationships(
            sourceArchive,
            targetArchive,
            sourceDrawingPath,
            targetDrawingPath,
            packageRelNs);
        var existingAnchorKeys = targetDrawingXml.Root.Elements()
            .Select(GetDrawingAnchorIdentity)
            .ToHashSet(StringComparer.Ordinal);

        var changed = false;
        foreach (var sourceAnchor in sourceDrawingXml.Root.Elements())
        {
            var anchorCopy = new XElement(sourceAnchor);
            RemapRelationshipReferences(anchorCopy, relNs, relIdMap);
            if (!existingAnchorKeys.Add(GetDrawingAnchorIdentity(anchorCopy)))
                continue;

            targetDrawingXml.Root.Add(anchorCopy);
            changed = true;
        }

        if (changed)
        {
            EnsureUniqueDrawingObjectIds(targetDrawingXml.Root);
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetDrawingPath, targetDrawingXml);
        }
    }

    private static Dictionary<string, string> MergeDrawingRelationships(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sourceDrawingPath,
        string targetDrawingPath,
        XNamespace packageRelNs)
    {
        var relIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourceRelsPath = XlsxPackagePath.GetRelationshipPartPath(sourceDrawingPath);
        var sourceRelsEntry = sourceArchive.GetEntry(sourceRelsPath);
        if (sourceRelsEntry is null)
            return relIdMap;

        var targetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetDrawingPath);
        var sourceRelsXml = XlsxPackageXmlEditor.LoadXml(sourceRelsEntry);
        var targetRelsXml = targetArchive.GetEntry(targetRelsPath) is { } targetRelsEntry
            ? XlsxPackageXmlEditor.LoadXml(targetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        if (sourceRelsXml.Root is null || targetRelsXml.Root is null)
            return relIdMap;

        var targetRelationships = targetRelsXml.Root.Elements(packageRelNs + "Relationship").ToList();
        var usedIds = targetRelationships
            .Select(rel => rel.Attribute("Id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var sourceRelationship in sourceRelsXml.Root.Elements(packageRelNs + "Relationship"))
        {
            var sourceId = sourceRelationship.Attribute("Id")?.Value;
            var type = sourceRelationship.Attribute("Type")?.Value;
            var target = sourceRelationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(sourceId) ||
                string.IsNullOrWhiteSpace(type) ||
                string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var targetMode = sourceRelationship.Attribute("TargetMode")?.Value;
            var resolvedTarget = string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase)
                ? target
                : XlsxPackagePath.ResolveRelationshipTarget(sourceDrawingPath, target);
            var targetRelationship = targetRelationships.FirstOrDefault(rel =>
                string.Equals(rel.Attribute("Type")?.Value, type, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rel.Attribute("TargetMode")?.Value, targetMode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase)
                        ? rel.Attribute("Target")?.Value
                        : XlsxPackagePath.ResolveRelationshipTarget(targetDrawingPath, rel.Attribute("Target")?.Value ?? ""),
                    resolvedTarget,
                    StringComparison.OrdinalIgnoreCase));
            if (targetRelationship is not null)
            {
                relIdMap[sourceId] = targetRelationship.Attribute("Id")!.Value;
                continue;
            }

            var targetId = sourceId;
            if (usedIds.Contains(targetId))
                targetId = NextPreservedRelationshipId(usedIds);
            usedIds.Add(targetId);
            relIdMap[sourceId] = targetId;

            targetRelsXml.Root.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", targetId),
                new XAttribute("Type", type),
                new XAttribute("Target", string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase)
                    ? target
                    : XlsxPackagePath.GetRelationshipTarget(targetDrawingPath, resolvedTarget)),
                string.IsNullOrWhiteSpace(targetMode) ? null : new XAttribute("TargetMode", targetMode)));
            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetRelsPath, targetRelsXml);

        return relIdMap;
    }

    private static string NextPreservedRelationshipId(HashSet<string> usedIds)
    {
        var index = 1;
        while (usedIds.Contains($"rIdPreserved{index}"))
            index++;

        return $"rIdPreserved{index}";
    }

    private static void RemapRelationshipReferences(
        XElement element,
        XNamespace relNs,
        IReadOnlyDictionary<string, string> relIdMap)
    {
        if (relIdMap.Count == 0)
            return;

        foreach (var attribute in element.DescendantsAndSelf().Attributes().Where(attribute => attribute.Name.Namespace == relNs))
        {
            if (relIdMap.TryGetValue(attribute.Value, out var replacementId))
                attribute.Value = replacementId;
        }
    }

    private static string GetDrawingAnchorIdentity(XElement anchor)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var objectName = anchor
            .Descendants(spreadsheetDrawingNs + "cNvPr")
            .Select(element => element.Attribute("name")?.Value)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        return string.IsNullOrWhiteSpace(objectName)
            ? anchor.ToString(SaveOptions.DisableFormatting)
            : $"{anchor.Name.LocalName}:{objectName}";
    }

    private static void EnsureUniqueDrawingObjectIds(XElement drawingRoot)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var objectProperties = drawingRoot
            .Descendants(spreadsheetDrawingNs + "cNvPr")
            .ToList();
        var usedIds = new HashSet<int>();
        var nextId = objectProperties
            .Select(element => int.TryParse(element.Attribute("id")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        foreach (var objectProperty in objectProperties)
        {
            if (int.TryParse(objectProperty.Attribute("id")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                id > 0 &&
                usedIds.Add(id))
            {
                continue;
            }

            while (!usedIds.Add(nextId))
                nextId++;
            objectProperty.SetAttributeValue("id", nextId.ToString(CultureInfo.InvariantCulture));
            nextId++;
        }
    }
}
