using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private static void ApplyPackagePostProcessing(
        Workbook workbook,
        Stream packageStream,
        string? currentModelFingerprint = null)
    {
        var featurePlan = XlsxPostProcessingFeaturePlan.Create(workbook);
        XlsxWorkbookWorksheetPathMap? worksheetPathMap = null;
        XlsxWorkbookWorksheetPathMap? GetWorksheetPathMap()
        {
            if (worksheetPathMap is not null)
                return worksheetPathMap;

            packageStream.Position = 0;
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
            worksheetPathMap = XlsxWorkbookWorksheetPathMap.TryCreate(archive);
            return worksheetPathMap;
        }

        if (featurePlan.HasWorkbookPostProcessingMetadata)
        {
            packageStream.Position = 0;
            XlsxWorkbookMetadataWriter.SavePostProcessingMetadata(packageStream, workbook);
        }

        if (featurePlan.HasNonDefaultDimensions)
        {
            packageStream.Position = 0;
            XlsxWorksheetDimensionDefaultsWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        // ClosedXML's Column.Width setter inflates the stored width (e.g. 2.0 -> 2.71) on save; rewrite
        // each worksheet's <cols> with the modelled exact widths so column widths round-trip.
        if (featurePlan.HasColumnWidths)
        {
            packageStream.Position = 0;
            XlsxWorksheetColumnWidthWriter.Save(packageStream, workbook);
        }

        if (featurePlan.HasStyleOnlyCells)
        {
            packageStream.Position = 0;
            XlsxStyleOnlyCellWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (featurePlan.HasFullCalculationOnLoad)
        {
            packageStream.Position = 0;
            XlsxWorksheetCalculationPropertyMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasModeledPrinterAttributes)
        {
            packageStream.Position = 0;
            XlsxWorksheetPageSetupMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (featurePlan.HasPhoneticProperties)
        {
            packageStream.Position = 0;
            XlsxWorksheetPhoneticPropertyMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasAllowEditRanges)
        {
            packageStream.Position = 0;
            XlsxAllowEditRangeMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasAdvancedConditionalFormats)
        {
            packageStream.Position = 0;
            XlsxAdvancedConditionalFormatWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (featurePlan.HasSparklines)
        {
            packageStream.Position = 0;
            XlsxSparklineMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasThreadedComments)
        {
            packageStream.Position = 0;
            XlsxWorksheetThreadedCommentMapper.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (featurePlan.HasBackgroundImages)
        {
            packageStream.Position = 0;
            XlsxWorksheetBackgroundReaderWriter.Save(packageStream, workbook);
        }

        if (featurePlan.HasHeaderFooterPictures)
        {
            IReadOnlySet<string>? sheetsToPreserve = null;
            if (SourcePackages.TryGetValue(workbook, out var headerFooterSourcePackage))
            {
                using var sourceStream = headerFooterSourcePackage.OpenRead();
                sheetsToPreserve = XlsxHeaderFooterPictureReaderWriter.FindSheetsWithUnchangedSourcePictures(
                    sourceStream,
                    workbook);
            }

            packageStream.Position = 0;
            XlsxHeaderFooterPictureReaderWriter.Save(packageStream, workbook, sheetsToPreserve);
        }

        if (featurePlan.HasPersistableViewState)
        {
            packageStream.Position = 0;
            XlsxWorksheetViewWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (featurePlan.HasCodeNames)
        {
            packageStream.Position = 0;
            XlsxWorksheetCodeNameWriter.Save(packageStream, workbook);
        }

        if (featurePlan.HasIgnoredFormulaErrors)
        {
            packageStream.Position = 0;
            XlsxWorksheetDiagnosticsMapper.SaveIgnoredErrors(packageStream, workbook, GetWorksheetPathMap());
        }

        if (workbook.WatchedCells.Count > 0)
        {
            packageStream.Position = 0;
            XlsxWorksheetDiagnosticsMapper.SaveCellWatches(packageStream, workbook);
        }

        if (workbook.Scenarios.Count > 0)
        {
            packageStream.Position = 0;
            XlsxWorksheetScenarioMapper.Save(packageStream, workbook);
        }

        if (workbook.CustomViews.Count > 0)
        {
            packageStream.Position = 0;
            XlsxCustomViewMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasCustomProperties)
        {
            packageStream.Position = 0;
            XlsxWorksheetCustomPropertyMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasWorksheetElementMetadata)
        {
            packageStream.Position = 0;
            XlsxWorksheetPostProcessingMetadataBatchWriter.SaveWorksheetElementMetadata(
                packageStream,
                workbook,
                GetWorksheetPathMap());
        }

        packageStream.Position = 0;
        XlsxWorkbookThemeWriter.Save(packageStream, workbook.Theme);

        if (workbook.IndexedColors.Colors.Count > 0)
        {
            packageStream.Position = 0;
            XlsxIndexedColorPaletteMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasSupportedCharts)
        {
            packageStream.Position = 0;
            XlsxWorksheetChartWriter.Save(
                packageStream,
                workbook,
                XlsxChartXmlWriter.IsSupportedXlsxChart,
                XlsxChartXmlWriter.ToChartXml,
                XlsxChartXmlWriter.GetContentType,
                XlsxChartXmlWriter.GetRelationshipType,
                GetSourceDrawingPathsBySheet(workbook));
        }

        if (featurePlan.HasSupportedDrawingObjects)
        {
            packageStream.Position = 0;
            XlsxWorksheetDrawingObjectWriter.Save(packageStream, workbook);
        }

        if (featurePlan.HasStructuredTables)
        {
            packageStream.Position = 0;
            XlsxStructuredTableWriter.Save(packageStream, workbook);
        }

        if (workbook.PivotTableStyles.Count > 0)
        {
            packageStream.Position = 0;
            XlsxSlicerTimelineWriter.SavePivotTableStyles(packageStream, workbook);
        }

        if (workbook.StructuredTableStyles.Count > 0)
        {
            packageStream.Position = 0;
            XlsxStructuredTableStyleMetadataWriter.Save(packageStream, workbook);
        }

        IReadOnlyDictionary<int, int> numberFormatIdMap = new Dictionary<int, int>();
        if (workbook.NumberFormatCatalog.Count > 0 ||
            featurePlan.HasPivotCustomNumberFormats)
        {
            packageStream.Position = 0;
            numberFormatIdMap = XlsxNumberFormatCatalogWriter.Save(packageStream, workbook);
        }

        var hasSourcePackage = SourcePackages.TryGetValue(workbook, out var sourcePackage);
        if (!hasSourcePackage &&
            workbook.PivotCaches.Count > 0 &&
            featurePlan.HasPivotTables)
        {
            packageStream.Position = 0;
            XlsxPivotTableWriter.Save(packageStream, workbook, numberFormatIdMap);
        }

        if (!hasSourcePackage &&
            (workbook.Slicers.Count > 0 || workbook.Timelines.Count > 0))
        {
            packageStream.Position = 0;
            XlsxSlicerTimelineWriter.SaveSlicerTimelines(packageStream, workbook);
        }

        if (!hasSourcePackage)
        {
            SaveSourcePackageIndependentPostProcessingMetadata();
            NormalizeStylesheetForSchema();
            NormalizeWorkbookForSchema();
            return;
        }

        packageStream.Position = 0;
        var sourceParts = PreserveSourcePackageParts(workbook, packageStream);

        if (sourceParts.HasDrawings)
        {
            packageStream.Position = 0;
            XlsxHeaderFooterPictureReaderWriter.RemoveClearedPictures(packageStream, workbook);
        }

        if (workbook.IndexedColors.Colors.Count > 0)
        {
            packageStream.Position = 0;
            XlsxIndexedColorPaletteMapper.Save(packageStream, workbook);
        }

        if (featurePlan.HasWorkbookReplayMetadata)
        {
            packageStream.Position = 0;
            XlsxWorkbookMetadataWriter.SaveSourcePackageReplayMetadata(packageStream, workbook);
        }

        SaveSourcePackageIndependentPostProcessingMetadata();

        if (featurePlan.HasReplayMetadata)
        {
            packageStream.Position = 0;
            XlsxWorksheetPostProcessingMetadataBatchWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (numberFormatIdMap.Any(pair => pair.Key != pair.Value))
        {
            packageStream.Position = 0;
            XlsxNumberFormatCatalogWriter.RemapPivotTableNumberFormats(packageStream, numberFormatIdMap);
        }

        NormalizeStylesheetForSchema();
        NormalizeSourcePackageForExcelCompatibility();
        NormalizeWorkbookForSchema();

        packageStream.Position = 0;
        SourcePackages.Remove(workbook);
        SourcePackages.Add(workbook, XlsxSourcePackage.Capture(
            packageStream,
            workbook,
            currentModelFingerprint,
            sourcePackage?.WorksheetsWithPreservableSourceMetadata,
            sourcePackage?.HasUnsupportedConditionalFormatting));

        void SaveSourcePackageIndependentPostProcessingMetadata()
        {
            if (featurePlan.HasSourceIndependentMetadata)
            {
                packageStream.Position = 0;
                XlsxWorksheetSourceIndependentMetadataBatchWriter.Save(packageStream, workbook, GetWorksheetPathMap());
            }
        }

        void NormalizeStylesheetForSchema()
        {
            packageStream.Position = 0;
            XlsxStylesheetSchemaNormalizer.Normalize(packageStream);
        }

        void NormalizeSourcePackageForExcelCompatibility()
        {
            packageStream.Position = 0;
            XlsxExcelCompatibilityNormalizer.NormalizeSourcePackageSave(
                packageStream,
                CreateExcelCompatibilityNormalizationPlan(sourcePackage, sourceParts, featurePlan));
        }

        void NormalizeWorkbookForSchema()
        {
            packageStream.Position = 0;
            XlsxWorkbookSchemaNormalizer.Normalize(packageStream);
        }
    }

    private static XlsxExcelCompatibilityNormalizationPlan CreateExcelCompatibilityNormalizationPlan(
        XlsxSourcePackage? sourcePackage,
        SourcePackagePartSummary sourceParts,
        XlsxPostProcessingFeaturePlan featurePlan)
    {
        var shouldScanSourceWorksheetMetadata =
            sourcePackage?.WorksheetsWithPreservableSourceMetadata is null ||
            sourcePackage.WorksheetsWithPreservableSourceMetadata.Count > 0;

        return new XlsxExcelCompatibilityNormalizationPlan(
            ScanWorksheetCustomViews: shouldScanSourceWorksheetMetadata || featurePlan.HasCustomViews,
            ScanWorksheetFormulaText: featurePlan.HasCellFormulas,
            ScanWorksheetDrawingTargets:
                sourceParts.HasDrawings ||
                featurePlan.HasSupportedCharts ||
                featurePlan.HasSupportedDrawingObjects);
    }

    // Maps each source-package sheet to the drawing part it owns. The rebuilt chart writer reuses a chart
    // sheet's own drawing part (so its charts stay on that sheet) and avoids every other sheet's drawing,
    // which the source preservation restores at its original path. Without this, a chart sheet's rebuilt
    // drawing could claim the part name another sheet's source drawing owns and steal its charts.
    private static IReadOnlyDictionary<string, string> GetSourceDrawingPathsBySheet(Workbook workbook)
    {
        if (!SourcePackages.TryGetValue(workbook, out var sourcePackage))
            return EmptyDrawingPathsBySheet;

        try
        {
            using var sourceStream = sourcePackage.OpenRead();
            using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read);

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var workbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return EmptyDrawingPathsBySheet;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var workbookRels = XlsxRelationshipReader.LoadTargets(
                sourceArchive, "xl/_rels/workbook.xml.rels", "xl/workbook.xml", packageRelNs);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (sheetName, worksheetPath) in
                     XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs))
            {
                if (result.ContainsKey(sheetName))
                    continue;

                var worksheetEntry = sourceArchive.GetEntry(worksheetPath);
                if (worksheetEntry is null)
                    continue;

                var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
                var drawingRelId = worksheetXml.Root?
                    .Element(workbookNs + "drawing")?
                    .Attribute(relNs + "id")?
                    .Value;
                if (string.IsNullOrWhiteSpace(drawingRelId))
                    continue;

                var worksheetRels = XlsxRelationshipReader.LoadTargets(
                    sourceArchive, XlsxPackagePath.GetRelationshipPartPath(worksheetPath), worksheetPath, packageRelNs);
                if (worksheetRels.TryGetValue(drawingRelId, out var drawingPath))
                    result[sheetName] = XlsxPackagePath.NormalizeZipPath(drawingPath);
            }

            return result;
        }
        catch
        {
            return EmptyDrawingPathsBySheet;
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyDrawingPathsBySheet =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static bool HasIgnoredFormulaErrors(Sheet sheet)
    {
        foreach (var pair in sheet.GetOccupiedCellMap())
        {
            if (pair.Value.IgnoreFormulaError)
                return true;
        }

        return false;
    }

    private static bool HasPivotCustomNumberFormats(Sheet sheet)
    {
        foreach (var pivot in sheet.PivotTables)
        {
            foreach (var field in pivot.DataFields)
            {
                if (field.NumberFormatId is >= 164 &&
                    !string.IsNullOrWhiteSpace(field.NumberFormatCode))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasSupportedXlsxCharts(Sheet sheet)
    {
        foreach (var chart in sheet.Charts)
        {
            if (XlsxChartXmlWriter.IsSupportedXlsxChart(chart))
                return true;
        }

        return false;
    }

    private struct XlsxPostProcessingFeaturePlan
    {
        public bool HasNonDefaultDimensions;
        public bool HasColumnWidths;
        public bool HasFullCalculationOnLoad;
        public bool HasModeledPrinterAttributes;
        public bool HasPhoneticProperties;
        public bool HasAllowEditRanges;
        public bool HasAdvancedConditionalFormats;
        public bool HasSparklines;
        public bool HasThreadedComments;
        public bool HasBackgroundImages;
        public bool HasHeaderFooterPictures;
        public bool HasPersistableViewState;
        public bool HasCodeNames;
        public bool HasIgnoredFormulaErrors;
        public bool HasCustomProperties;
        public bool HasWorksheetElementMetadata;
        public bool HasSupportedCharts;
        public bool HasSupportedDrawingObjects;
        public bool HasStructuredTables;
        public bool HasPivotTables;
        public bool HasPivotCustomNumberFormats;
        public bool HasWorkbookPostProcessingMetadata;
        public bool HasWorkbookReplayMetadata;
        public bool HasReplayMetadata;
        public bool HasSourceIndependentMetadata;
        public bool HasStyleOnlyCells;
        public bool HasCustomViews;
        public bool HasCellFormulas;

        public static XlsxPostProcessingFeaturePlan Create(Workbook workbook)
        {
            var plan = new XlsxPostProcessingFeaturePlan();
            plan.HasWorkbookPostProcessingMetadata = XlsxWorkbookMetadataWriter.HasPostProcessingMetadata(workbook);
            plan.HasWorkbookReplayMetadata = XlsxWorkbookMetadataWriter.HasSourcePackageReplayMetadata(workbook);
            plan.HasCustomViews = workbook.CustomViews.Count > 0;
            foreach (var sheet in workbook.Sheets)
                plan.Include(sheet);

            return plan;
        }

        private void Include(Sheet sheet)
        {
            HasNonDefaultDimensions |= XlsxWorksheetDimensionDefaultsWriter.HasNonDefaultDimensions(sheet);
            HasColumnWidths |= sheet.ColumnWidths.Count > 0;
            HasFullCalculationOnLoad |= sheet.FullCalculationOnLoad;
            HasModeledPrinterAttributes |= XlsxWorksheetPageSetupMetadataWriter.HasModeledPrinterAttributes(sheet);
            HasPhoneticProperties |= sheet.PhoneticProperties is not null;
            HasAllowEditRanges |= sheet.AllowEditRanges.Count > 0;
            HasAdvancedConditionalFormats |= XlsxAdvancedConditionalFormatWriter.HasAdvancedConditionalFormats(sheet);
            HasSparklines |= sheet.Sparklines.Count > 0;
            HasThreadedComments |= XlsxWorksheetThreadedCommentMapper.HasThreadedComments(sheet);
            HasBackgroundImages |= sheet.BackgroundImage is not null;
            HasHeaderFooterPictures |= XlsxHeaderFooterPictureReaderWriter.HasPictures(sheet);
            HasPersistableViewState |= XlsxWorksheetViewWriter.HasPersistableViewState(sheet);
            HasCodeNames |= !string.IsNullOrWhiteSpace(sheet.CodeName);
            IncludeCellFeatures(sheet);
            HasCustomProperties |= sheet.CustomProperties.Count > 0;
            HasWorksheetElementMetadata |= XlsxWorksheetPostProcessingMetadataBatchWriter.HasWorksheetElementMetadata(sheet);
            HasSupportedCharts |= HasSupportedXlsxCharts(sheet);
            HasSupportedDrawingObjects |= XlsxWorksheetDrawingObjectWriter.HasSupportedObjects(sheet);
            HasStructuredTables |= sheet.StructuredTables.Count > 0;
            HasPivotTables |= sheet.PivotTables.Count > 0;
            HasPivotCustomNumberFormats |= HasPivotCustomNumberFormats(sheet);
            HasReplayMetadata |= XlsxWorksheetPostProcessingMetadataBatchWriter.HasReplayMetadata(sheet);
            HasSourceIndependentMetadata |= XlsxWorksheetSourceIndependentMetadataBatchWriter.HasMetadata(sheet);
            HasStyleOnlyCells |= sheet.HasStyleOnlyCells;
        }

        private void IncludeCellFeatures(Sheet sheet)
        {
            foreach (var pair in sheet.GetOccupiedCellMap())
            {
                HasIgnoredFormulaErrors |= pair.Value.IgnoreFormulaError;
                HasCellFormulas |= pair.Value.HasFormula;
                if (HasIgnoredFormulaErrors && HasCellFormulas)
                    return;
            }
        }
    }
}
