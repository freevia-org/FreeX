using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorkbookMetadataPreserver
{
    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive, Workbook workbook)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var sourceWorkbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        if (sourceWorkbookEntry is null || targetWorkbookEntry is null)
            return;

        var sourceWorkbookXml = XlsxPackageXmlEditor.LoadXml(sourceWorkbookEntry);
        var sourceRevisionPointer = sourceWorkbookXml.Root?.Element(workbookNs + "revisionPtr");
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
        if (MergeChildBlock(sourceFileVersion, targetRoot, workbookNs + "fileVersion"))
            changed = true;
        if (MergeChildBlock(sourceFileSharing, targetRoot, workbookNs + "fileSharing"))
            changed = true;
        if (MergeChildBlocks(sourceFileRecoveryProperties, targetRoot, workbookNs + "fileRecoveryPr"))
            changed = true;
        if (MergeChildBlock(sourceSmartTagProperties, targetRoot, workbookNs + "smartTagPr"))
            changed = true;
        if (MergeChildBlock(sourceSmartTagTypes, targetRoot, workbookNs + "smartTagTypes"))
            changed = true;
        if (MergeChildBlock(sourceFunctionGroups, targetRoot, workbookNs + "functionGroups"))
            changed = true;
        if (MergeWorkbookProperties(sourceWorkbookProperties, targetRoot, workbookNs))
            changed = true;
        if (MergeWorkbookProtection(sourceWorkbookProtection, targetRoot, workbookNs))
            changed = true;
        if (MergeCalculationProperties(sourceCalculationProperties, targetRoot, workbookNs))
            changed = true;
        if (MergeWorkbookViews(sourceBookViews, targetRoot, workbookNs))
            changed = true;
        if (MergeCustomWorkbookViews(sourceCustomWorkbookViews, targetRoot, workbookNs, XlsxCustomViewMapper.GetModeledIds(workbook)))
            changed = true;
        if (MergeDefinedNames(sourceDefinedNames, targetRoot, workbookNs))
            changed = true;
        if (MergeChildBlock(sourceOleSize, targetRoot, workbookNs + "oleSize"))
            changed = true;
        if (MergeChildBlock(sourceWebPublishing, targetRoot, workbookNs + "webPublishing"))
            changed = true;
        if (MergeChildBlock(sourceWebPublishObjects, targetRoot, workbookNs + "webPublishObjects"))
            changed = true;
        if (XlsxNativeXmlMerger.MergeExtensionList(sourceExtensionList, targetRoot, workbookNs))
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

    private static bool MergeChildBlocks(IReadOnlyCollection<XElement> sourceBlocks, XElement targetRoot, XName blockName)
    {
        if (sourceBlocks.Count == 0 || targetRoot.Element(blockName) is not null)
            return false;

        foreach (var sourceBlock in sourceBlocks)
            targetRoot.Add(new XElement(sourceBlock));
        return true;
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
                    .Select(view => new XElement(view))
                    .ToList();
                if (retainedViews.Count == 0)
                    return false;

                InsertCustomWorkbookViewsInOrder(
                    targetRoot,
                    workbookNs,
                    new XElement(sourceCustomWorkbookViews.Name, sourceCustomWorkbookViews.Attributes(), retainedViews));
                return true;
            }

            InsertCustomWorkbookViewsInOrder(targetRoot, workbookNs, new XElement(sourceCustomWorkbookViews));
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

        foreach (var sourceView in sourceCustomWorkbookViews.Elements(workbookNs + "customWorkbookView"))
        {
            var id = XlsxCustomViewMapper.NormalizeId(sourceView.Attribute("guid")?.Value);
            if (!string.IsNullOrWhiteSpace(id) && targetViewsById.TryGetValue(id, out var targetView))
            {
                changed |= MergeMissingAttributes(sourceView, targetView, ["name", "guid"]);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(id) && modeledCustomViewIds.Contains(id))
                continue;

            targetCustomWorkbookViews.Add(new XElement(sourceView));
            if (!string.IsNullOrWhiteSpace(id))
                targetViewsById[id] = targetCustomWorkbookViews.Elements(workbookNs + "customWorkbookView").Last();
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorkbookProtection(XElement? sourceWorkbookProtection, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceWorkbookProtection is null)
            return false;

        var targetWorkbookProtection = targetRoot.Element(workbookNs + "workbookProtection");
        if (targetWorkbookProtection is null)
        {
            targetRoot.AddFirst(new XElement(sourceWorkbookProtection));
            return true;
        }

        return XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(
            sourceWorkbookProtection,
            targetWorkbookProtection);
    }

    private static bool MergeCalculationProperties(XElement? sourceCalculationProperties, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceCalculationProperties is null)
            return false;

        var targetCalculationProperties = targetRoot.Element(workbookNs + "calcPr");
        if (targetCalculationProperties is null)
        {
            targetRoot.Add(new XElement(sourceCalculationProperties));
            return true;
        }

        string[] modeledAttributes =
        [
            "calcMode",
            "fullCalcOnLoad",
            "forceFullCalc",
            "iterate",
            "iterateCount",
            "iterateDelta"
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

        return changed;
    }

    private static bool MergeWorkbookProperties(XElement? sourceWorkbookProperties, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceWorkbookProperties is null)
            return false;

        XName[] modeledAttributes = [workbookNs + "date1904"];
        var targetWorkbookProperties = targetRoot.Element(workbookNs + "workbookPr");
        if (targetWorkbookProperties is null)
        {
            var cloned = new XElement(sourceWorkbookProperties);
            foreach (var attribute in modeledAttributes)
                cloned.Attribute(attribute)?.Remove();

            if (!cloned.HasAttributes && !cloned.HasElements)
                return false;

            targetRoot.AddFirst(cloned);
            return true;
        }

        return XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(
            sourceWorkbookProperties,
            targetWorkbookProperties,
            modeledAttributes);
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
            targetRoot.AddFirst(new XElement(sourceBookViews!));
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
        foreach (var sourceView in sourceViews)
        {
            var raw = sourceView.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
            if (existingRawViews.Contains(raw))
                continue;

            var sourceViewKey = WorkbookViewIdentityKey(sourceView);
            var targetView = IsPrimaryWorkbookView(sourceView) && !mergedTargetViewKeys.Contains(sourceViewKey)
                ? targetViews.FirstOrDefault(view => string.Equals(
                    WorkbookViewIdentityKey(view),
                    sourceViewKey,
                    StringComparison.OrdinalIgnoreCase))
                : null;
            if (targetView is not null)
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceView, targetView))
                    changed = true;
                mergedTargetViewKeys.Add(sourceViewKey);
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
    }

    private static bool MergeDefinedNames(XElement? sourceDefinedNames, XElement targetRoot, XNamespace workbookNs)
    {
        var sourceNames = sourceDefinedNames?
            .Elements(workbookNs + "definedName")
            .ToList()
            ?? [];
        if (sourceNames.Count == 0)
            return false;

        var targetDefinedNames = targetRoot.Element(workbookNs + "definedNames");
        if (targetDefinedNames is null)
        {
            targetRoot.Add(new XElement(sourceDefinedNames!));
            return true;
        }

        var existingKeys = targetDefinedNames
            .Elements(workbookNs + "definedName")
            .Select(DefinedNameKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var sourceName in sourceNames)
        {
            var key = DefinedNameKey(sourceName);
            if (existingKeys.Contains(key))
                continue;

            targetDefinedNames.Add(new XElement(sourceName));
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

        var insertionPoint = workbookRoot.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == workbookNs &&
                laterWorkbookElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
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
