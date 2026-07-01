using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using FreeX.Core.IO;
using FreeX.ToolsShared.Wpf;
using static FreeX.ToolsShared.Wpf.ExcelComAutomation;

internal static partial class ChartInteropCompare
{
    private const int XlOpenXmlWorkbook = 51;
    private const double ExcelPointsPerPixel = 72.0 / 96.0;

    private static object CreateExcelApplication()
    {
        return ExcelComAutomation.CreateExcelApplicationWithRetry(
            "Excel.Application COM registration was not found.",
            "Excel.Application COM activation returned null.",
            maxAttempts: 3,
            retryDelayMilliseconds: 2000,
            failureMessagePrefix: "Excel.Application COM activation failed after retries",
            configure: app =>
            {
                app.Visible = false;
                app.DisplayAlerts = false;
                TrySetProperty(app, "EnableEvents", false);
                TrySetProperty(app, "AutomationSecurity", 3);
            });
    }

    private static void ExportFreeXWorkbookThroughExcel(
        dynamic excel,
        ChartCase chartCase,
        ComparisonDirectories directories,
        ChartCompareResult result)
    {
        if (!result.FreeXXlsxSaved || string.IsNullOrWhiteSpace(result.FreeXXlsxPath))
            return;

        try
        {
            var pngPath = Path.Combine(directories.FreeXExcelPng, $"{chartCase.Name}.png");
            var export = OpenWorkbookAndExportFirstChart(excel, result.FreeXXlsxPath, pngPath);
            result.FreeXXlsxOpenedInExcel = true;
            result.FreeXExcelChartCount = export.ChartCount;
            result.FreeXExcelPngPath = export.ChartExported ? pngPath : null;
            result.FreeXExcelPng = export.ChartExported;
            if (!export.ChartExported)
                result.AddNote("Excel opened FreeX XLSX but did not expose an exportable chart.");
        }
        catch (Exception ex)
        {
            result.Error = AppendError(result.Error, $"FreeX XLSX Excel open/export failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void GenerateExcelNativeFixture(
        dynamic excel,
        ChartCase chartCase,
        ComparisonDirectories directories,
        ChartCompareResult result)
    {
        object? workbookObject = null;
        try
        {
            dynamic workbook = excel.Workbooks.Add();
            workbookObject = workbook;
            dynamic worksheet = workbook.Worksheets.Item(1);
            worksheet.Name = "Data";

            var rangeAddress = PopulateExcelWorksheet(worksheet, chartCase);
            dynamic sourceRange = worksheet.Range[rangeAddress];
            dynamic chart;
            if (chartCase.Kind == ChartFixtureKind.Stock)
            {
                dynamic chartObject = worksheet.ChartObjects().Add(
                    ToExcelPoints(320),
                    ToExcelPoints(40),
                    ToExcelPoints(560),
                    ToExcelPoints(360));
                chart = chartObject.Chart;
                chart.SetSourceData(sourceRange, 2);
                chart.ChartType = chartCase.ExcelChartType;
            }
            else
            {
                dynamic shape = worksheet.Shapes.AddChart2(
                    201,
                    chartCase.ExcelChartType,
                    ToExcelPoints(320),
                    ToExcelPoints(40),
                    ToExcelPoints(560),
                    ToExcelPoints(360));
                chart = shape.Chart;
                chart.SetSourceData(sourceRange);
            }

            chart.HasTitle = true;
            chart.ChartTitle.Text = $"{chartCase.Name} Excel Native";
            chart.HasLegend = chartCase.ShowLegend;

            var workbookPath = Path.Combine(directories.ExcelXlsx, $"{chartCase.Name}.xlsx");
            if (File.Exists(workbookPath))
                File.Delete(workbookPath);
            workbook.SaveAs(workbookPath, XlOpenXmlWorkbook);
            result.ExcelNativeXlsxPath = workbookPath;
            result.ExcelNativeCreated = true;

            var pngPath = Path.Combine(directories.ExcelNativePng, $"{chartCase.Name}.png");
            string? exportError;
            result.ExcelNativePng = TryExportChart(chart, pngPath, out exportError);
            result.ExcelNativePngPath = result.ExcelNativePng ? pngPath : null;
            if (!string.IsNullOrWhiteSpace(exportError))
                result.AddNote($"Excel-native chart PNG export: {exportError}");

            workbook.Close(false);
        }
        catch (Exception ex)
        {
            result.Error = AppendError(result.Error, $"Excel-native fixture failed: {ex.GetType().Name}: {ex.Message}");
            TryCloseWorkbook(workbookObject);
        }
    }

    private static double ToExcelPoints(double pixels) => pixels * ExcelPointsPerPixel;

    private static void RoundTripExcelWorkbookThroughFreeX(
        dynamic excel,
        ChartCase chartCase,
        ComparisonDirectories directories,
        ChartCompareResult result)
    {
        if (!result.ExcelNativeCreated || string.IsNullOrWhiteSpace(result.ExcelNativeXlsxPath))
            return;

        try
        {
            using (var input = File.OpenRead(result.ExcelNativeXlsxPath))
            using (var output = File.Create(Path.Combine(directories.ExcelRoundTripXlsx, $"{chartCase.Name}.xlsx")))
            {
                var workbook = new XlsxFileAdapter().Load(input);
                result.ExcelLoadedByFreeX = true;
                new XlsxFileAdapter().Save(workbook, output);
                result.ExcelRoundTripSavedByFreeX = true;
                result.ExcelRoundTripXlsxPath = output.Name;
            }

            var pngPath = Path.Combine(directories.ExcelRoundTripPng, $"{chartCase.Name}.png");
            var export = OpenWorkbookAndExportFirstChart(excel, result.ExcelRoundTripXlsxPath!, pngPath);
            result.ExcelRoundTripOpenedInExcel = true;
            result.ExcelRoundTripChartCount = export.ChartCount;
            result.ExcelRoundTripPng = export.ChartExported;
            result.ExcelRoundTripPngPath = export.ChartExported ? pngPath : null;
            if (!export.ChartExported)
                result.AddNote("Excel opened the FreeX round-trip but did not expose an exportable chart.");
        }
        catch (Exception ex)
        {
            result.Error = AppendError(result.Error, $"Excel->FreeX->Excel round-trip failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static ExcelChartExport OpenWorkbookAndExportFirstChart(dynamic excel, string workbookPath, string pngPath)
    {
        object? workbookObject = null;
        try
        {
            dynamic workbook = excel.Workbooks.Open(workbookPath);
            workbookObject = workbook;
            var export = ExportFirstChart(workbook, pngPath);
            workbook.Close(false);
            return export;
        }
        catch
        {
            TryCloseWorkbook(workbookObject);
            throw;
        }
    }

    private static ExcelChartExport ExportFirstChart(dynamic workbook, string pngPath)
    {
        var chartCount = 0;
        var worksheetCount = (int)workbook.Worksheets.Count;
        for (var sheetIndex = 1; sheetIndex <= worksheetCount; sheetIndex++)
        {
            object? worksheetRcw = null;
            object? chartObjectsRcw = null;
            object? shapesRcw = null;
            try
            {
                dynamic worksheet = workbook.Worksheets.Item(sheetIndex);
                worksheetRcw = worksheet;

                dynamic chartObjects = worksheet.ChartObjects();
                chartObjectsRcw = chartObjects;
                var chartObjectCount = (int)chartObjects.Count;
                chartCount += chartObjectCount;
                if (chartObjectCount > 0)
                {
                    // chartObjects.Item(1).Chart: release the intermediate chartObject RCW after export.
                    object? chartObjectItemRcw = null;
                    object? chartRcw = null;
                    try
                    {
                        dynamic chartObjectItem = chartObjects.Item(1);
                        chartObjectItemRcw = chartObjectItem;
                        dynamic chart = chartObjectItem.Chart;
                        chartRcw = chart;
                        string? exportError;
                        return new ExcelChartExport(chartCount, TryExportChart(chart, pngPath, out exportError));
                    }
                    finally
                    {
                        ReleaseComObject(chartRcw);
                        ReleaseComObject(chartObjectItemRcw);
                    }
                }

                dynamic shapes = worksheet.Shapes;
                shapesRcw = shapes;
                var shapeCount = (int)shapes.Count;
                for (var shapeIndex = 1; shapeIndex <= shapeCount; shapeIndex++)
                {
                    object? shapeRcw = null;
                    object? shapeChartRcw = null;
                    try
                    {
                        dynamic shape = shapes.Item(shapeIndex);
                        shapeRcw = shape;
                        var hasChart = false;
                        try
                        {
                            hasChart = Convert.ToBoolean(shape.HasChart, CultureInfo.InvariantCulture);
                        }
                        catch (COMException)
                        {
                            hasChart = false;
                        }

                        if (!hasChart)
                            continue;

                        chartCount++;
                        dynamic shapeChart = shape.Chart;
                        shapeChartRcw = shapeChart;
                        string? exportError;
                        return new ExcelChartExport(chartCount, TryExportChart(shapeChart, pngPath, out exportError));
                    }
                    finally
                    {
                        ReleaseComObject(shapeChartRcw);
                        ReleaseComObject(shapeRcw);
                    }
                }
            }
            finally
            {
                ReleaseComObject(shapesRcw);
                ReleaseComObject(chartObjectsRcw);
                ReleaseComObject(worksheetRcw);
            }
        }

        var chartSheetCount = (int)workbook.Charts.Count;
        chartCount += chartSheetCount;
        if (chartSheetCount > 0)
        {
            object? chartSheetRcw = null;
            try
            {
                dynamic chart = workbook.Charts.Item(1);
                chartSheetRcw = chart;
                string? exportError;
                return new ExcelChartExport(chartCount, TryExportChart(chart, pngPath, out exportError));
            }
            finally
            {
                ReleaseComObject(chartSheetRcw);
            }
        }

        return new ExcelChartExport(chartCount, false);
    }

    private static bool TryExportChart(dynamic chart, string pngPath, out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(pngPath))
                File.Delete(pngPath);
            var exported = Convert.ToBoolean(chart.Export(pngPath, "PNG"), CultureInfo.InvariantCulture);
            if (!exported || !File.Exists(pngPath))
            {
                error = "Excel returned false or did not create a PNG.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

}
