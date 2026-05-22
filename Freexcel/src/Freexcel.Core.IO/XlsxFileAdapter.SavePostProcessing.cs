using System.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private static void ApplyPackagePostProcessing(Workbook workbook, MemoryStream packageStream)
    {
        if (workbook.IsStructureProtected)
        {
            packageStream.Position = 0;
            XlsxWorkbookMetadataWriter.SaveProtection(packageStream, workbook);
        }

        packageStream.Position = 0;
        XlsxWorkbookMetadataWriter.SaveCalculationProperties(packageStream, workbook);

        if (workbook.Sheets.Any(sheet => sheet.FullCalculationOnLoad))
        {
            packageStream.Position = 0;
            XlsxWorksheetCalculationPropertyMapper.Save(packageStream, workbook);
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

        if (workbook.Sheets.Any(sheet => sheet.DataValidations.Any(XlsxDataValidationNativeMetadataMapper.HasNativeMetadata)))
        {
            packageStream.Position = 0;
            XlsxDataValidationNativeMetadataMapper.Save(packageStream, workbook);
        }

        if (XlsxAdvancedConditionalFormatWriter.HasAdvancedConditionalFormats(workbook))
        {
            packageStream.Position = 0;
            XlsxAdvancedConditionalFormatWriter.Save(packageStream, workbook);
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

        if (workbook.Sheets.Any(XlsxWorksheetViewWriter.HasPersistableViewState))
        {
            packageStream.Position = 0;
            XlsxWorksheetViewWriter.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => !string.IsNullOrWhiteSpace(sheet.CodeName)))
        {
            packageStream.Position = 0;
            XlsxWorksheetCodeNameWriter.Save(packageStream, workbook);
        }

        if (workbook.Sheets.Any(sheet => sheet.GetUsedCells().Any(pair => pair.Value.IgnoreFormulaError)))
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

        packageStream.Position = 0;
        XlsxWorkbookThemeWriter.Save(packageStream, workbook.Theme);

        if (XlsxWorksheetChartWriter.HasSupportedCharts(workbook, XlsxChartXmlWriter.IsSupportedXlsxChart))
        {
            packageStream.Position = 0;
            XlsxWorksheetChartWriter.Save(packageStream, workbook, XlsxChartXmlWriter.IsSupportedXlsxChart, XlsxChartXmlWriter.ToChartXml);
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
            workbook.Sheets.SelectMany(sheet => sheet.PivotTables)
                .SelectMany(pivot => pivot.DataFields)
                .Any(field => field.NumberFormatId is >= 164 && !string.IsNullOrWhiteSpace(field.NumberFormatCode)))
        {
            packageStream.Position = 0;
            numberFormatIdMap = XlsxNumberFormatCatalogWriter.Save(packageStream, workbook);
        }

        if (!SourcePackages.TryGetValue(workbook, out _) &&
            workbook.PivotCaches.Count > 0 &&
            workbook.Sheets.Any(sheet => sheet.PivotTables.Count > 0))
        {
            packageStream.Position = 0;
            XlsxPivotTableWriter.Save(packageStream, workbook, numberFormatIdMap);
        }

        if (!SourcePackages.TryGetValue(workbook, out _) &&
            (workbook.Slicers.Count > 0 || workbook.Timelines.Count > 0))
        {
            packageStream.Position = 0;
            XlsxSlicerTimelineWriter.SaveSlicerTimelines(packageStream, workbook);
        }

        packageStream.Position = 0;
        PreserveSourcePackageParts(workbook, packageStream);

        if (numberFormatIdMap.Any(pair => pair.Key != pair.Value))
        {
            packageStream.Position = 0;
            XlsxNumberFormatCatalogWriter.RemapPivotTableNumberFormats(packageStream, numberFormatIdMap);
        }
    }
}
