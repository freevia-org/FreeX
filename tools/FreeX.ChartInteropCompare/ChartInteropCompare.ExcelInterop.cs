using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FreeX.Core.IO;

internal static partial class ChartInteropCompare
{
    private const int XlOpenXmlWorkbook = 51;
    private const double ExcelPointsPerPixel = 72.0 / 96.0;
    private const string ExcelProcessName = "EXCEL";

    private static object CreateExcelApplication()
    {
        var excelType = Type.GetTypeFromProgID("Excel.Application")
            ?? throw new InvalidOperationException("Excel.Application COM registration was not found.");
        Exception? lastException = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var excel = Activator.CreateInstance(excelType)
                    ?? throw new InvalidOperationException("Excel.Application COM activation returned null.");
                dynamic app = excel;
                app.Visible = false;
                app.DisplayAlerts = false;
                TrySetExcelProperty(app, "EnableEvents", false);
                TrySetExcelProperty(app, "AutomationSecurity", 3);
                return excel;
            }
            catch (Exception ex) when (attempt < 3)
            {
                lastException = ex;
                Thread.Sleep(2000);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        throw new InvalidOperationException($"Excel.Application COM activation failed after retries: {lastException?.Message}", lastException);
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
            dynamic worksheet = workbook.Worksheets.Item(sheetIndex);
            dynamic chartObjects = worksheet.ChartObjects();
            var chartObjectCount = (int)chartObjects.Count;
            chartCount += chartObjectCount;
            if (chartObjectCount > 0)
            {
                dynamic chart = chartObjects.Item(1).Chart;
                string? exportError;
                return new ExcelChartExport(chartCount, TryExportChart(chart, pngPath, out exportError));
            }

            dynamic shapes = worksheet.Shapes;
            var shapeCount = (int)shapes.Count;
            for (var shapeIndex = 1; shapeIndex <= shapeCount; shapeIndex++)
            {
                dynamic shape = shapes.Item(shapeIndex);
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
                string? exportError;
                return new ExcelChartExport(chartCount, TryExportChart(shape.Chart, pngPath, out exportError));
            }
        }

        var chartSheetCount = (int)workbook.Charts.Count;
        chartCount += chartSheetCount;
        if (chartSheetCount > 0)
        {
            dynamic chart = workbook.Charts.Item(1);
            string? exportError;
            return new ExcelChartExport(chartCount, TryExportChart(chart, pngPath, out exportError));
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

    private static void TrySetExcelProperty(dynamic excel, string propertyName, object value)
    {
        try
        {
            var property = excel.GetType().InvokeMember(
                propertyName,
                System.Reflection.BindingFlags.SetProperty,
                null,
                excel,
                new[] { value },
                CultureInfo.InvariantCulture);
            _ = property;
        }
        catch (Exception)
        {
            // Some Excel builds block optional automation flags; the comparison can continue.
        }
    }

    private static void TryCloseWorkbook(object? workbook)
    {
        if (workbook is null)
            return;

        try
        {
            ((dynamic)workbook).Close(false);
        }
        catch (Exception)
        {
            // Best effort during error cleanup.
        }
    }

    private static HashSet<int> GetExcelProcessIds() =>
        Process.GetProcessesByName(ExcelProcessName).Select(process => process.Id).ToHashSet();

    private static void WaitForExcelProcessesToExit(HashSet<int> excelPids, int timeoutMilliseconds)
    {
        if (excelPids.Count == 0)
            return;

        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            var running = Process.GetProcessesByName(ExcelProcessName)
                .Any(process => excelPids.Contains(process.Id));
            if (!running)
                return;

            Thread.Sleep(250);
        }
    }

    private static void KillExcelProcesses(HashSet<int> excelPids)
    {
        if (excelPids.Count == 0)
            return;

        foreach (var process in Process.GetProcessesByName(ExcelProcessName))
        {
            if (!excelPids.Contains(process.Id))
                continue;

            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
                Console.WriteLine($"Killed owned EXCEL PID {process.Id}.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to kill owned EXCEL PID {process.Id}: {ex.Message}");
            }
        }
    }

    private static void ReleaseComObject(object value)
    {
        try
        {
            if (Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }
        catch (Exception)
        {
            // Best effort only; orphan cleanup handles leaked Excel processes.
        }
    }
}
