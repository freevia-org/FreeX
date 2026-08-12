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
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var sourceWorkbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        if (sourceWorkbookEntry is null || targetWorkbookEntry is null)
            return;

        var sourceWorkbookXml = XlsxPackageXmlEditor.LoadXml(sourceWorkbookEntry);
        var sourceRevisionPointer = sourceWorkbookXml.Root?.Element(workbookNs + "revisionPtr");
        if (sourceRevisionPointer is not null && !HasCompleteRevisionHistorySidecarGraph(sourceArchive))
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

        var targetWorkbookXml = XlsxPackageXmlEditor.LoadXml(targetWorkbookEntry);
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
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, "xl/workbook.xml", targetWorkbookXml);
    }

    private static bool MergeChildBlock(XElement? sourceBlock, XElement targetRoot, XName blockName)
    {
        if (sourceBlock is null || targetRoot.Element(blockName) is not null)
            return false;

        targetRoot.Add(new XElement(sourceBlock));
        return true;
    }

    private static bool HasCompleteRevisionHistorySidecarGraph(ZipArchive sourceArchive)
    {
        const string revisionHeadersRelationshipType =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/revisionHeaders";
        const string revisionLogRelationshipType =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/revisionLog";
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookRelationshipsEntry = sourceArchive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookRelationshipsEntry is null)
            return false;

        var workbookRelationshipsXml = XlsxPackageXmlEditor.LoadXml(workbookRelationshipsEntry);
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
        RemoveOfficeRevisionAttributes(clone);
        XlsxWorkbookViewNormalizer.NormalizeWorkbookViewElement(clone);
        return clone;
    }

    private static void RemoveOfficeRevisionAttributes(XElement element)
    {
        foreach (var attribute in element.Attributes().Where(IsOfficeRevisionAttribute).ToList())
            attribute.Remove();

        foreach (var namespaceAttribute in element.Attributes().Where(attribute =>
                     attribute.IsNamespaceDeclaration &&
                     IsOfficeRevisionNamespace(attribute.Value) &&
                     !element.Attributes().Any(other =>
                         !other.IsNamespaceDeclaration &&
                         other.Name.NamespaceName == attribute.Value)).ToList())
        {
            namespaceAttribute.Remove();
        }
    }

    private static bool IsOfficeRevisionAttribute(XAttribute attribute) =>
        !attribute.IsNamespaceDeclaration &&
        string.Equals(attribute.Name.LocalName, "uid", StringComparison.Ordinal) &&
        IsOfficeRevisionNamespace(attribute.Name.NamespaceName);

    private static bool IsOfficeRevisionNamespace(string namespaceName) =>
        namespaceName.StartsWith("http://schemas.microsoft.com/office/spreadsheetml/", StringComparison.Ordinal) &&
        namespaceName.Contains("/revision", StringComparison.Ordinal);

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

        var targetDefinedNames = targetRoot.Element(workbookNs + "definedNames");
        var existingKeys = targetDefinedNames?
            .Elements(workbookNs + "definedName")
            .Select(DefinedNameKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Keys (name + current local-sheet scope) of every defined name still live in the workbook
        // model, in the same format DefinedNameKey produces here AFTER the localSheetId remap below
        // (that remap targets the current model/target sheet order, matching GetLiveDefinedNameKeys's
        // model-order localSheetId). A model-representable source name whose remapped key is absent
        // here was deleted from the Name Manager - resurrecting it from the pristine source snapshot
        // would silently bring it back forever, so it is gated out just as
        // RestorePatchWorkbookDefinedNames gates the patch-save path.
        var liveModelDefinedNameKeys = XlsxNamedRangeMapper.GetLiveDefinedNameKeys(workbook);

        var changed = false;
        foreach (var sourceName in sourceNames)
        {
            var candidate = new XElement(sourceName);
            var localSheetIdAttr = candidate.Attribute("localSheetId");
            if (localSheetIdAttr is not null)
            {
                if (!int.TryParse(localSheetIdAttr.Value, out var oldLocalSheetId) ||
                    oldLocalSheetId < 0 ||
                    oldLocalSheetId >= sourceSheetNamesByLocalId.Count)
                    continue;

                var scopeSheetName = sourceSheetNamesByLocalId[oldLocalSheetId];
                var newLocalSheetId = targetSheetNames.FindIndex(
                    name => string.Equals(name, scopeSheetName, StringComparison.OrdinalIgnoreCase));
                if (newLocalSheetId < 0)
                {
                    // The old scope-sheet name isn't present under any current sheet BY NAME. This is
                    // ambiguous between the sheet having been DELETED (drop the name, per P112) and
                    // the sheet having simply been RENAMED with no other structural change (the sheet
                    // - and this name's scope - is still there, just under a new name). Count+ordinal
                    // alone can't tell those apart: deleting a sheet and adding an unrelated one at
                    // the same ordinal also leaves the count and position matching. Disambiguate by
                    // identity instead - a rename keeps the SAME Sheet object (and its stable
                    // Sheet.Id) alive; a delete+add always produces a brand-new Sheet.Id that was
                    // never present at this snapshot's pristine load/rebase. Only treat this as a
                    // rename when the ORIGINAL sheet's Sheet.Id genuinely still exists (mirrors
                    // R27-io-workbook-parts-deep-2 / R28-meta-3 in RestorePatchWorkbookDefinedNames;
                    // that path otherwise silently picks up the slack for this specific case, but the
                    // drop is still visible for other rename combinations without this fix).
                    var renamedSheetIndex = -1;
                    if (oldLocalSheetId < sourceSheetIdsByLocalId.Count)
                    {
                        var originalSheetId = sourceSheetIdsByLocalId[oldLocalSheetId];
                        for (var sheetIndex = 0; sheetIndex < workbook.Sheets.Count; sheetIndex++)
                        {
                            if (workbook.Sheets[sheetIndex].Id == originalSheetId)
                            {
                                renamedSheetIndex = sheetIndex;
                                break;
                            }
                        }
                    }

                    if (renamedSheetIndex < 0)
                        continue;

                    newLocalSheetId = renamedSheetIndex;
                }

                localSheetIdAttr.Value = newLocalSheetId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            var key = DefinedNameKey(candidate);
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
                    .FirstOrDefault(element => string.Equals(DefinedNameKey(element), key, StringComparison.OrdinalIgnoreCase));
                if (existingElement is not null)
                {
                    foreach (var attribute in candidate.Attributes())
                    {
                        if (existingElement.Attribute(attribute.Name) is not null)
                            continue;

                        existingElement.SetAttributeValue(attribute.Name, attribute.Value);
                        changed = true;
                    }
                }

                continue;
            }

            // Liveness gate: never resurrect a model-representable name the user deleted from the
            // Name Manager. Names FreeX cannot round-trip through the model (validator-rejected, or
            // an unmodelable refers-to such as a constant/#REF!/external-workbook reference) and
            // Excel-reserved names (Print_Area etc.) are absent from liveModelDefinedNameKeys for
            // reasons unrelated to deletion - they were never loaded into the model - so they stay
            // exempt from the gate and are still preserved. Mirrors RestorePatchWorkbookDefinedNames'
            // resurrection gate. (A model-representable name that IS live was already re-emitted into
            // the target by the name write-back, so it is caught by the existingKeys check above and
            // never reaches here.)
            var sourceNameAttr = candidate.Attribute("name")?.Value;
            var isModelRepresentable = !string.IsNullOrWhiteSpace(sourceNameAttr) &&
                workbook.ValidateNamedRangeName(sourceNameAttr) is null &&
                !XlsxNamedRangeMapper.IsUnmodelableDefinedNameRefersTo(candidate.Value);

            if (TryGetPrintSettingKind(sourceNameAttr, out var printSettingKind) &&
                localSheetIdAttr is not null &&
                int.TryParse(localSheetIdAttr.Value, out var scopeSheetIndex) &&
                scopeSheetIndex >= 0 &&
                scopeSheetIndex < workbook.Sheets.Count)
            {
                // Print_Area/Print_Titles ARE modeled (Sheet.PrintAreas / Sheet.PrintTitleRows|
                // PrintTitleColumns) even though they are Excel-reserved names, unlike the OTHER
                // reserved names (_FilterDatabase, Criteria, Database, Extract, Consolidate_Area,
                // _xlchart.*) which FreeX never loads into the model at all and therefore always
                // preserves verbatim below via the reserved-name exemption. liveModelDefinedNameKeys
                // can't help distinguish "live" from "cleared" here either -
                // XlsxNamedRangeMapper.CreateDefinedNameEntries deliberately excludes ALL reserved
                // names from that set (it feeds the general Name-Manager write-back, not print
                // settings), so it never contains a Print_Area/Print_Titles key even when the
                // sheet's print area/titles are still set - which is exactly what let the reserved-
                // name exemption below unconditionally resurrect a print area/titles the user just
                // cleared (Sheet.SetPrintAreas([]) / PrintTitleRows=null), even though the
                // full-rebuild write-back above (XlsxFileAdapter.Save.cs) correctly omits the
                // _xlnm.Print_Area/_xlnm.Print_Titles name for a cleared sheet. So for these two
                // names specifically, check the CURRENT model state of the sheet this candidate is
                // scoped to directly - using localSheetIdAttr's value, which was already remapped
                // above (by sheet name, or by Sheet.Id on rename) to the CURRENT sheet position -
                // instead of exempting it from the gate. When it IS still live, the full rebuild
                // will already have re-emitted it under the same key, so this candidate would have
                // been caught by the existingKeys branch above; reaching here with isLive true only
                // happens if that emission is somehow missing, in which case falling through to
                // resurrect the pristine value (below) is still the safest match to the model.
                var scopeSheet = workbook.Sheets[scopeSheetIndex];
                var isLive = printSettingKind == PrintSettingKind.PrintArea
                    ? scopeSheet.PrintAreas.Count > 0
                    : scopeSheet.PrintTitleRows is not null || scopeSheet.PrintTitleColumns is not null;
                if (!isLive)
                    continue;
            }
            else if (isModelRepresentable &&
                !liveModelDefinedNameKeys.Contains(key) &&
                !XlsxNamedRangeMapper.IsExcelReservedDefinedName(sourceNameAttr))
            {
                continue;
            }

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

        static string DefinedNameKey(XElement element)
        {
            var name = element.Attribute("name")?.Value ?? string.Empty;
            var localSheetId = element.Attribute("localSheetId")?.Value ?? string.Empty;
            return $"{name}\u001f{localSheetId}";
        }
    }

    private enum PrintSettingKind
    {
        PrintArea,
        PrintTitles,
    }

    // Matches the reserved defined-name identifying a sheet's print area or print titles
    // (repeat rows/columns), whether stored with the standard OOXML "_xlnm." built-in-name
    // prefix (e.g. "_xlnm.Print_Area") or, for oddly-authored/legacy files, bare
    // ("Print_Area") - mirroring the two forms XlsxNamedRangeMapper.IsExcelReservedDefinedName
    // itself recognizes.
    private static bool TryGetPrintSettingKind(string? name, out PrintSettingKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmed = name.Trim();
        var unprefixed = trimmed.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase)
            ? trimmed["_xlnm.".Length..]
            : trimmed;

        if (string.Equals(unprefixed, "Print_Area", StringComparison.OrdinalIgnoreCase))
        {
            kind = PrintSettingKind.PrintArea;
            return true;
        }

        if (string.Equals(unprefixed, "Print_Titles", StringComparison.OrdinalIgnoreCase))
        {
            kind = PrintSettingKind.PrintTitles;
            return true;
        }

        return false;
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
        IReadOnlyCollection<string> excludedLocalNames)
    {
        var changed = false;
        foreach (var attribute in sourceElement.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                IsOfficeRevisionAttribute(attribute) ||
                excludedLocalNames.Contains(attribute.Name.LocalName, StringComparer.Ordinal) ||
                targetElement.Attribute(attribute.Name) is not null)
            {
                continue;
            }

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }
}
