using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookMetadataPreserver
{
    public static void Preserve(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        Workbook workbook,
        IReadOnlyList<SheetId> sourceSheetIdsByLocalId)
    {
        var context = XlsxSourcePackagePreservationContext.TryCreate(
            sourceArchive,
            targetArchive,
            workbook,
            sourceSheetIdsByLocalId);
        Preserve(context, workbook, sourceSheetIdsByLocalId);
    }

    public static void Preserve(
        XlsxSourcePackagePreservationContext? context,
        Workbook workbook,
        IReadOnlyList<SheetId> sourceSheetIdsByLocalId)
    {
        if (context is null)
            return;

        var sourceArchive = context.SourceArchive;
        var targetArchive = context.TargetArchive;
        var workbookNs = context.WorkbookNs;
        var sourceWorkbookXml = context.SourceWorkbookXml;
        var sourceRevisionPointer = sourceWorkbookXml.Root?.Element(workbookNs + "revisionPtr");
        if (sourceRevisionPointer is not null && !HasCompleteRevisionHistorySidecarGraph(context))
            sourceRevisionPointer = null;
        var sourceExtensionList = sourceWorkbookXml.Root?.Element(workbookNs + "extLst");
        var sourceFileVersion = sourceWorkbookXml.Root?.Element(workbookNs + "fileVersion");
        var sourceFileSharing = sourceWorkbookXml.Root?.Element(workbookNs + "fileSharing");
        var sourceFileRecoveryProperties = sourceWorkbookXml.Root?.Elements(workbookNs + "fileRecoveryPr").ToArray() ?? [];
        var sourceSmartTagProperties = sourceWorkbookXml.Root?.Element(workbookNs + "smartTagPr");
        var sourceSmartTagTypes = sourceWorkbookXml.Root?.Element(workbookNs + "smartTagTypes");
        var sourceFunctionGroups = sourceWorkbookXml.Root?.Element(workbookNs + "functionGroups");
        var sourceDefinedNames = sourceWorkbookXml.Root?.Element(workbookNs + "definedNames");
        var sourceBookViews = sourceWorkbookXml.Root?.Element(workbookNs + "bookViews");
        var sourceCustomWorkbookViews = sourceWorkbookXml.Root?.Element(workbookNs + "customWorkbookViews");
        var sourceWorkbookProperties = sourceWorkbookXml.Root?.Element(workbookNs + "workbookPr");
        var sourceWorkbookProtection = sourceWorkbookXml.Root?.Element(workbookNs + "workbookProtection");
        var sourceCalculationProperties = sourceWorkbookXml.Root?.Element(workbookNs + "calcPr");
        var sourceOleSize = sourceWorkbookXml.Root?.Element(workbookNs + "oleSize");
        var sourceWebPublishing = sourceWorkbookXml.Root?.Element(workbookNs + "webPublishing");
        var sourceWebPublishObjects = sourceWorkbookXml.Root?.Element(workbookNs + "webPublishObjects");
        if (sourceRevisionPointer is null &&
            sourceExtensionList is null &&
            sourceFileVersion is null &&
            sourceFileSharing is null &&
            sourceFileRecoveryProperties.Length == 0 &&
            sourceSmartTagProperties is null &&
            sourceSmartTagTypes is null &&
            sourceFunctionGroups is null &&
            sourceDefinedNames is null &&
            sourceBookViews is null &&
            sourceCustomWorkbookViews is null &&
            sourceWorkbookProperties is null &&
            sourceWorkbookProtection is null &&
            sourceCalculationProperties is null &&
            sourceOleSize is null &&
            sourceWebPublishing is null &&
            sourceWebPublishObjects is null)
        {
            return;
        }

        var targetWorkbookXml = context.LoadCurrentTargetWorkbookXml();
        var targetRoot = targetWorkbookXml.Root;
        if (targetRoot is null)
            return;

        var changed = false;
        if (MergeChildBlock(sourceRevisionPointer, targetRoot, workbookNs + "revisionPtr"))
            changed = true;
        if (MergeFileVersion(sourceFileVersion, targetRoot, workbookNs + "fileVersion"))
            changed = true;
        if (MergeFileSharing(sourceFileSharing, targetRoot, workbookNs, workbook.FileSharing is not null))
            changed = true;
        if (MergeFileRecoveryProperties(sourceFileRecoveryProperties, targetRoot, workbookNs + "fileRecoveryPr"))
            changed = true;
        if (MergeSmartTagProperties(sourceSmartTagProperties, targetRoot, workbookNs + "smartTagPr"))
            changed = true;
        if (MergeSmartTagTypes(sourceSmartTagTypes, targetRoot, workbookNs + "smartTagTypes"))
            changed = true;
        if (MergeFunctionGroups(sourceFunctionGroups, targetRoot, workbookNs + "functionGroups"))
            changed = true;
        if (MergeWorkbookProperties(sourceWorkbookProperties, targetRoot, workbookNs))
            changed = true;
        if (MergeWorkbookProtection(sourceWorkbookProtection, targetRoot, workbookNs, HasModeledWorkbookProtection(workbook)))
            changed = true;
        if (MergeCalculationProperties(sourceCalculationProperties, targetRoot, workbookNs))
            changed = true;
        if (MergeWorkbookViews(sourceBookViews, targetRoot, workbookNs))
            changed = true;
        if (MergeCustomWorkbookViews(sourceCustomWorkbookViews, targetRoot, workbookNs, XlsxCustomViewMapper.GetModeledIds(workbook)))
            changed = true;
        if (MergeDefinedNames(sourceDefinedNames, targetRoot, workbookNs, sourceWorkbookXml, workbook, sourceSheetIdsByLocalId))
            changed = true;
        if (MergeChildBlock(sourceOleSize, targetRoot, workbookNs + "oleSize"))
            changed = true;
        if (MergeChildBlock(sourceWebPublishing, targetRoot, workbookNs + "webPublishing"))
            changed = true;
        if (MergeChildBlock(sourceWebPublishObjects, targetRoot, workbookNs + "webPublishObjects"))
            changed = true;
        var workbookExtensionRelationshipIdMap =
            XlsxExtensionListPackageRelationshipRebinder.BuildRelationshipIdMap(
                sourceArchive,
                targetArchive,
                "xl/workbook.xml",
                "xl/workbook.xml");
        if (XlsxNativeXmlMerger.MergeExtensionList(
                sourceExtensionList,
                targetRoot,
                workbookNs,
                workbookExtensionRelationshipIdMap))
            changed = true;

        if (changed)
            context.ReplaceTargetWorkbookXml(targetWorkbookXml);
    }

    private static bool MergeChildBlock(XElement? sourceBlock, XElement targetRoot, XName blockName)
    {
        if (sourceBlock is null || targetRoot.Element(blockName) is not null)
            return false;

        targetRoot.Add(new XElement(sourceBlock));
        return true;
    }

    private static bool HasCompleteRevisionHistorySidecarGraph(XlsxSourcePackagePreservationContext context)
    {
        const string revisionHeadersRelationshipType =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/revisionHeaders";
        const string revisionLogRelationshipType =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/revisionLog";
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookRelationshipsXml = context.SourceWorkbookRelationshipsXml;
        if (workbookRelationshipsXml is null)
            return false;

        var sourceArchive = context.SourceArchive;
        foreach (var relationship in workbookRelationshipsXml.Root?.Elements(relationshipNs + "Relationship") ?? [])
        {
            if (!IsInternalRelationshipOfType(relationship, revisionHeadersRelationshipType))
                continue;

            var revisionHeaderPath = XlsxPackagePath.ResolveRelationshipTarget(
                "xl/workbook.xml",
                relationship.Attribute("Target")!.Value.Trim());
            if (!revisionHeaderPath.StartsWith("xl/revisionHeaders/", StringComparison.OrdinalIgnoreCase) ||
                sourceArchive.GetEntry(revisionHeaderPath) is null)
            {
                continue;
            }

            if (RevisionHeaderReferencesExistingRevisionLog(sourceArchive, revisionHeaderPath, revisionLogRelationshipType, relationshipNs))
                return true;
        }

        return false;
    }

    private static bool RevisionHeaderReferencesExistingRevisionLog(
        ZipArchive sourceArchive,
        string revisionHeaderPath,
        string revisionLogRelationshipType,
        XNamespace relationshipNs)
    {
        var revisionHeaderRelationshipsEntry = sourceArchive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(revisionHeaderPath));
        if (revisionHeaderRelationshipsEntry is null)
            return false;

        var revisionHeaderRelationshipsXml = XlsxPackageXmlEditor.LoadXml(revisionHeaderRelationshipsEntry);
        foreach (var relationship in revisionHeaderRelationshipsXml.Root?.Elements(relationshipNs + "Relationship") ?? [])
        {
            if (!IsInternalRelationshipOfType(relationship, revisionLogRelationshipType))
                continue;

            var revisionLogPath = XlsxPackagePath.ResolveRelationshipTarget(
                revisionHeaderPath,
                relationship.Attribute("Target")!.Value.Trim());
            if (revisionLogPath.StartsWith("xl/revisions/", StringComparison.OrdinalIgnoreCase) &&
                sourceArchive.GetEntry(revisionLogPath) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInternalRelationshipOfType(XElement relationship, string relationshipType)
    {
        if (!string.Equals(relationship.Attribute("Type")?.Value.Trim(), relationshipType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(relationship.Attribute("Target")?.Value))
            return false;

        var targetMode = relationship.Attribute("TargetMode")?.Value;
        return string.IsNullOrWhiteSpace(targetMode) ||
               string.Equals(targetMode.Trim(), "Internal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MergeChildBlocks(IReadOnlyCollection<XElement> sourceBlocks, XElement targetRoot, XName blockName)
    {
        if (sourceBlocks.Count == 0 || targetRoot.Element(blockName) is not null)
            return false;

        foreach (var sourceBlock in sourceBlocks)
            targetRoot.Add(new XElement(sourceBlock));
        return true;
    }

    private static bool MergeFileVersion(XElement? sourceBlock, XElement targetRoot, XName blockName)
    {
        if (sourceBlock is null || targetRoot.Element(blockName) is not null)
            return false;

        var clone = new XElement(sourceBlock);
        XlsxWorkbookLeafElementNormalizer.Normalize(clone);
        targetRoot.Add(clone);
        return true;
    }

    private static bool MergeFileRecoveryProperties(IReadOnlyCollection<XElement> sourceBlocks, XElement targetRoot, XName blockName)
    {
        if (sourceBlocks.Count == 0 || targetRoot.Element(blockName) is not null)
            return false;

        foreach (var sourceBlock in sourceBlocks)
        {
            var clone = new XElement(sourceBlock);
            XlsxWorkbookLeafElementNormalizer.Normalize(clone);
            targetRoot.Add(clone);
        }

        return true;
    }

    private static bool MergeFunctionGroups(XElement? sourceBlock, XElement targetRoot, XName blockName)
    {
        if (sourceBlock is null || targetRoot.Element(blockName) is not null)
            return false;

        var clone = new XElement(sourceBlock);
        XlsxWorkbookFunctionGroupsNormalizer.NormalizeElement(clone);
        targetRoot.Add(clone);
        return true;
    }

    private static bool MergeSmartTagProperties(XElement? sourceBlock, XElement targetRoot, XName blockName)
    {
        if (sourceBlock is null || targetRoot.Element(blockName) is not null)
            return false;

        var clone = new XElement(sourceBlock);
        XlsxWorkbookSmartTagNormalizer.NormalizeSmartTagPropertiesElement(clone);
        targetRoot.Add(clone);
        return true;
    }

    private static bool MergeSmartTagTypes(XElement? sourceBlock, XElement targetRoot, XName blockName)
    {
        if (sourceBlock is null || targetRoot.Element(blockName) is not null)
            return false;

        var clone = new XElement(sourceBlock);
        XlsxWorkbookSmartTagNormalizer.NormalizeSmartTagTypesElement(clone);
        if (XlsxWorkbookSmartTagNormalizer.ShouldRemoveSmartTagTypesElement(clone))
            return false;

        targetRoot.Add(clone);
        return true;
    }

    private static bool MergeFileSharing(
        XElement? sourceFileSharing,
        XElement targetRoot,
        XNamespace workbookNs,
        bool hasModeledFileSharing)
    {
        if (sourceFileSharing is null)
            return false;

        var targetFileSharing = targetRoot.Element(workbookNs + "fileSharing");
        if (targetFileSharing is null)
        {
            if (!hasModeledFileSharing)
            {
                var clone = new XElement(sourceFileSharing);
                XlsxWorkbookLeafElementNormalizer.Normalize(clone);
                targetRoot.Add(clone);
                return true;
            }

            var cloned = new XElement(sourceFileSharing);
            RemoveModeledFileSharingAttributes(cloned);
            XlsxWorkbookLeafElementNormalizer.Normalize(cloned);
            if (!cloned.HasAttributes && !cloned.HasElements)
                return false;

            targetRoot.Add(cloned);
            return true;
        }

        var changed = XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(
            sourceFileSharing,
            targetFileSharing,
            [XName.Get("readOnlyRecommended"), XName.Get("userName"), XName.Get("reservationPassword")]);
        if (XlsxWorkbookLeafElementNormalizer.Normalize(targetFileSharing))
            changed = true;

        return changed;
    }

    private static void RemoveModeledFileSharingAttributes(XElement fileSharing)
    {
        fileSharing.Attribute("readOnlyRecommended")?.Remove();
        fileSharing.Attribute("userName")?.Remove();
        fileSharing.Attribute("reservationPassword")?.Remove();
    }

    private static bool MergeCustomWorkbookViews(
        XElement? sourceCustomWorkbookViews,
        XElement targetRoot,
        XNamespace workbookNs,
        IReadOnlySet<string> modeledCustomViewIds)
    {
        if (sourceCustomWorkbookViews is null)
            return false;

        var targetCustomWorkbookViews = targetRoot.Element(workbookNs + "customWorkbookViews");
        if (targetCustomWorkbookViews is null)
        {
            if (modeledCustomViewIds.Count > 0)
            {
                var retainedViews = sourceCustomWorkbookViews
                    .Elements(workbookNs + "customWorkbookView")
                    .Where(view => !modeledCustomViewIds.Contains(XlsxCustomViewMapper.NormalizeId(view.Attribute("guid")?.Value) ?? string.Empty))
                    .Select(CloneCustomWorkbookViewForPreservation)
                    .ToList();
                if (retainedViews.Count == 0)
                    return false;

                InsertCustomWorkbookViewsInOrder(
                    targetRoot,
                    workbookNs,
                    new XElement(
                        sourceCustomWorkbookViews.Name,
                        sourceCustomWorkbookViews.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration),
                        retainedViews));
                return true;
            }

            InsertCustomWorkbookViewsInOrder(
                targetRoot,
                workbookNs,
                new XElement(
                    sourceCustomWorkbookViews.Name,
                    sourceCustomWorkbookViews.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration),
                    sourceCustomWorkbookViews
                        .Elements(workbookNs + "customWorkbookView")
                        .Select(CloneCustomWorkbookViewForPreservation)));
            return true;
        }

        var changed = MergeMissingAttributes(sourceCustomWorkbookViews, targetCustomWorkbookViews, []);
        var targetViewsById = targetCustomWorkbookViews
            .Elements(workbookNs + "customWorkbookView")
            .Select(view => new
            {
                Id = XlsxCustomViewMapper.NormalizeId(view.Attribute("guid")?.Value),
                View = view
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().View, StringComparer.OrdinalIgnoreCase);

        foreach (var originalSourceView in sourceCustomWorkbookViews.Elements(workbookNs + "customWorkbookView"))
        {
            var sourceView = CloneCustomWorkbookViewForPreservation(originalSourceView);
            var id = XlsxCustomViewMapper.NormalizeId(sourceView.Attribute("guid")?.Value);
            if (!string.IsNullOrWhiteSpace(id) && targetViewsById.TryGetValue(id, out var targetView))
            {
                changed |= MergeMissingAttributes(sourceView, targetView, ["name", "guid"]);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(id) && modeledCustomViewIds.Contains(id))
                continue;

            targetCustomWorkbookViews.Add(sourceView);
            if (!string.IsNullOrWhiteSpace(id))
                targetViewsById[id] = targetCustomWorkbookViews.Elements(workbookNs + "customWorkbookView").Last();
            changed = true;
        }

        return changed;
    }

    private static XElement CloneCustomWorkbookViewForPreservation(XElement sourceView)
    {
        var clone = new XElement(sourceView);
        if (XlsxCustomViewMapper.NormalizeId(clone.Attribute("guid")?.Value) is { } id)
            clone.SetAttributeValue("guid", id);
        if (string.IsNullOrWhiteSpace(clone.Attribute("activeSheetId")?.Value))
            clone.SetAttributeValue("activeSheetId", "1");
        return clone;
    }

    private static bool HasModeledWorkbookProtection(Workbook workbook) =>
        workbook.IsStructureProtected ||
        !string.IsNullOrWhiteSpace(workbook.StructureProtectionPassword) ||
        workbook.ProtectionMetadata is not null;

    private static bool MergeWorkbookProtection(
        XElement? sourceWorkbookProtection,
        XElement targetRoot,
        XNamespace workbookNs,
        bool hasModeledWorkbookProtection)
    {
        if (sourceWorkbookProtection is null)
            return false;

        var targetWorkbookProtection = targetRoot.Element(workbookNs + "workbookProtection");
        if (targetWorkbookProtection is null)
        {
            // No target element means the CURRENT model state has no protection to write (see
            // ApplyProtection's own early-return, which uses this same tri-state check). Most
            // commonly this is exactly what a resave after "Unprotect Workbook" produces
            // (IsStructureProtected/StructureProtectionPassword/ProtectionMetadata all cleared) --
            // cloning the stale pre-edit source element back in here would silently resurrect the
            // protection the user just removed, so leave the target alone in that case.
            if (!hasModeledWorkbookProtection)
                return false;

            var clone = new XElement(sourceWorkbookProtection);
            XlsxWorkbookLeafElementNormalizer.Normalize(clone);
            targetRoot.AddFirst(clone);
            return true;
        }

        // Every workbookProtection attribute is model-governed: lockStructure/workbookPassword are
        // written directly from Workbook.IsStructureProtected/StructureProtectionPassword, and every
        // other attribute (the modern workbookAlgorithmName/workbookHashValue/workbookSaltValue/
        // workbookSpinCount quartet, revisionsPassword, lockWindows, ...) is carried verbatim through
        // Workbook.ProtectionMetadata (see XlsxWorkbookMetadataReader.LoadProtectionMetadata /
        // XlsxWorkbookMetadataWriter.ApplyProtection). A target workbookProtection element existing
        // here means ApplyProtection already ran and wrote the model's current, authoritative state --
        // so, unlike the other native-attribute merges in this file, nothing should be blindly copied
        // back from the stale pre-edit source element (that would resurrect a revoked password's
        // verifier alongside a freshly-set one; see
        // FreeXCleanupMED15Tests.ProtectWorkbookCommand_AfterUnprotectingModernHashWorkbook_DropsStaleVerifierForOldPassword).
        // Only the normalizer runs, to keep formatting consistent with the wholesale-clone branch above.
        return XlsxWorkbookLeafElementNormalizer.Normalize(targetWorkbookProtection);
    }

    private static bool MergeCalculationProperties(XElement? sourceCalculationProperties, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceCalculationProperties is null)
            return false;

        var targetCalculationProperties = targetRoot.Element(workbookNs + "calcPr");
        if (targetCalculationProperties is null)
        {
            var cloned = new XElement(sourceCalculationProperties);
            XlsxWorkbookLeafElementNormalizer.Normalize(cloned);
            targetRoot.Add(cloned);
            return true;
        }

        string[] modeledAttributes =
        [
            "calcMode",
            "fullCalcOnLoad",
            "forceFullCalc",
            "iterate",
            "iterateCount",
            "iterateDelta",
            "fullPrecision"
        ];
        var modeledAttributeNames = modeledAttributes
            .Select(name => XName.Get(name))
            .ToHashSet();

        var changed = false;
        foreach (var attribute in sourceCalculationProperties.Attributes())
        {
            if (modeledAttributeNames.Contains(attribute.Name))
                continue;

            if (targetCalculationProperties.Attribute(attribute.Name)?.Value == attribute.Value)
                continue;

            targetCalculationProperties.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        if (XlsxWorkbookLeafElementNormalizer.Normalize(targetCalculationProperties))
            changed = true;

        return changed;
    }

    private static bool MergeWorkbookProperties(XElement? sourceWorkbookProperties, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceWorkbookProperties is null)
            return false;

        XName[] modeledAttributes = ["date1904"];
        var targetWorkbookProperties = targetRoot.Element(workbookNs + "workbookPr");
        if (targetWorkbookProperties is null)
        {
            var cloned = new XElement(sourceWorkbookProperties);
            foreach (var attribute in modeledAttributes)
                cloned.Attribute(attribute)?.Remove();
            XlsxWorkbookLeafElementNormalizer.Normalize(cloned);

            if (!cloned.HasAttributes && !cloned.HasElements)
                return false;

            targetRoot.AddFirst(cloned);
            return true;
        }

        var changed = XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(
            sourceWorkbookProperties,
            targetWorkbookProperties,
            modeledAttributes);
        if (XlsxWorkbookLeafElementNormalizer.Normalize(targetWorkbookProperties))
            changed = true;

        return changed;
    }

    private static bool MergeWorkbookViews(XElement? sourceBookViews, XElement targetRoot, XNamespace workbookNs)
    {
        var sourceViews = sourceBookViews?
            .Elements(workbookNs + "workbookView")
            .ToList()
            ?? [];
        if (sourceViews.Count == 0)
            return false;

        var targetBookViews = targetRoot.Element(workbookNs + "bookViews");
        if (targetBookViews is null)
        {
            targetRoot.AddFirst(CloneWorkbookViewsForPreservation(sourceBookViews!, workbookNs));
            return true;
        }

        var targetViews = targetBookViews
            .Elements(workbookNs + "workbookView")
            .ToList();
        var existingRawViews = targetViews
            .Select(view => view.ToString(System.Xml.Linq.SaveOptions.DisableFormatting))
            .ToHashSet(StringComparer.Ordinal);

        var changed = false;
        var mergedTargetViewKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // The first <workbookView> is always the "primary" one: XlsxWorkbookMetadataWriter.
        // ApplyWorkbookViewProperties targets it positionally and may already have rewritten
        // its firstSheet/activeTab to the workbook's current sheet-selection state before this
        // preservation pass runs, while the cloned source view below still carries the
        // pre-edit values. Keying the merge on those mutable attributes would then never
        // match (see WorkbookViewIdentityKey), causing the stale source view to be appended
        // as a bogus second window instead of merged in place. Match the primary view by
        // position instead, and only fall back to key-based identity matching for any
        // additional views, which represent genuine extra windows (Window > New Window) that
        // ApplyWorkbookViewProperties never touches.
        var primaryTargetView = targetViews.Count > 0 ? targetViews[0] : null;
        var primaryTargetViewMerged = false;
        for (var sourceIndex = 0; sourceIndex < sourceViews.Count; sourceIndex++)
        {
            var sourceView = CloneWorkbookViewForPreservation(sourceViews[sourceIndex]);
            var raw = sourceView.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
            if (existingRawViews.Contains(raw))
                continue;

            XElement? targetView = null;
            if (sourceIndex == 0 && !primaryTargetViewMerged && primaryTargetView is not null)
            {
                targetView = primaryTargetView;
            }
            else
            {
                var sourceViewKey = WorkbookViewIdentityKey(sourceView);
                if (IsPrimaryWorkbookView(sourceView) && !mergedTargetViewKeys.Contains(sourceViewKey))
                {
                    // A non-primary source view (a genuine extra window) must never be matched against
                    // the primary target view here, even when it shares the same firstSheet/activeTab
                    // key: the primary was already claimed by position above (or is reserved for the
                    // sourceIndex==0 iteration), so re-matching it against a *different* source view
                    // would silently swallow a real second window into the primary instead of appending
                    // it (R33-meta-1). Only non-primary target views are eligible identity-key matches.
                    targetView = FindWorkbookViewByIdentityKey(
                        targetViews.Where(view => !ReferenceEquals(view, primaryTargetView)),
                        sourceViewKey);
                }
            }

            if (targetView is not null)
            {
                XName[] modeledPrimaryViewAttributes =
                [
                    "showSheetTabs",
                    "tabRatio",
                    "firstSheet",
                    "activeTab"
                ];
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceView, targetView, modeledPrimaryViewAttributes))
                    changed = true;
                if (XlsxWorkbookViewNormalizer.NormalizeWorkbookViewElement(targetView))
                    changed = true;
                if (ReferenceEquals(targetView, primaryTargetView))
                    primaryTargetViewMerged = true;
                else
                    mergedTargetViewKeys.Add(WorkbookViewIdentityKey(targetView));
                continue;
            }

            targetBookViews.Add(new XElement(sourceView));
            targetViews.Add(targetBookViews.Elements(workbookNs + "workbookView").Last());
            existingRawViews.Add(raw);
            changed = true;
        }

        return changed;

        static string WorkbookViewIdentityKey(XElement view)
        {
            var firstSheet = view.Attribute("firstSheet")?.Value ?? string.Empty;
            var activeTab = view.Attribute("activeTab")?.Value ?? string.Empty;
            return $"{firstSheet}\u001f{activeTab}";
        }

        static bool IsPrimaryWorkbookView(XElement view)
        {
            var visibility = view.Attribute("visibility")?.Value;
            return string.IsNullOrWhiteSpace(visibility) ||
                   string.Equals(visibility, "visible", StringComparison.OrdinalIgnoreCase);
        }

        static XElement? FindWorkbookViewByIdentityKey(IEnumerable<XElement> views, string sourceViewKey)
        {
            foreach (var view in views)
            {
                if (string.Equals(WorkbookViewIdentityKey(view), sourceViewKey, StringComparison.OrdinalIgnoreCase))
                    return view;
            }

            return null;
        }
    }

    private static XElement CloneWorkbookViewsForPreservation(XElement sourceBookViews, XNamespace workbookNs)
    {
        var clone = new XElement(sourceBookViews.Name, sourceBookViews.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration));
        foreach (var child in sourceBookViews.Elements())
        {
            clone.Add(child.Name == workbookNs + "workbookView"
                ? CloneWorkbookViewForPreservation(child)
                : new XElement(child));
        }

        return clone;
    }

    private static XElement CloneWorkbookViewForPreservation(XElement sourceView)
    {
        var clone = new XElement(sourceView);
        XlsxXmlPreservationPolicy.RemoveOfficeRevisionAttributes(clone);
        XlsxWorkbookViewNormalizer.NormalizeWorkbookViewElement(clone);
        return clone;
    }

    private static bool MergeDefinedNames(
        XElement? sourceDefinedNames,
        XElement targetRoot,
        XNamespace workbookNs,
        XDocument sourceWorkbookXml,
        Workbook workbook,
        IReadOnlyList<SheetId> sourceSheetIdsByLocalId)
    {
        var sourceNames = sourceDefinedNames?
            .Elements(workbookNs + "definedName")
            .ToList()
            ?? [];
        if (sourceNames.Count == 0)
            return false;

        // Sheet-scoped defined names merged in here (Excel-reserved or otherwise never loaded into
        // the model - see XlsxFileAdapter.SourcePackageSnapshot.RestorePatchWorkbookDefinedNames's
        // resurrection-gate comments) carry a localSheetId that indexes the PRISTINE pre-edit sheet
        // order. A sheet delete/reorder shifts that index, so it must be remapped by sheet NAME onto
        // the CURRENT (already-written target) sheet order rather than cloned verbatim (P112) -
        // otherwise the name ends up scoped to the wrong sheet, or carries an out-of-range index.
        var sourceSheetNamesByLocalId = sourceWorkbookXml.Root?
            .Element(workbookNs + "sheets")?
            .Elements(workbookNs + "sheet")
            .Select(sheet => sheet.Attribute("name")?.Value ?? string.Empty)
            .ToList()
            ?? [];
        var targetSheetNames = targetRoot.Element(workbookNs + "sheets")?
            .Elements(workbookNs + "sheet")
            .Select(sheet => sheet.Attribute("name")?.Value ?? string.Empty)
            .ToList()
            ?? [];
        var preservationPolicy = new XlsxDefinedNamePreservationPolicy(
            workbook,
            sourceSheetNamesByLocalId,
            sourceSheetIdsByLocalId,
            targetSheetNames);

        var targetDefinedNames = targetRoot.Element(workbookNs + "definedNames");
        var existingKeys = targetDefinedNames?
            .Elements(workbookNs + "definedName")
            .Select(XlsxDefinedNamePreservationPolicy.GetKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var sourceName in sourceNames)
        {
            if (!preservationPolicy.TryPrepareCandidate(sourceName, out var candidate))
                continue;

            var key = XlsxDefinedNamePreservationPolicy.GetKey(candidate);
            if (existingKeys.Contains(key))
            {
                // This name was already re-emitted into the target by the full-rebuild name
                // write-back (e.g. a formula/constant-refersTo name via NamedFormulas, which has no
                // Hidden/Comment metadata slot on the model - unlike plain ranges'
                // NamedRangeMetadataByName). Backfill any attribute (hidden, comment, ...) present on
                // the pristine source element but missing from the freshly-written one, mirroring
                // RestorePatchWorkbookDefinedNames' backfill for the patch-save path, so a live,
                // unchanged name's hidden/comment attributes survive a full rebuild too.
                var existingElement = targetDefinedNames?
                    .Elements(workbookNs + "definedName")
                    .FirstOrDefault(element => string.Equals(
                        XlsxDefinedNamePreservationPolicy.GetKey(element),
                        key,
                        StringComparison.OrdinalIgnoreCase));
                if (existingElement is not null &&
                    XlsxDefinedNamePreservationPolicy.BackfillMissingAttributes(candidate, existingElement))
                    changed = true;

                continue;
            }

            if (!preservationPolicy.ShouldPreserveMissingCandidate(candidate))
                continue;

            if (targetDefinedNames is null)
            {
                targetDefinedNames = new XElement(workbookNs + "definedNames");
                targetRoot.Add(targetDefinedNames);
            }

            targetDefinedNames.Add(candidate);
            existingKeys.Add(key);
            changed = true;
        }

        return changed;
    }

    private static void InsertCustomWorkbookViewsInOrder(
        XElement? workbookRoot,
        XNamespace workbookNs,
        XElement customWorkbookViews)
    {
        if (workbookRoot is null)
            return;

        string[] laterWorkbookElements =
        [
            "pivotCaches",
            "smartTagPr",
            "smartTagTypes",
            "webPublishing",
            "fileRecoveryPr",
            "webPublishObjects",
            "extLst"
        ];

        XElement? insertionPoint = null;
        foreach (var element in workbookRoot.Elements())
        {
            if (element.Name.Namespace != workbookNs ||
                !laterWorkbookElements.Contains(element.Name.LocalName, StringComparer.Ordinal))
            {
                continue;
            }

            insertionPoint = element;
            break;
        }
        if (insertionPoint is null)
            workbookRoot.Add(customWorkbookViews);
        else
            insertionPoint.AddBeforeSelf(customWorkbookViews);
    }

    private static bool MergeMissingAttributes(
        XElement sourceElement,
        XElement targetElement,
        IReadOnlyCollection<string> excludedLocalNames) =>
        XlsxXmlPreservationPolicy.MergeMissingAttributes(sourceElement, targetElement, excludedLocalNames);
}
