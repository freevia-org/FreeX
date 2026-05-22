using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxWorksheetMetadataPreserver
{
    private static readonly HashSet<string> ModeledPrintOptionsAttributes = new(StringComparer.Ordinal)
    {
        "gridLines",
        "headings",
        "horizontalCentered",
        "verticalCentered"
    };

    private static readonly HashSet<string> ModeledDimensionAttributes = new(StringComparer.Ordinal)
    {
        "ref"
    };

    private static readonly HashSet<string> ModeledPageMarginsAttributes = new(StringComparer.Ordinal)
    {
        "left",
        "right",
        "top",
        "bottom"
    };

    private static readonly HashSet<string> ModeledPageSetupAttributes = new(StringComparer.Ordinal)
    {
        "paperSize",
        "scale",
        "firstPageNumber",
        "fitToWidth",
        "fitToHeight",
        "pageOrder",
        "orientation",
        "useFirstPageNumber",
        "blackAndWhite",
        "draft",
        "cellComments",
        "errors",
        "horizontalDpi",
        "verticalDpi"
    };

    private static readonly HashSet<string> ModeledHeaderFooterAttributes = new(StringComparer.Ordinal)
    {
        "differentOddEven",
        "differentFirst",
        "scaleWithDoc",
        "alignWithMargins"
    };

    private static readonly HashSet<string> ModeledMergeCellsAttributes = new(StringComparer.Ordinal)
    {
        "count"
    };

    private static readonly HashSet<string> ModeledMergeCellAttributes = new(StringComparer.Ordinal)
    {
        "ref"
    };

    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive, Workbook workbook)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XName[] retainedChildNames =
        [
            workbookNs + "customSheetViews",
            workbookNs + "scenarios",
            workbookNs + "ignoredErrors",
            workbookNs + "cellWatches",
            workbookNs + "sheetCalcPr",
            workbookNs + "phoneticPr",
            workbookNs + "sortState",
            workbookNs + "dataConsolidate",
            workbookNs + "legacyDrawing",
            workbookNs + "legacyDrawingHF",
            workbookNs + "picture",
            workbookNs + "customProperties",
            workbookNs + "smartTags",
            workbookNs + "singleXmlCells",
            workbookNs + "autoFilter",
            workbookNs + "protectedRanges",
            workbookNs + "rowBreaks",
            workbookNs + "colBreaks",
            workbookNs + "queryTableParts",
            workbookNs + "webPublishItems",
            workbookNs + "oleObjects",
            workbookNs + "controls"
        ];

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
        var targetWorkbookXml = XlsxPackageXmlEditor.LoadXml(targetWorkbookEntry);
        var sourceWorkbookRels = XlsxRelationshipReader.LoadTargets(
            sourceArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
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
            var sourceBlocks = retainedChildNames
                .Select(name => sourceWorksheetXml.Root?.Element(name))
                .Where(element => element is not null)
                .Cast<XElement>()
                .ToList();
            var sourceSheetProperties = sourceWorksheetXml.Root?.Element(workbookNs + "sheetPr");
            var sourceSheetFormatProperties = sourceWorksheetXml.Root?.Element(workbookNs + "sheetFormatPr");
            var sourceDimension = sourceWorksheetXml.Root?.Element(workbookNs + "dimension");
            var sourcePrintOptions = sourceWorksheetXml.Root?.Element(workbookNs + "printOptions");
            var sourcePageMargins = sourceWorksheetXml.Root?.Element(workbookNs + "pageMargins");
            var sourcePageSetup = sourceWorksheetXml.Root?.Element(workbookNs + "pageSetup");
            var sourceHeaderFooter = sourceWorksheetXml.Root?.Element(workbookNs + "headerFooter");
            var sourceMergeCells = sourceWorksheetXml.Root?.Element(workbookNs + "mergeCells");
            var sourceColumns = sourceWorksheetXml.Root?.Element(workbookNs + "cols");
            var sourceSheetData = sourceWorksheetXml.Root?.Element(workbookNs + "sheetData");
            var sourceSheetProtection = sourceWorksheetXml.Root?.Element(workbookNs + "sheetProtection");
            var sourceSheetViews = sourceWorksheetXml.Root?.Element(workbookNs + "sheetViews");
            var sourceHyperlinks = sourceWorksheetXml.Root?.Element(workbookNs + "hyperlinks");
            var sourceExtensionList = sourceWorksheetXml.Root?.Element(workbookNs + "extLst");
            if (sourceBlocks.Count == 0 &&
                sourceSheetProperties is null &&
                sourceSheetFormatProperties is null &&
                sourceDimension is null &&
                sourcePrintOptions is null &&
                sourcePageMargins is null &&
                sourcePageSetup is null &&
                sourceHeaderFooter is null &&
                sourceMergeCells is null &&
                sourceColumns is null &&
                sourceSheetData is null &&
                sourceSheetProtection is null &&
                sourceSheetViews is null &&
                sourceHyperlinks is null &&
                sourceExtensionList is null)
            {
                continue;
            }

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            var changed = false;
            if (MergeWorksheetSheetProperties(sourceSheetProperties, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetSheetFormatProperties(sourceSheetFormatProperties, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetNativeOnlyElementAttributes(
                    sourceDimension,
                    targetRoot,
                    workbookNs + "dimension",
                    ModeledDimensionAttributes))
                changed = true;
            if (MergeWorksheetNativeOnlyElementAttributes(
                    sourcePrintOptions,
                    targetRoot,
                    workbookNs + "printOptions",
                    ModeledPrintOptionsAttributes))
                changed = true;
            if (MergeWorksheetNativeOnlyElementAttributes(
                    sourcePageMargins,
                    targetRoot,
                    workbookNs + "pageMargins",
                    ModeledPageMarginsAttributes))
                changed = true;
            if (MergeWorksheetNativeOnlyElementAttributes(
                    sourcePageSetup,
                    targetRoot,
                    workbookNs + "pageSetup",
                    ModeledPageSetupAttributes))
                changed = true;
            if (MergeWorksheetNativeOnlyElementAttributes(
                    sourceHeaderFooter,
                    targetRoot,
                    workbookNs + "headerFooter",
                    ModeledHeaderFooterAttributes))
                changed = true;
            if (MergeWorksheetColumnAttributes(sourceColumns, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetRowAttributes(sourceSheetData, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetCellAttributes(sourceSheetData, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetInlineStringMetadata(sourceSheetData, targetRoot, targetArchive, workbookNs))
                changed = true;
            if (MergeWorksheetFormulaMetadata(sourceSheetData, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetMergedCellMetadata(sourceMergeCells, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetSheetProtection(sourceSheetProtection, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetSheetViews(sourceSheetViews, targetRoot, workbookNs))
                changed = true;
            if (MergeWorksheetHyperlinkMetadata(sourceHyperlinks, targetRoot, workbookNs, relNs))
                changed = true;
            foreach (var sourceBlock in sourceBlocks)
            {
                if (sourceBlock.Name == workbookNs + "protectedRanges")
                {
                    if (MergeWorksheetProtectedRanges(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxAllowEditRangeMapper.GetModeledReferences(workbook, sheetName)))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "sheetCalcPr")
                {
                    if (MergeWorksheetCalculationProperties(sourceBlock, targetRoot, workbookNs))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "phoneticPr")
                {
                    if (MergeWorksheetPhoneticProperties(sourceBlock, targetRoot, workbookNs))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "customSheetViews")
                {
                    if (MergeWorksheetCustomSheetViews(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxCustomViewMapper.GetModeledIds(workbook)))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "customProperties")
                {
                    if (MergeWorksheetCustomProperties(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxWorksheetCustomPropertyMapper.GetModeledNames(workbook, sheetName)))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "rowBreaks")
                {
                    if (MergeWorksheetBreaks(
                            sourceBlock,
                            targetRoot,
                            workbookNs,
                            GetModeledWorksheetBreakIds(workbook, sheetName, rowBreaks: true),
                            CellAddress.MaxRow))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "colBreaks")
                {
                    if (MergeWorksheetBreaks(
                            sourceBlock,
                            targetRoot,
                            workbookNs,
                            GetModeledWorksheetBreakIds(workbook, sheetName, rowBreaks: false),
                            CellAddress.MaxCol))
                    {
                        changed = true;
                    }

                    continue;
                }
                if (sourceBlock.Name == workbookNs + "ignoredErrors" &&
                    XlsxWorksheetDiagnosticsMapper.MergeIgnoredErrors(sourceBlock, targetRoot, workbookNs))
                {
                    changed = true;
                    continue;
                }
                if (sourceBlock.Name == workbookNs + "cellWatches" &&
                    XlsxWorksheetDiagnosticsMapper.MergeCellWatches(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxWorksheetDiagnosticsMapper.GetModeledCellWatchReferences(workbook, sheetName)))
                {
                    changed = true;
                    continue;
                }

                if (sourceBlock.Name == workbookNs + "scenarios" &&
                    MergeWorksheetScenarios(
                        sourceBlock,
                        targetRoot,
                        workbookNs,
                        XlsxWorksheetScenarioMapper.GetModeledNamesForSheet(workbook, sheetName)))
                {
                    changed = true;
                }
                if (sourceBlock.Name == workbookNs + "scenarios")
                    continue;

                if (targetRoot.Element(sourceBlock.Name) is not null)
                    continue;

                targetRoot.Add(new XElement(sourceBlock));
                changed = true;
            }

            if (XlsxNativeXmlMerger.MergeExtensionList(sourceExtensionList, targetRoot, workbookNs))
                changed = true;

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);
        }
    }

    private static void InsertWorksheetMetadataElementInOrder(
        XElement worksheetRoot,
        XNamespace workbookNs,
        XElement metadataElement)
    {
        string[] laterWorksheetElements = metadataElement.Name.LocalName switch
        {
            "sheetCalcPr" =>
            [
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            "protectedRanges" =>
            [
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            "scenarios" =>
            [
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            "customSheetViews" =>
            [
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            "phoneticPr" =>
            [
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            "customProperties" =>
            [
                "cellWatches",
                "ignoredErrors",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            "cellWatches" =>
            [
                "ignoredErrors",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            "queryTableParts" =>
            [
                "extLst"
            ],
            _ =>
            [
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ]
        };

        var insertionPoint = worksheetRoot.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == workbookNs &&
                laterWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
        if (insertionPoint is null)
            worksheetRoot.Add(metadataElement);
        else
            insertionPoint.AddBeforeSelf(metadataElement);
    }

    private static bool MergeWorksheetNativeOnlyElementAttributes(
        XElement? sourceElement,
        XElement targetRoot,
        XName elementName,
        HashSet<string> modeledAttributeNames)
    {
        if (sourceElement is null)
            return false;

        var retainedAttributes = sourceElement
            .Attributes()
            .Where(attribute => IsNativeOnlyWorksheetAttribute(attribute, modeledAttributeNames))
            .Select(attribute => new XAttribute(attribute))
            .ToList();
        var retainedChildren = sourceElement
            .Elements()
            .Select(element => new XElement(element))
            .ToList();
        if (retainedAttributes.Count == 0 && retainedChildren.Count == 0)
            return false;

        var targetElement = targetRoot.Element(elementName);
        if (targetElement is null)
        {
            targetRoot.Add(new XElement(elementName, retainedAttributes, retainedChildren));
            return true;
        }

        var changed = false;
        foreach (var attribute in retainedAttributes)
        {
            if (targetElement.Attribute(attribute.Name) is not null)
                continue;

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var existingChildrenByKey = targetElement
            .Elements()
            .GroupBy(ElementIdentityKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var child in retainedChildren)
        {
            var key = ElementIdentityKey(child);
            if (existingChildrenByKey.ContainsKey(key))
                continue;

            targetElement.Add(child);
            existingChildrenByKey[key] = child;
            changed = true;
        }

        return changed;
    }

    private static bool IsNativeOnlyWorksheetAttribute(XAttribute attribute, HashSet<string> modeledAttributeNames)
    {
        if (attribute.IsNamespaceDeclaration)
            return false;

        if (attribute.Name.NamespaceName.Length == 0 &&
            modeledAttributeNames.Contains(attribute.Name.LocalName))
        {
            return false;
        }

        return attribute.Name != XName.Get(
            "id",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
    }

    private static HashSet<uint> GetModeledWorksheetBreakIds(Workbook workbook, string sheetName, bool rowBreaks)
    {
        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return [];

        var maxBreakId = rowBreaks ? CellAddress.MaxRow : CellAddress.MaxCol;
        return (rowBreaks ? sheet.RowPageBreaks : sheet.ColumnPageBreaks)
            .Where(id => IsSupportedWorksheetBreakId(id, maxBreakId))
            .ToHashSet();
    }

    private static bool MergeWorksheetBreaks(
        XElement sourceBreaks,
        XElement targetRoot,
        XNamespace workbookNs,
        HashSet<uint> modeledBreakIds,
        uint maxBreakId)
    {
        var targetBreaks = targetRoot.Element(sourceBreaks.Name);
        if (targetBreaks is null)
        {
            var retainedBreaks = sourceBreaks
                .Elements(workbookNs + "brk")
                .Where(sourceBreak =>
                    !TryGetSupportedWorksheetBreakId(sourceBreak, maxBreakId, out var sourceId) ||
                    modeledBreakIds.Contains(sourceId))
                .Select(sourceBreak => new XElement(sourceBreak))
                .ToList();
            if (retainedBreaks.Count == 0)
                return false;

            targetRoot.Add(new XElement(sourceBreaks.Name, sourceBreaks.Attributes(), retainedBreaks));
            return true;
        }

        var changed = false;
        foreach (var attribute in sourceBreaks.Attributes())
        {
            if (targetBreaks.Attribute(attribute.Name) is not null)
                continue;

            targetBreaks.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var targetBreaksBySupportedId = targetBreaks
            .Elements(workbookNs + "brk")
            .Select(element => new
            {
                Element = element,
                Parsed = TryGetSupportedWorksheetBreakId(element, maxBreakId, out var id),
                Id = id
            })
            .Where(entry => entry.Parsed)
            .GroupBy(entry => entry.Id)
            .ToDictionary(
                group => group.Key,
                group => group.First().Element);
        var targetBreaksByRawId = targetBreaks
            .Elements(workbookNs + "brk")
            .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("id")?.Value))
            .GroupBy(element => element.Attribute("id")!.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var sourceBreak in sourceBreaks.Elements(workbookNs + "brk"))
        {
            var id = sourceBreak.Attribute("id")?.Value;
            if (TryGetSupportedWorksheetBreakId(sourceBreak, maxBreakId, out var sourceId))
            {
                if (!modeledBreakIds.Contains(sourceId))
                    continue;

                if (targetBreaksBySupportedId.TryGetValue(sourceId, out var targetBreak))
                {
                    changed |= MergeMissingAttributes(sourceBreak, targetBreak);
                    continue;
                }

                targetBreaks.Add(new XElement(sourceBreak));
                var addedBreak = targetBreaks.Elements(workbookNs + "brk").Last();
                targetBreaksBySupportedId[sourceId] = addedBreak;
                if (!string.IsNullOrWhiteSpace(id))
                    targetBreaksByRawId[id] = addedBreak;
                changed = true;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(id) &&
                targetBreaksByRawId.ContainsKey(id))
            {
                continue;
            }

            targetBreaks.Add(new XElement(sourceBreak));
            if (!string.IsNullOrWhiteSpace(id))
                targetBreaksByRawId[id] = targetBreaks.Elements(workbookNs + "brk").Last();
            changed = true;
        }

        return changed;
    }

    private static bool TryGetSupportedWorksheetBreakId(XElement breakElement, uint maxBreakId, out uint id)
    {
        id = 0;
        var rawId = breakElement.Attribute("id")?.Value;
        if (string.IsNullOrWhiteSpace(rawId) ||
            !uint.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out id))
        {
            return false;
        }

        return IsSupportedWorksheetBreakId(id, maxBreakId);
    }

    private static bool IsSupportedWorksheetBreakId(uint id, uint maxBreakId)
    {
        return id >= 2 && id <= maxBreakId;
    }

    private static bool MergeWorksheetCalculationProperties(
        XElement sourceSheetCalcPr,
        XElement targetRoot,
        XNamespace workbookNs)
    {
        var targetSheetCalcPr = targetRoot.Element(workbookNs + "sheetCalcPr");
        if (targetSheetCalcPr is null)
        {
            var retained = new XElement(sourceSheetCalcPr);
            retained.Attribute("fullCalcOnLoad")?.Remove();
            if (!retained.HasAttributes && !retained.HasElements)
                return false;

            InsertWorksheetMetadataElementInOrder(targetRoot, workbookNs, retained);
            return true;
        }

        var changed = MergeMissingAttributes(sourceSheetCalcPr, targetSheetCalcPr, ["fullCalcOnLoad"]);
        foreach (var sourceChild in sourceSheetCalcPr.Elements())
        {
            var targetChild = targetSheetCalcPr.Elements(sourceChild.Name)
                .FirstOrDefault(child => ElementIdentityKey(child) == ElementIdentityKey(sourceChild));
            if (targetChild is not null)
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetSheetCalcPr.Add(new XElement(sourceChild));
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetPhoneticProperties(
        XElement sourcePhoneticPr,
        XElement targetRoot,
        XNamespace workbookNs)
    {
        var modeledAttributes = new[] { "fontId", "type", "alignment" };
        var targetPhoneticPr = targetRoot.Element(workbookNs + "phoneticPr");
        if (targetPhoneticPr is null)
        {
            var retained = new XElement(sourcePhoneticPr);
            foreach (var attributeName in modeledAttributes)
                retained.Attribute(attributeName)?.Remove();
            if (!retained.HasAttributes && !retained.HasElements)
                return false;

            InsertWorksheetMetadataElementInOrder(targetRoot, workbookNs, retained);
            return true;
        }

        var changed = MergeMissingAttributes(sourcePhoneticPr, targetPhoneticPr, modeledAttributes);
        foreach (var sourceChild in sourcePhoneticPr.Elements())
        {
            var targetChild = targetPhoneticPr.Elements(sourceChild.Name)
                .FirstOrDefault(child => ElementIdentityKey(child) == ElementIdentityKey(sourceChild));
            if (targetChild is not null)
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetPhoneticPr.Add(new XElement(sourceChild));
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetCustomProperties(
        XElement sourceCustomProperties,
        XElement targetRoot,
        XNamespace workbookNs,
        IReadOnlySet<string> modeledPropertyNames)
    {
        var targetCustomProperties = targetRoot.Element(workbookNs + "customProperties");
        if (targetCustomProperties is null)
        {
            var retainedProperties = sourceCustomProperties
                .Elements(workbookNs + "customPr")
                .Where(property => !IsSupportedWorksheetCustomProperty(property))
                .Select(property => new XElement(property))
                .ToList();
            if (retainedProperties.Count == 0)
                return false;

            InsertWorksheetMetadataElementInOrder(
                targetRoot,
                workbookNs,
                new XElement(sourceCustomProperties.Name, sourceCustomProperties.Attributes(), retainedProperties));
            return true;
        }

        var changed = MergeMissingAttributes(sourceCustomProperties, targetCustomProperties, []);
        var targetPropertiesByName = targetCustomProperties
            .Elements(workbookNs + "customPr")
            .Select(property => new
            {
                Name = property.Attribute("name")?.Value,
                Element = property
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Element, StringComparer.OrdinalIgnoreCase);

        foreach (var sourceProperty in sourceCustomProperties.Elements(workbookNs + "customPr"))
        {
            var name = sourceProperty.Attribute("name")?.Value;
            if (!string.IsNullOrWhiteSpace(name) && targetPropertiesByName.TryGetValue(name, out var targetProperty))
            {
                changed |= MergeMissingAttributes(sourceProperty, targetProperty, ["name", "id"]);
                foreach (var sourceChild in sourceProperty.Elements())
                {
                    var targetChild = targetProperty.Elements(sourceChild.Name)
                        .FirstOrDefault(child => ElementIdentityKey(child) == ElementIdentityKey(sourceChild));
                    if (targetChild is not null)
                    {
                        if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                            changed = true;
                        continue;
                    }

                    targetProperty.Add(new XElement(sourceChild));
                    changed = true;
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(name) && modeledPropertyNames.Contains(name))
                continue;

            if (IsSupportedWorksheetCustomProperty(sourceProperty))
                continue;

            targetCustomProperties.Add(new XElement(sourceProperty));
            if (!string.IsNullOrWhiteSpace(name))
                targetPropertiesByName[name] = targetCustomProperties.Elements(workbookNs + "customPr").Last();
            changed = true;
        }

        return changed;
    }

    private static bool IsSupportedWorksheetCustomProperty(XElement customProperty)
    {
        return !string.IsNullOrWhiteSpace(customProperty.Attribute("name")?.Value) &&
               int.TryParse(
                   customProperty.Attribute("id")?.Value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out var id) &&
               id > 0;
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

    private static bool MergeMissingAttributes(XElement sourceElement, XElement targetElement)
    {
        var changed = false;
        foreach (var attribute in sourceElement.Attributes())
        {
            if (targetElement.Attribute(attribute.Name) is not null)
                continue;

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetSheetFormatProperties(XElement? sourceSheetFormatProperties, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetFormatProperties is null)
            return false;

        var targetSheetFormatProperties = targetRoot.Element(workbookNs + "sheetFormatPr");
        if (targetSheetFormatProperties is null)
        {
            targetRoot.AddFirst(new XElement(sourceSheetFormatProperties));
            return true;
        }

        string[] nativeOnlyAttributes =
        [
            "baseColWidth",
            "zeroHeight",
            "thickTop",
            "thickBottom",
            "outlineLevelRow",
            "outlineLevelCol"
        ];
        var nativeOnlyAttributeNames = nativeOnlyAttributes
            .Select(name => XName.Get(name))
            .ToHashSet();

        var changed = false;
        foreach (var attribute in sourceSheetFormatProperties.Attributes())
        {
            if (targetSheetFormatProperties.Attribute(attribute.Name) is not null &&
                !nativeOnlyAttributeNames.Contains(attribute.Name))
            {
                continue;
            }

            if (targetSheetFormatProperties.Attribute(attribute.Name)?.Value == attribute.Value)
                continue;

            targetSheetFormatProperties.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        var existingChildrenByKey = targetSheetFormatProperties
            .Elements()
            .GroupBy(ElementIdentityKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var sourceChild in sourceSheetFormatProperties.Elements())
        {
            var key = ElementIdentityKey(sourceChild);
            if (existingChildrenByKey.ContainsKey(key))
                continue;

            targetSheetFormatProperties.Add(new XElement(sourceChild));
            existingChildrenByKey[key] = targetSheetFormatProperties.Elements().Last();
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetSheetViews(XElement? sourceSheetViews, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetViews is null)
            return false;

        var sourceViews = sourceSheetViews.Elements(workbookNs + "sheetView").ToList();
        if (sourceViews.Count == 0)
            return false;

        var targetSheetViews = targetRoot.Element(workbookNs + "sheetViews");
        if (targetSheetViews is null)
        {
            targetRoot.AddFirst(new XElement(sourceSheetViews));
            return true;
        }

        var existingViewIds = targetSheetViews
            .Elements(workbookNs + "sheetView")
            .Select(element => element.Attribute("workbookViewId")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        if (MergeMissingAttributes(sourceSheetViews, targetSheetViews, []))
            changed = true;

        foreach (var sourceView in sourceViews)
        {
            var viewId = sourceView.Attribute("workbookViewId")?.Value;
            var targetView = !string.IsNullOrWhiteSpace(viewId)
                ? targetSheetViews
                    .Elements(workbookNs + "sheetView")
                    .FirstOrDefault(element => string.Equals(
                        element.Attribute("workbookViewId")?.Value,
                        viewId,
                        StringComparison.OrdinalIgnoreCase))
                : null;
            if (targetView is not null)
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceView, targetView))
                    changed = true;
                continue;
            }

            targetSheetViews.Add(new XElement(sourceView));
            if (!string.IsNullOrWhiteSpace(viewId))
                existingViewIds.Add(viewId);
            changed = true;
        }

        return changed;
    }

    private static string ElementIdentityKey(XElement element)
    {
        var address = element.Attribute("pane")?.Value
            ?? element.Attribute("sqref")?.Value
            ?? element.Attribute("ref")?.Value
            ?? element.Attribute("r")?.Value
            ?? element.Attribute("activeCell")?.Value
            ?? element.Attribute("name")?.Value
            ?? element.Attribute("id")?.Value
            ?? element.Attribute("uid")?.Value
            ?? element.Attribute("uri")?.Value
            ?? string.Empty;
        return $"{element.Name}\u001f{address}";
    }

    private static bool MergeWorksheetProtectedRanges(
        XElement sourceProtectedRanges,
        XElement targetRoot,
        XNamespace workbookNs,
        IReadOnlySet<string> modeledSqrefs)
    {
        var targetProtectedRanges = targetRoot.Element(workbookNs + "protectedRanges");

        var changed = false;
        var targetBySqref = targetProtectedRanges is null
            ? new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase)
            : targetProtectedRanges
                .Elements(workbookNs + "protectedRange")
                .Select(element => (Element: element, Key: CanonicalSupportedProtectedRangeSqref(element)))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .GroupBy(pair => pair.Key!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Element,
                    StringComparer.OrdinalIgnoreCase);

        foreach (var sourceRange in sourceProtectedRanges.Elements(workbookNs + "protectedRange"))
        {
            var sourceSqref = CanonicalSupportedProtectedRangeSqref(sourceRange);
            if (!string.IsNullOrWhiteSpace(sourceSqref))
            {
                if (!modeledSqrefs.Contains(sourceSqref) ||
                    !targetBySqref.TryGetValue(sourceSqref, out var targetRange))
                {
                    continue;
                }

                if (MergeProtectedRangeMetadata(sourceRange, targetRange))
                    changed = true;
                continue;
            }

            if (targetProtectedRanges is null)
            {
                targetProtectedRanges = new XElement(workbookNs + "protectedRanges");
                targetRoot.Add(targetProtectedRanges);
                changed = true;
            }

            if (!HasEquivalentProtectedRange(targetProtectedRanges, sourceRange, workbookNs))
            {
                targetProtectedRanges.Add(new XElement(sourceRange));
                changed = true;
            }
        }

        return changed;
    }

    private static string? CanonicalSupportedProtectedRangeSqref(XElement protectedRange)
    {
        var sqref = protectedRange.Attribute("sqref")?.Value;
        if (string.IsNullOrWhiteSpace(sqref))
            return null;

        var tokens = sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length != 1)
            return null;

        return TryParseSqrefToken(tokens[0], SheetId.New(), out var range)
            ? range.ToString()
            : null;
    }

    private static bool TryParseSqrefToken(string token, SheetId sheet, out GridRange range)
    {
        range = default;
        var parts = token.Split(':');
        if (parts.Length == 1)
        {
            if (!CellAddress.TryParse(parts[0], sheet, out var address))
                return false;

            range = new GridRange(address, address);
            return true;
        }

        if (parts.Length == 2 &&
            CellAddress.TryParse(parts[0], sheet, out var start) &&
            CellAddress.TryParse(parts[1], sheet, out var end))
        {
            range = new GridRange(start, end);
            return true;
        }

        return false;
    }

    private static bool MergeProtectedRangeMetadata(XElement sourceRange, XElement targetRange)
    {
        var changed = false;
        foreach (var sourceAttribute in sourceRange.Attributes())
        {
            if (sourceAttribute.Name == "sqref")
                continue;

            if (targetRange.Attribute(sourceAttribute.Name)?.Value == sourceAttribute.Value)
                continue;

            targetRange.SetAttributeValue(sourceAttribute.Name, sourceAttribute.Value);
            changed = true;
        }

        if (MergeMissingNativeChildren(sourceRange, targetRange, _ => true))
        {
            changed = true;
        }

        return changed;
    }

    private static bool HasEquivalentProtectedRange(
        XElement targetProtectedRanges,
        XElement sourceRange,
        XNamespace workbookNs)
    {
        var sourceSqref = sourceRange.Attribute("sqref")?.Value;
        var sourceName = sourceRange.Attribute("name")?.Value;
        return targetProtectedRanges
            .Elements(workbookNs + "protectedRange")
            .Any(targetRange =>
                (!string.IsNullOrWhiteSpace(sourceSqref) &&
                 string.Equals(targetRange.Attribute("sqref")?.Value, sourceSqref, StringComparison.OrdinalIgnoreCase)) ||
                (string.IsNullOrWhiteSpace(sourceSqref) &&
                 !string.IsNullOrWhiteSpace(sourceName) &&
                 string.Equals(targetRange.Attribute("name")?.Value, sourceName, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool MergeWorksheetSheetProtection(XElement? sourceSheetProtection, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetProtection is null)
            return false;

        var targetSheetProtection = targetRoot.Element(workbookNs + "sheetProtection");
        if (targetSheetProtection is null)
        {
            targetRoot.Add(new XElement(sourceSheetProtection));
            return true;
        }

        return XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(
            sourceSheetProtection,
            targetSheetProtection);
    }

    private static bool MergeMissingNativeChildren(
        XElement sourceElement,
        XElement targetElement,
        Func<XElement, bool> shouldRetain)
    {
        var existingChildrenByKey = targetElement
            .Elements()
            .GroupBy(ElementIdentityKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var changed = false;
        foreach (var sourceChild in sourceElement.Elements().Where(shouldRetain))
        {
            var key = ElementIdentityKey(sourceChild);
            if (existingChildrenByKey.TryGetValue(key, out var targetChild))
            {
                if (XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(sourceChild, targetChild))
                    changed = true;
                continue;
            }

            targetElement.Add(new XElement(sourceChild));
            existingChildrenByKey[key] = targetElement.Elements().Last();
            changed = true;
        }

        return changed;
    }

    private static bool MergeWorksheetSheetProperties(XElement? sourceSheetProperties, XElement targetRoot, XNamespace workbookNs)
    {
        if (sourceSheetProperties is null)
            return false;

        var targetSheetProperties = targetRoot.Element(workbookNs + "sheetPr");
        if (targetSheetProperties is null)
        {
            targetRoot.AddFirst(new XElement(sourceSheetProperties));
            return true;
        }

        return XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren(
            sourceSheetProperties,
            targetSheetProperties);
    }

}

