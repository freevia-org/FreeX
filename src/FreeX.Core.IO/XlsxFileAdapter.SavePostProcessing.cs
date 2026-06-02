using System.IO;
using System.IO.Compression;
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
            if (SourcePackages.TryGetValue(workbook, out var sourcePackage))
            {
                using var sourceStream = sourcePackage.OpenRead();
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
            XlsxWorksheetDiagnosticsMapper.SaveIgnoredErrors(packageStream, workbook);
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
                XlsxChartXmlWriter.GetRelationshipType);
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

        var hasSourcePackage = SourcePackages.TryGetValue(workbook, out _);
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

        packageStream.Position = 0;
        SourcePackages.Remove(workbook);
        SourcePackages.Add(workbook, XlsxSourcePackage.Capture(packageStream, workbook, currentModelFingerprint));

        void SaveSourcePackageIndependentPostProcessingMetadata()
        {
            if (featurePlan.HasSourceIndependentMetadata)
            {
                packageStream.Position = 0;
                XlsxWorksheetSourceIndependentMetadataBatchWriter.Save(packageStream, workbook, GetWorksheetPathMap());
            }
        }
    }

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

        public static XlsxPostProcessingFeaturePlan Create(Workbook workbook)
        {
            var plan = new XlsxPostProcessingFeaturePlan();
            plan.HasWorkbookPostProcessingMetadata = XlsxWorkbookMetadataWriter.HasPostProcessingMetadata(workbook);
            plan.HasWorkbookReplayMetadata = XlsxWorkbookMetadataWriter.HasSourcePackageReplayMetadata(workbook);
            foreach (var sheet in workbook.Sheets)
                plan.Include(sheet);

            return plan;
        }

        private void Include(Sheet sheet)
        {
            HasNonDefaultDimensions |= XlsxWorksheetDimensionDefaultsWriter.HasNonDefaultDimensions(sheet);
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
            HasIgnoredFormulaErrors |= HasIgnoredFormulaErrors(sheet);
            HasCustomProperties |= sheet.CustomProperties.Count > 0;
            HasWorksheetElementMetadata |= XlsxWorksheetPostProcessingMetadataBatchWriter.HasWorksheetElementMetadata(sheet);
            HasSupportedCharts |= HasSupportedXlsxCharts(sheet);
            HasSupportedDrawingObjects |= XlsxWorksheetDrawingObjectWriter.HasSupportedObjects(sheet);
            HasStructuredTables |= sheet.StructuredTables.Count > 0;
            HasPivotTables |= sheet.PivotTables.Count > 0;
            HasPivotCustomNumberFormats |= HasPivotCustomNumberFormats(sheet);
            HasReplayMetadata |= XlsxWorksheetPostProcessingMetadataBatchWriter.HasReplayMetadata(sheet);
            HasSourceIndependentMetadata |= XlsxWorksheetSourceIndependentMetadataBatchWriter.HasMetadata(sheet);
        }
    }
}
