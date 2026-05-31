using System.IO;
using System.IO.Compression;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private static void ApplyPackagePostProcessing(Workbook workbook, Stream packageStream)
    {
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

        packageStream.Position = 0;
        XlsxWorkbookMetadataWriter.SavePostProcessingMetadata(packageStream, workbook);

        if (workbook.Sheets.Any(XlsxWorksheetDimensionDefaultsWriter.HasNonDefaultDimensions))
        {
            packageStream.Position = 0;
            XlsxWorksheetDimensionDefaultsWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (workbook.Sheets.Any(sheet => sheet.FullCalculationOnLoad))
        {
            packageStream.Position = 0;
            XlsxWorksheetCalculationPropertyMapper.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(XlsxWorksheetPageSetupMetadataWriter.HasModeledPrinterAttributes))
        {
            packageStream.Position = 0;
            XlsxWorksheetPageSetupMetadataWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (workbook.Sheets.Any(sheet => sheet.PhoneticProperties is not null))
        {
            packageStream.Position = 0;
            XlsxWorksheetPhoneticPropertyMapper.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.AllowEditRanges.Count > 0))
        {
            packageStream.Position = 0;
            XlsxAllowEditRangeMapper.Save(packageStream, workbook);
        }

        if (XlsxAdvancedConditionalFormatWriter.HasAdvancedConditionalFormats(workbook))
        {
            packageStream.Position = 0;
            XlsxAdvancedConditionalFormatWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (workbook.Sheets.Any(sheet => sheet.Sparklines.Count > 0))
        {
            packageStream.Position = 0;
            XlsxSparklineMapper.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.BackgroundImage is not null))
        {
            packageStream.Position = 0;
            XlsxWorksheetBackgroundReaderWriter.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(XlsxHeaderFooterPictureReaderWriter.HasPictures))
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

        if (workbook.Sheets.Any(XlsxWorksheetViewWriter.HasPersistableViewState))
        {
            packageStream.Position = 0;
            XlsxWorksheetViewWriter.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        if (workbook.Sheets.Any(sheet => !string.IsNullOrWhiteSpace(sheet.CodeName)))
        {
            packageStream.Position = 0;
            XlsxWorksheetCodeNameWriter.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.EnumerateCells().Any(pair => pair.Cell.IgnoreFormulaError)))
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

        if (workbook.Sheets.Any(sheet => sheet.CustomProperties.Count > 0))
        {
            packageStream.Position = 0;
            XlsxWorksheetCustomPropertyMapper.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(XlsxWorksheetPostProcessingMetadataBatchWriter.HasWorksheetElementMetadata))
        {
            packageStream.Position = 0;
            XlsxWorksheetPostProcessingMetadataBatchWriter.SaveWorksheetElementMetadata(
                packageStream,
                workbook,
                GetWorksheetPathMap());
        }

        if (workbook.Sheets.Any(sheet => sheet.SingleXmlCells is not null))
        {
            packageStream.Position = 0;
            XlsxWorksheetSingleXmlCellMapper.Save(packageStream, workbook, GetWorksheetPathMap());
        }

        packageStream.Position = 0;
        XlsxWorkbookThemeWriter.Save(packageStream, workbook.Theme);

        if (workbook.IndexedColors.Colors.Count > 0)
        {
            packageStream.Position = 0;
            XlsxIndexedColorPaletteMapper.Save(packageStream, workbook);
        }

        if (XlsxWorksheetChartWriter.HasSupportedCharts(workbook, XlsxChartXmlWriter.IsSupportedXlsxChart))
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

        if (XlsxWorksheetDrawingObjectWriter.HasSupportedObjects(workbook))
        {
            packageStream.Position = 0;
            XlsxWorksheetDrawingObjectWriter.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.StructuredTables.Count > 0))
        {
            packageStream.Position = 0;
            XlsxStructuredTableWriter.Save(packageStream, workbook);
        }

        if (workbook.PivotTableStyles.Count > 0)
        {
            packageStream.Position = 0;
            XlsxSlicerTimelineWriter.SavePivotTableStyles(packageStream, workbook);
        }

        IReadOnlyDictionary<int, int> numberFormatIdMap = new Dictionary<int, int>();
        if (workbook.NumberFormatCatalog.Count > 0 ||
            HasPivotCustomNumberFormats(workbook))
        {
            packageStream.Position = 0;
            numberFormatIdMap = XlsxNumberFormatCatalogWriter.Save(packageStream, workbook);
        }

        var hasSourcePackage = SourcePackages.TryGetValue(workbook, out _);
        if (!hasSourcePackage &&
            workbook.PivotCaches.Count > 0 &&
            workbook.Sheets.Any(sheet => sheet.PivotTables.Count > 0))
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
        PreserveSourcePackageParts(workbook, packageStream);

        packageStream.Position = 0;
        XlsxHeaderFooterPictureReaderWriter.RemoveClearedPictures(packageStream, workbook);

        if (workbook.IndexedColors.Colors.Count > 0)
        {
            packageStream.Position = 0;
            XlsxIndexedColorPaletteMapper.Save(packageStream, workbook);
        }

        packageStream.Position = 0;
        XlsxWorkbookMetadataWriter.SaveSourcePackageReplayMetadata(packageStream, workbook);

        SaveSourcePackageIndependentPostProcessingMetadata();

        if (workbook.Sheets.Any(XlsxWorksheetPostProcessingMetadataBatchWriter.HasReplayMetadata))
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
        SourcePackages.Add(workbook, XlsxSourcePackage.Capture(packageStream, workbook));

        void SaveSourcePackageIndependentPostProcessingMetadata()
        {
            if (workbook.Sheets.Any(XlsxWorksheetSourceIndependentMetadataBatchWriter.HasMetadata))
            {
                packageStream.Position = 0;
                XlsxWorksheetSourceIndependentMetadataBatchWriter.Save(packageStream, workbook, GetWorksheetPathMap());
            }
        }
    }

    private static bool HasPivotCustomNumberFormats(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
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
        }

        return false;
    }
}
