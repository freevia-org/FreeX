using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxWorksheetMetadataPreserver
{
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
            Dictionary<string, XElement>? targetCellsByAddress = null;
            IReadOnlyDictionary<string, XElement> GetTargetCellsByAddress() =>
                targetCellsByAddress ??= BuildCellLookup(targetRoot.Element(workbookNs + "sheetData"), workbookNs);

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
            if (MergeWorksheetCellNativeMetadata(sourceSheetData, GetTargetCellsByAddress, targetArchive, workbookNs))
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

    public static void Preserve(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        Workbook workbook,
        XlsxSourcePackagePreservationContext? context)
    {
        if (context is null)
        {
            Preserve(sourceArchive, targetArchive, workbook);
            return;
        }

        XName[] retainedChildNames =
        [
            context.WorkbookNs + "customSheetViews",
            context.WorkbookNs + "scenarios",
            context.WorkbookNs + "ignoredErrors",
            context.WorkbookNs + "cellWatches",
            context.WorkbookNs + "sheetCalcPr",
            context.WorkbookNs + "phoneticPr",
            context.WorkbookNs + "sortState",
            context.WorkbookNs + "dataConsolidate",
            context.WorkbookNs + "legacyDrawing",
            context.WorkbookNs + "legacyDrawingHF",
            context.WorkbookNs + "picture",
            context.WorkbookNs + "customProperties",
            context.WorkbookNs + "smartTags",
            context.WorkbookNs + "singleXmlCells",
            context.WorkbookNs + "autoFilter",
            context.WorkbookNs + "protectedRanges",
            context.WorkbookNs + "rowBreaks",
            context.WorkbookNs + "colBreaks",
            context.WorkbookNs + "queryTableParts",
            context.WorkbookNs + "webPublishItems",
            context.WorkbookNs + "oleObjects",
            context.WorkbookNs + "controls"
        ];

        PreserveWorksheetMetadata(
            sourceArchive,
            targetArchive,
            workbook,
            retainedChildNames,
            context.WorkbookNs,
            context.RelNs,
            context.SourceSheets,
            context.TargetSheets,
            context);
    }

    private static void PreserveWorksheetMetadata(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        Workbook workbook,
        XName[] retainedChildNames,
        XNamespace workbookNs,
        XNamespace relNs,
        IReadOnlyDictionary<string, string> sourceSheets,
        IReadOnlyDictionary<string, string> targetSheets,
        XlsxSourcePackagePreservationContext? context = null)
    {
        foreach (var (sheetName, sourceWorksheetPath) in sourceSheets)
        {
            if (!targetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            var sourceWorksheetXml = context?.GetSourceWorksheetXml(sourceArchive, sourceWorksheetPath);
            if (sourceWorksheetXml is null)
            {
                var sourceWorksheetEntry = sourceArchive.GetEntry(sourceWorksheetPath);
                if (sourceWorksheetEntry is null)
                    continue;

                sourceWorksheetXml = XlsxPackageXmlEditor.LoadXml(sourceWorksheetEntry);
            }

            if (targetWorksheetEntry is null)
                continue;

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
            Dictionary<string, XElement>? targetCellsByAddress = null;
            IReadOnlyDictionary<string, XElement> GetTargetCellsByAddress() =>
                targetCellsByAddress ??= BuildCellLookup(targetRoot.Element(workbookNs + "sheetData"), workbookNs);

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
            if (MergeWorksheetCellNativeMetadata(sourceSheetData, GetTargetCellsByAddress, targetArchive, workbookNs))
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
}

