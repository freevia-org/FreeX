using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

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

            var streamedDrawingRelId = ReadWorksheetDrawingRelId(worksheetEntry, worksheetNs, relNs);
            if (string.IsNullOrWhiteSpace(streamedDrawingRelId))
                return null;

            return ResolveWorksheetDrawingPath(archive, worksheetPath, packageRelNs, streamedDrawingRelId);
        }

        var drawingRelId = worksheetXml.Root?
            .Element(worksheetNs + "drawing")?
            .Attribute(relNs + "id")?
            .Value;
        if (string.IsNullOrWhiteSpace(drawingRelId))
            return null;

        return ResolveWorksheetDrawingPath(archive, worksheetPath, packageRelNs, drawingRelId);
    }

    private static string? ResolveWorksheetDrawingPath(
        ZipArchive archive,
        string worksheetPath,
        XNamespace packageRelNs,
        string drawingRelId)
    {
        var worksheetRels = XlsxRelationshipReader.LoadTargets(
            archive,
            XlsxPackagePath.GetRelationshipPartPath(worksheetPath),
            worksheetPath,
            packageRelNs);
        return worksheetRels.TryGetValue(drawingRelId, out var drawingPath)
            ? drawingPath
            : null;
    }

    private static string? ReadWorksheetDrawingRelId(
        ZipArchiveEntry worksheetEntry,
        XNamespace worksheetNs,
        XNamespace relNs)
    {
        using var stream = worksheetEntry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true
        });

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element ||
                !string.Equals(reader.LocalName, "drawing", StringComparison.Ordinal) ||
                !string.Equals(reader.NamespaceURI, worksheetNs.NamespaceName, StringComparison.Ordinal))
            {
                continue;
            }

            return reader.GetAttribute("id", relNs.NamespaceName);
        }

        return null;
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

        // Chart anchors are de-duplicated by the chart relationship TARGET, not just by anchor identity.
        // The chart writer replaces a chart sheet's drawing with its own graphicFrames before this merge
        // runs; the source-package drawing still holds its original frame for the same chart, and that
        // frame's identity (cNvPr name like "Chart 2" vs the writer's "Chart 14") can differ even though
        // both point at the same chart part. Keying chart anchors on their resolved chart-part target
        // ensures a chart already emitted by the writer is not re-added from the source drawing, which
        // would otherwise yield two graphicFrames (one zero-sized) for a single chart.
        var targetChartTargets = CollectAnchoredChartTargets(
            targetDrawingXml.Root, targetArchive, targetDrawingPath, relNs, packageRelNs);

        var changed = false;
        foreach (var sourceAnchor in sourceDrawingXml.Root.Elements())
        {
            var anchorCopy = new XElement(sourceAnchor);
            RemapRelationshipReferences(anchorCopy, relNs, relIdMap);
            if (!existingAnchorKeys.Add(GetDrawingAnchorIdentity(anchorCopy)))
                continue;

            // After remap, a chart anchor's rel id points into the target drawing's rels. If that chart
            // target is already anchored in the target, the writer already emitted this chart — skip the
            // duplicate source frame.
            var chartTarget = ResolveAnchorChartTarget(
                anchorCopy, targetArchive, targetDrawingPath, relNs, packageRelNs);
            if (chartTarget is not null && !targetChartTargets.Add(chartTarget))
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
            var targetRelationship = FindMatchingRelationship(
                targetRelationships,
                targetDrawingPath,
                type,
                targetMode,
                resolvedTarget);
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

    private static XElement? FindMatchingRelationship(
        IReadOnlyList<XElement> targetRelationships,
        string targetDrawingPath,
        string type,
        string? targetMode,
        string resolvedTarget)
    {
        foreach (var relationship in targetRelationships)
        {
            if (!string.Equals(relationship.Attribute("Type")?.Value, type, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(relationship.Attribute("TargetMode")?.Value, targetMode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relationshipTarget = string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase)
                ? relationship.Attribute("Target")?.Value
                : XlsxPackagePath.ResolveRelationshipTarget(targetDrawingPath, relationship.Attribute("Target")?.Value ?? "");
            if (string.Equals(relationshipTarget, resolvedTarget, StringComparison.OrdinalIgnoreCase))
                return relationship;
        }

        return null;
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

    // Resolved chart-part targets (e.g. "xl/charts/chart3.xml") for every chart graphicFrame already
    // anchored in the given drawing root.
    private static HashSet<string> CollectAnchoredChartTargets(
        XElement drawingRoot,
        ZipArchive archive,
        string drawingPath,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rels = XlsxRelationshipReader.LoadTargets(
            archive, XlsxPackagePath.GetRelationshipPartPath(drawingPath), drawingPath, packageRelNs);
        foreach (var anchor in drawingRoot.Elements())
        {
            var target = ResolveAnchorChartTargetFromRels(anchor, rels, relNs);
            if (target is not null)
                targets.Add(target);
        }

        return targets;
    }

    private static string? ResolveAnchorChartTarget(
        XElement anchor,
        ZipArchive archive,
        string drawingPath,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var rels = XlsxRelationshipReader.LoadTargets(
            archive, XlsxPackagePath.GetRelationshipPartPath(drawingPath), drawingPath, packageRelNs);
        return ResolveAnchorChartTargetFromRels(anchor, rels, relNs);
    }

    // The relationship id a chart graphicFrame points at lives on the <c:chart>/<cx:chart> element under
    // the anchor's graphicData. Resolve it against the drawing's rels to the chart part path; null when the
    // anchor holds no chart (a picture/shape) or the rel id does not resolve.
    private static string? ResolveAnchorChartTargetFromRels(
        XElement anchor,
        IReadOnlyDictionary<string, string> rels,
        XNamespace relNs)
    {
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace chartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        foreach (var chartElement in anchor.Descendants()
                     .Where(e => e.Name == chartNs + "chart" || e.Name == chartExNs + "chart"))
        {
            var relId = chartElement.Attribute(relNs + "id")?.Value;
            if (!string.IsNullOrWhiteSpace(relId) && rels.TryGetValue(relId, out var target))
                return target;
        }

        return null;
    }

    private static string GetDrawingAnchorIdentity(XElement anchor)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        var objectName = ReadFirstNonVisualPropertyName(anchor, spreadsheetDrawingNs);
        if (string.IsNullOrWhiteSpace(objectName))
            return anchor.ToString(SaveOptions.DisableFormatting);

        // Excel's own default object naming ("TextBox 1", "Picture 1", ...) is reused independently
        // per sheet, so a source-loaded object and a brand-new object authored in FreeX can end up
        // with the exact same cNvPr name while being genuinely distinct objects. Name alone is not a
        // reliable identity for dedup: fold the anchor's own position (from/to cell + offsets for
        // one/two-cell anchors, or absolute pos + extent for absolute anchors) into the key so two
        // anchors that merely share a default name but sit at different positions are both kept. A
        // source anchor the writer re-emits verbatim keeps both the same name and the same position,
        // so it still collapses to a single identity and continues to de-dupe correctly.
        var position = GetDrawingAnchorPositionSignature(anchor, spreadsheetDrawingNs);
        return $"{anchor.Name.LocalName}:{objectName}:{position}";
    }

    private static string GetDrawingAnchorPositionSignature(XElement anchor, XNamespace spreadsheetDrawingNs)
    {
        // Pull out the actual position values rather than serializing the elements with ToString():
        // a source anchor copied into a detached XElement (see MergeDrawingPart's anchorCopy) loses the
        // "xdr" prefix binding it inherited from its original document's root and re-serializes using the
        // default-namespace form (e.g. "<from xmlns=\"...\">" instead of "<xdr:from xmlns:xdr=\"...\">"),
        // even though the position is byte-for-byte the same. Comparing raw child/attribute values instead
        // of XML text keeps the signature immune to that prefix churn so a source anchor re-emitted
        // verbatim by the writer still collapses to the same identity as its target-side counterpart.
        return string.Join(
            "|",
            FormatCellReference("from", anchor.Element(spreadsheetDrawingNs + "from"), spreadsheetDrawingNs),
            FormatCellReference("to", anchor.Element(spreadsheetDrawingNs + "to"), spreadsheetDrawingNs),
            FormatPositionAttributes("pos", anchor.Element(spreadsheetDrawingNs + "pos")),
            FormatPositionAttributes("ext", anchor.Element(spreadsheetDrawingNs + "ext")));
    }

    private static string FormatCellReference(string label, XElement? element, XNamespace spreadsheetDrawingNs)
    {
        if (element is null)
            return $"{label}:";

        return string.Join(
            "/",
            label,
            element.Element(spreadsheetDrawingNs + "col")?.Value,
            element.Element(spreadsheetDrawingNs + "colOff")?.Value,
            element.Element(spreadsheetDrawingNs + "row")?.Value,
            element.Element(spreadsheetDrawingNs + "rowOff")?.Value);
    }

    private static string FormatPositionAttributes(string label, XElement? element)
    {
        if (element is null)
            return $"{label}:";

        return string.Join(
            "/",
            label,
            element.Attribute("x")?.Value,
            element.Attribute("y")?.Value,
            element.Attribute("cx")?.Value,
            element.Attribute("cy")?.Value);
    }

    private static string? ReadFirstNonVisualPropertyName(XElement anchor, XNamespace spreadsheetDrawingNs)
    {
        foreach (var element in anchor.Descendants(spreadsheetDrawingNs + "cNvPr"))
        {
            var name = element.Attribute("name")?.Value;
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        return null;
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
