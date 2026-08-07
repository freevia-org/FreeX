using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

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
            if (!string.IsNullOrWhiteSpace(targetDrawingPath))
                MergeChartShadowIntoTarget(targetArchive, targetDrawingPath, relNs, packageRelNs);

            if (string.IsNullOrWhiteSpace(sourceDrawingPath) || string.IsNullOrWhiteSpace(targetDrawingPath))
                continue;

            MergeDrawingPart(sourceArchive, targetArchive, sourceDrawingPath, targetDrawingPath, relNs, packageRelNs);
        }
    }

    public static void Merge(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext? context,
        Workbook? workbook = null)
    {
        _ = MergeAndGetDrawingPaths(sourceArchive, targetArchive, context, workbook);
    }

    // The optional workbook is the in-memory model being saved (null on the workbook-less path or in tests
    // that drive the merger directly). When supplied, each sheet's edited-but-originally-source-loaded drawing
    // objects are resolved via XlsxWorksheetDrawingObjectWriter.GetRewrittenSourceObjectNames so
    // MergeDrawingPart can drop their now-stale ORIGINAL source anchors — the writer has already re-emitted
    // a fresh anchor for each, and re-adding the original would duplicate the object on reload.
    public static XlsxWorksheetDrawingPathMap MergeAndGetDrawingPaths(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext? context,
        Workbook? workbook = null)
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
            // R102-io-rename-worksheet-exclusion-sweep-1: sheetName is the sheet's LOAD-TIME name;
            // a plain rename makes a direct lookup against context.TargetSheets (keyed by CURRENT
            // name) fail even though the sheet's own worksheet part -- and thus its drawing -- is
            // completely unaffected. Resolve via XlsxRenamedSourceSheetResolver so a renamed sheet's
            // drawing still gets merged instead of being silently skipped like a deleted sheet's.
            // R123-io-rename-drawing-supersede-gap: use TryResolveCurrentSheet (not the path-only
            // TryResolveTargetWorksheetPath overload) so we also get the sheet's CURRENT (post-rename)
            // name back -- needed below to look the Sheet model up in the live Workbook, which only
            // knows sheets by their current name. Using the stale load-time sheetName there made
            // workbook?.GetSheet(sheetName) return null after any rename, silently disabling the
            // tombstone/supersede guard (deleted objects resurrected, edited objects duplicated).
            if (!XlsxRenamedSourceSheetResolver.TryResolveCurrentSheet(
                    context, sheetName, sourceWorksheetPath, out var currentSheetName, out var targetWorksheetPath))
            {
                continue;
            }

            var sourceDrawingPath = GetWorksheetDrawingPath(sourceArchive, sourceWorksheetPath, context.WorkbookNs, context.RelNs, context.PackageRelNs, context);
            var targetDrawingPath = GetWorksheetDrawingPath(targetArchive, targetWorksheetPath, context.WorkbookNs, context.RelNs, context.PackageRelNs);
            if (!string.IsNullOrWhiteSpace(sourceDrawingPath))
                sourceDrawingPaths[sheetName] = sourceDrawingPath;
            if (!string.IsNullOrWhiteSpace(targetDrawingPath))
            {
                targetDrawingPaths[sheetName] = targetDrawingPath;

                // drawing-zorder-share-part: reclaim any chart anchors XlsxWorksheetChartWriter stashed
                // for this sheet's drawing part before XlsxWorksheetDrawingObjectWriter (which runs
                // between the chart writer and here) had a chance to delete-and-rewrite it. Must run
                // regardless of whether the source side resolves below, since a sheet can gain its
                // first-ever drawing part in this very save.
                MergeChartShadowIntoTarget(targetArchive, targetDrawingPath, context.RelNs, context.PackageRelNs);
            }

            if (string.IsNullOrWhiteSpace(sourceDrawingPath) || string.IsNullOrWhiteSpace(targetDrawingPath))
                continue;

            var sheet = workbook?.GetSheet(currentSheetName);
            var supersededSourceNames = sheet is not null
                ? XlsxWorksheetDrawingObjectWriter.GetRewrittenSourceObjectNames(sheet)
                : null;
            MergeDrawingPart(sourceArchive, targetArchive, sourceDrawingPath, targetDrawingPath, context.RelNs, context.PackageRelNs, supersededSourceNames, sheet);
        }

        return new XlsxWorksheetDrawingPathMap(sourceDrawingPaths, targetDrawingPaths);
    }

    // R127-io-drawing-relationship-orphan-1: internal (not private) so
    // XlsxFileAdapter.SourcePackage.GetExcludedDeletedChartPartPaths can resolve a sheet's SOURCE
    // drawing part path without duplicating this resolution logic.
    internal static string? GetWorksheetDrawingPath(
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

    // drawing-zorder-share-part: XlsxWorksheetChartWriter stashes a throwaway copy of the chart anchors
    // it wrote at XlsxWorksheetChartDrawingShadow.GetShadowPath(targetDrawingPath) whenever it reused a
    // drawing part XlsxWorksheetDrawingObjectWriter was about to delete-and-rewrite afterwards (see the
    // comment on that helper). By the time this runs, both writers have already executed, so the shadow
    // -- if present -- is the only surviving record of those chart anchors. Reuse the existing
    // source-vs-target merge machinery to fold the shadow's anchors (and their chart relationships)
    // into the live target drawing part, then delete the shadow so it never leaks into the saved
    // package. A no-op when no shadow was written for this drawing part.
    private static void MergeChartShadowIntoTarget(
        ZipArchive targetArchive,
        string targetDrawingPath,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var shadowPath = XlsxWorksheetChartDrawingShadow.GetShadowPath(targetDrawingPath);
        if (targetArchive.GetEntry(shadowPath) is null)
            return;

        // Same archive on both sides: the shadow and the live drawing part it feeds are both in
        // targetArchive. MergeDrawingPart only ever reads from its "source" side, so this is safe.
        MergeDrawingPart(targetArchive, targetArchive, shadowPath, targetDrawingPath, relNs, packageRelNs);

        targetArchive.GetEntry(shadowPath)?.Delete();
        targetArchive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(shadowPath))?.Delete();
    }

    private static void MergeDrawingPart(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sourceDrawingPath,
        string targetDrawingPath,
        XNamespace relNs,
        XNamespace packageRelNs,
        IReadOnlySet<string>? supersededSourceNames = null,
        Sheet? sheet = null)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
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
            // Drop a source anchor whose object was originally loaded from the .xlsx but has since been
            // edited so its IsSourceLoaded flag was cleared: XlsxWorksheetDrawingObjectWriter has already
            // re-emitted a fresh anchor for it into the target drawing (with the same cNvPr name), so
            // re-adding this ORIGINAL anchor would leave the saved part holding both — the object would be
            // duplicated on reload. The anchor's cNvPr name doesn't carry a relationship id, so read it off
            // the un-remapped source element.
            if (supersededSourceNames is { Count: > 0 })
            {
                var sourceObjectName = ReadFirstNonVisualPropertyName(sourceAnchor, spreadsheetDrawingNs);
                if (sourceObjectName is not null && supersededSourceNames.Contains(sourceObjectName))
                    continue;
            }

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

        // R118-io-drawing-zorder-1: BringDrawingShapeForwardCommand/SendDrawingShapeBackwardCommand
        // (DrawingShapeZOrderCommands.cs) and MoveSelectionPaneObjectCommand (SelectionPaneCommands.cs)
        // only ever mutate sheet.DrawingObjectZOrder -- they never clear a moved object's IsSourceLoaded,
        // which is the normal (unedited) state for most loaded objects. XlsxWorksheetDrawingObjectWriter's
        // own z-order-aware anchor loop only walks its !IsSourceLoaded-filtered pictures/textBoxes/shapes
        // lists, so a still-source-loaded object never reaches it; its ORIGINAL anchor is instead copied
        // verbatim, in source document order, by the loop above -- silently discarding a Bring
        // Forward/Send Backward/Selection Pane reorder on save. Reorder the anchors this merge just
        // assembled (freshly-written ones already in the root, plus every preserved one just appended
        // above) to match the sheet's CURRENT z-order before saving.
        var reordered = sheet is not null &&
            ReorderAnchorsByZOrder(targetDrawingXml.Root, sheet, spreadsheetDrawingNs);

        if (changed || reordered)
        {
            EnsureUniqueDrawingObjectIds(targetDrawingXml.Root);
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetDrawingPath, targetDrawingXml);
        }

        // R127-io-drawing-relationship-orphan-1: MergeDrawingRelationships (above) copies every
        // Relationship from the SOURCE drawing's .rels into the target's .rels, matched only by
        // Type+Target -- it runs BEFORE the anchor loop above decides which source anchors actually
        // survive (a deleted picture/chart/shape/text box's anchor is dropped via supersededSourceNames,
        // but its relationship was already copied). That leaves a <Relationship> entry (an image, or --
        // worse -- a chart) in the saved drawing part's .rels that nothing in the drawing XML references
        // anymore. Prune here, now that the FINAL anchor set (freshly-written anchors already present
        // before this merge, plus every source anchor just preserved above) is known: any Relationship
        // Id not referenced by an r:id/r:embed/r:link/r:cs attribute anywhere in the merged drawing is
        // dead and must not survive the save, mirroring the pattern
        // XlsxWorksheetHyperlinkRelationshipPruner already uses for orphaned hyperlink relationships.
        //
        // sourceArchive != targetArchive ONLY on the real cross-package merge (true pristine source
        // package -> generated package) -- MergeChartShadowIntoTarget's reconciliation call passes the
        // SAME archive for both (it is folding the chart writer's own shadow anchors back into the
        // target, entirely within targetArchive). That shadow call always runs BEFORE this sheet's real
        // merge and therefore sees an INCOMPLETE target drawing (only the chart anchor the chart writer
        // wrote so far -- the picture/shape/text-box anchors this same save's real merge is about to add
        // back in haven't been merged in yet). Pruning against that incomplete anchor set would delete
        // relationships XlsxPackageMetadataMerger.MergeRelationshipParts had already correctly attached
        // moments earlier for those not-yet-remerged objects, forcing MergeDrawingRelationships's OWN
        // relationship-recreation path (whose Target computation is not reliable for a non-External,
        // location-only hyperlink Target living inside xl/drawings/ -- see GetRelationshipTarget's
        // xl/drawings whitelist) to run and corrupt the Target string. Restricting the prune to the real
        // merge means it only ever runs after every anchor for this drawing part has been assembled.
        if (!ReferenceEquals(sourceArchive, targetArchive))
            PruneUnreferencedDrawingRelationships(targetArchive, targetDrawingPath, targetDrawingXml.Root, relNs, packageRelNs);
    }

    // R127-io-drawing-relationship-orphan-1: removes any <Relationship> in the drawing part's own .rels
    // that no surviving anchor in drawingRoot references anymore. Must only be called once the FINAL
    // anchor set for this drawing part is assembled -- see the caller's comment.
    private static void PruneUnreferencedDrawingRelationships(
        ZipArchive targetArchive,
        string targetDrawingPath,
        XElement drawingRoot,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var targetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetDrawingPath);
        var targetRelsEntry = targetArchive.GetEntry(targetRelsPath);
        if (targetRelsEntry is null)
            return;

        // Same generic relNs-attribute scan RemapRelationshipReferences already uses: every reference a
        // drawing part can make into its own .rels (r:id on a pic/graphicFrame/hlinkClick, r:embed on a
        // blipFill, r:link, r:cs, ...) lives in this one namespace, so scanning for it here stays in sync
        // with whatever kinds of references the merge above is willing to remap.
        var referencedIds = drawingRoot.DescendantsAndSelf()
            .Attributes()
            .Where(attribute => attribute.Name.Namespace == relNs)
            .Select(attribute => attribute.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var relsXml = XlsxPackageXmlEditor.LoadXml(targetRelsEntry);
        var root = relsXml.Root;
        if (root is null)
            return;

        var orphans = root.Elements(packageRelNs + "Relationship")
            .Where(relationship =>
            {
                var id = relationship.Attribute("Id")?.Value;
                return !string.IsNullOrWhiteSpace(id) && !referencedIds.Contains(id);
            })
            .ToList();
        if (orphans.Count == 0)
            return;

        foreach (var orphan in orphans)
            orphan.Remove();

        if (root.Elements(packageRelNs + "Relationship").Any())
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetRelsPath, relsXml);
        else
            targetRelsEntry.Delete();
    }

    // R118-io-drawing-zorder-1: reorders the TOP-LEVEL anchors of a merged drawing part to match
    // sheet.DrawingObjectZOrder, so a Bring Forward/Send Backward/Selection Pane move survives save even
    // for objects that are still IsSourceLoaded (the writer never re-emits those -- see the caller's
    // comment). Matches each anchor to its DrawingObjectZOrderEntry by the same stable cNvPr@name identity
    // the rest of this file already relies on (GetRewrittenSourceObjectNames, GetDrawingAnchorIdentity):
    // a source-loaded object's model Name was stamped from that exact anchor's cNvPr@name at load time,
    // and a freshly-written anchor carries the model's CURRENT Name (DrawingName(...)), so the identity
    // holds for both the "just appended, preserved" and "already emitted by the writer" cases.
    // <para>
    // Only Picture/TextBox/Shape entries participate: Chart anchors are matched POSITIONALLY by
    // XlsxWorksheetChartWriter (a graphicFrame's cNvPr name is not a reliable chart identity — see that
    // writer's own comments), so charts are deliberately left out of the name-based lookup below and keep
    // whatever relative position they already have.
    // </para>
    // <para>
    // A picture/shape/text box nested inside a preserved &lt;xdr:grpSp&gt; group is NOT a top-level anchor
    // at all (the whole group is ONE top-level anchor); reordering such a child relative to a top-level
    // sibling would require splitting the group apart, which this method deliberately does not attempt.
    // Only objects with no match at all, or fewer than two matches, leave the anchors untouched.
    // </para>
    internal static bool ReorderAnchorsByZOrder(XElement drawingRoot, Sheet sheet, XNamespace spreadsheetDrawingNs)
    {
        if (sheet.DrawingObjectZOrder.Count == 0)
            return false;

        var normalizedOrder = DrawingObjectZOrder.GetNormalizedOrder(sheet);
        if (normalizedOrder.Count == 0)
            return false;

        var nameOrderIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < normalizedOrder.Count; index++)
        {
            var entry = normalizedOrder[index];
            var name = entry.Kind switch
            {
                SelectionPaneObjectKind.Picture => sheet.Pictures.FirstOrDefault(picture => picture.Id == entry.Id)?.Name,
                SelectionPaneObjectKind.TextBox => sheet.TextBoxes.FirstOrDefault(textBox => textBox.Id == entry.Id)?.Name,
                SelectionPaneObjectKind.Shape => sheet.DrawingShapes.FirstOrDefault(shape => shape.Id == entry.Id)?.Name,
                // Chart intentionally excluded -- see the method comment above.
                _ => null
            };

            // Excel's default naming ("Picture 1", "Shape 1", ...) is reused independently per sheet, so
            // two distinct entries could in principle share a literal Name; keep the FIRST (lowest
            // z-order index) mapping only -- an unresolvable name collision is exactly the pre-existing
            // GetDrawingAnchorIdentity caveat this file already documents, not a new risk this fix adds.
            if (!string.IsNullOrWhiteSpace(name) && !nameOrderIndex.ContainsKey(name))
                nameOrderIndex[name] = index;
        }

        if (nameOrderIndex.Count == 0)
            return false;

        var elements = drawingRoot.Elements().ToList();
        var matchedPositions = new List<int>();
        var matchedZOrderIndexes = new List<int>();
        for (var position = 0; position < elements.Count; position++)
        {
            var name = ReadFirstNonVisualPropertyName(elements[position], spreadsheetDrawingNs);
            if (name is not null && nameOrderIndex.TryGetValue(name, out var zOrderIndex))
            {
                matchedPositions.Add(position);
                matchedZOrderIndexes.Add(zOrderIndex);
            }
        }

        // Fewer than two matched anchors means there is nothing to reorder relative to each other.
        if (matchedPositions.Count < 2)
            return false;

        var sortedMatchIndexes = Enumerable.Range(0, matchedPositions.Count)
            .OrderBy(matchIndex => matchedZOrderIndexes[matchIndex])
            .ToList();

        var alreadyInOrder = true;
        for (var matchIndex = 0; matchIndex < sortedMatchIndexes.Count; matchIndex++)
        {
            if (sortedMatchIndexes[matchIndex] != matchIndex)
            {
                alreadyInOrder = false;
                break;
            }
        }

        if (alreadyInOrder)
            return false;

        // Re-assign only the matched slots, in z-order sequence; every unmatched element (a chart, an
        // unnamed anchor, or one this pass could not identify) keeps its original position untouched.
        var reorderedMatchedElements = sortedMatchIndexes.Select(matchIndex => elements[matchedPositions[matchIndex]]).ToList();
        for (var matchIndex = 0; matchIndex < matchedPositions.Count; matchIndex++)
            elements[matchedPositions[matchIndex]] = reorderedMatchedElements[matchIndex];

        drawingRoot.RemoveNodes();
        foreach (var element in elements)
            drawingRoot.Add(element);

        return true;
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
