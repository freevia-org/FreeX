using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.UI;
using FreeX.Core.IO;
using FreeX.Core.Model;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => ChartInteropCompare.Run(args);
}

internal static class ChartInteropCompare
{
    private const int XlOpenXmlWorkbook = 51;
    private const string ExcelProcessName = "EXCEL";

    public static int Run(string[] args)
    {
        var options = CompareOptions.Parse(args);
        if (options.ShowHelp)
        {
            WriteUsage();
            return 0;
        }

        var runDirectory = options.OutputDirectory ?? CreateDefaultRunDirectory();
        Directory.CreateDirectory(runDirectory);

        var directories = ComparisonDirectories.Create(runDirectory);
        var cases = CreateCases();
        var results = cases.Select(chartCase => new ChartCompareResult(chartCase.Name, chartCase.Type.ToString(), chartCase.Family)).ToList();

        Console.WriteLine("FreeX / Excel chart interop comparison");
        Console.WriteLine($"Run directory: {runDirectory}");
        Console.WriteLine($"Chart cases: {cases.Count}");

        for (var index = 0; index < cases.Count; index++)
        {
            var chartCase = cases[index];
            var result = results[index];
            Console.WriteLine($"[{index + 1}/{cases.Count}] FreeX fixture: {chartCase.Name}");
            GenerateFreeXFixture(chartCase, directories, result);
        }

        var baselineExcelPids = GetExcelProcessIds();
        object? excel = null;
        try
        {
            var excelType = Type.GetTypeFromProgID("Excel.Application")
                ?? throw new InvalidOperationException("Excel.Application COM registration was not found.");
            excel = Activator.CreateInstance(excelType)
                ?? throw new InvalidOperationException("Excel.Application COM activation returned null.");
            dynamic app = excel;
            app.Visible = false;
            app.DisplayAlerts = false;
            TrySetExcelProperty(app, "EnableEvents", false);
            TrySetExcelProperty(app, "AutomationSecurity", 3);

            for (var index = 0; index < cases.Count; index++)
            {
                var chartCase = cases[index];
                var result = results[index];
                Console.WriteLine($"[{index + 1}/{cases.Count}] Excel interop: {chartCase.Name}");
                ExportFreeXWorkbookThroughExcel(app, chartCase, directories, result);
                GenerateExcelNativeFixture(app, chartCase, directories, result);
                RoundTripExcelWorkbookThroughFreeX(app, chartCase, directories, result);
            }
        }
        catch (Exception ex)
        {
            foreach (var result in results.Where(result => string.IsNullOrWhiteSpace(result.Error)))
                result.Error = $"Excel automation stopped before this case completed: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            if (excel is not null)
            {
                try
                {
                    ((dynamic)excel).Quit();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Excel.Quit failed during cleanup: {ex.Message}");
                }

                ReleaseComObject(excel);
            }

            KillOrphanExcelProcesses(baselineExcelPids);
        }

        WriteResults(runDirectory, results);
        Console.WriteLine($"Results: {Path.Combine(runDirectory, "chart_compare_results.csv")}");
        Console.WriteLine($"Summary: {Path.Combine(runDirectory, "README.md")}");

        var failed = results.Count(result => !result.Passed);
        Console.WriteLine(failed == 0
            ? $"PASS: {results.Count}/{results.Count} chart cases interoperate."
            : $"FAIL: {results.Count - failed}/{results.Count} chart cases interoperate; {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    private static void GenerateFreeXFixture(ChartCase chartCase, ComparisonDirectories directories, ChartCompareResult result)
    {
        try
        {
            var (workbook, sheet, chart, chartDataCells) = CreateFreeXWorkbook(chartCase);
            var xlsxPath = Path.Combine(directories.FreeXXlsx, $"{chartCase.Name}.xlsx");
            using (var stream = File.Create(xlsxPath))
                new XlsxFileAdapter().Save(workbook, stream);

            result.FreeXXlsxPath = xlsxPath;
            result.FreeXXlsxSaved = true;

            var viewport = new ViewportModel([], [], [], null, [], null, chartDataCells);
            var image = ChartRenderer.Render(chart, viewport, WorkbookTheme.Office, renderScale: 1.5);
            if (image is not null)
            {
                var pngPath = Path.Combine(directories.FreeXRendererPng, $"{chartCase.Name}.png");
                SaveImage(image, pngPath);
                result.FreeXRendererPngPath = pngPath;
                result.FreeXRendererPng = true;
            }
            else
            {
                result.AddNote("FreeX renderer returned no image.");
            }

            _ = sheet;
        }
        catch (Exception ex)
        {
            result.Error = $"FreeX fixture failed: {ex.GetType().Name}: {ex.Message}";
        }
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
                dynamic chartObject = worksheet.ChartObjects().Add(320, 40, 560, 360);
                chart = chartObject.Chart;
                chart.SetSourceData(sourceRange, 2);
                chart.ChartType = chartCase.ExcelChartType;
            }
            else
            {
                dynamic shape = worksheet.Shapes.AddChart2(201, chartCase.ExcelChartType, 320, 40, 560, 360);
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

    private static (Workbook Workbook, Sheet Sheet, ChartModel Chart, IReadOnlyList<ChartDataCell> ChartDataCells) CreateFreeXWorkbook(ChartCase chartCase)
    {
        var workbook = new Workbook($"ChartCompare_{chartCase.Name}");
        var sheet = workbook.AddSheet("Data");
        var chartDataCells = new List<ChartDataCell>();

        void Text(uint row, uint col, string value)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(value));
            chartDataCells.Add(new ChartDataCell(sheet.Id, row, col, value, new TextValue(value)));
        }

        void Number(uint row, uint col, double value)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(value));
            chartDataCells.Add(new ChartDataCell(sheet.Id, row, col, value.ToString(CultureInfo.InvariantCulture), new NumberValue(value)));
        }

        var range = chartCase.Kind switch
        {
            ChartFixtureKind.Histogram => PopulateHistogram(Text, Number, sheet.Id),
            ChartFixtureKind.SingleSeries => PopulateSingleSeries(Text, Number, sheet.Id),
            ChartFixtureKind.Scatter => PopulateScatter(Text, Number, sheet.Id),
            ChartFixtureKind.Bubble => PopulateBubble(Text, Number, sheet.Id),
            ChartFixtureKind.Stock => PopulateStock(Text, Number, sheet.Id),
            ChartFixtureKind.Surface => PopulateSurface(Text, Number, sheet.Id),
            ChartFixtureKind.BoxAndWhisker => PopulateBoxAndWhisker(Text, Number, sheet.Id),
            _ => PopulateCategorySeries(Text, Number, sheet.Id)
        };

        var chart = new ChartModel
        {
            Type = chartCase.Type,
            Name = chartCase.Name,
            Title = $"{chartCase.Name} FreeX",
            DataRange = range,
            FirstRowIsHeader = true,
            FirstColIsCategories = chartCase.FirstColIsCategories,
            ShowLegend = chartCase.ShowLegend,
            Left = 320,
            Top = 40,
            Width = 560,
            Height = 360,
            WaterfallTotalPointIndices = chartCase.Type == ChartType.Waterfall ? [0, 4] : [],
            StockSubtype = chartCase.Type == ChartType.Stock ? StockChartSubtype.HighLowClose : StockChartSubtype.HighLowClose
        };

        sheet.Charts.Add(chart);
        return (workbook, sheet, chart, chartDataCells);
    }

    private static GridRange PopulateCategorySeries(Action<uint, uint, string> text, Action<uint, uint, double> number, SheetId sheetId)
    {
        text(1, 1, "Category");
        text(1, 2, "North");
        text(1, 3, "South");
        (string Category, double North, double South)[] rows =
        [
            ("Q1", 18, 12),
            ("Q2", 24, 17),
            ("Q3", 21, 22),
            ("Q4", 30, 25)
        ];
        for (var index = 0; index < rows.Length; index++)
        {
            var row = (uint)index + 2;
            text(row, 1, rows[index].Category);
            number(row, 2, rows[index].North);
            number(row, 3, rows[index].South);
        }

        return new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 3));
    }

    private static GridRange PopulateSingleSeries(Action<uint, uint, string> text, Action<uint, uint, double> number, SheetId sheetId)
    {
        text(1, 1, "Category");
        text(1, 2, "Value");
        (string Category, double Value)[] rows =
        [
            ("Opening", 120),
            ("Sales", 45),
            ("Returns", -18),
            ("Costs", -32),
            ("Closing", 115)
        ];
        for (var index = 0; index < rows.Length; index++)
        {
            var row = (uint)index + 2;
            text(row, 1, rows[index].Category);
            number(row, 2, rows[index].Value);
        }

        return new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 6, 2));
    }

    private static GridRange PopulateHistogram(Action<uint, uint, string> text, Action<uint, uint, double> number, SheetId sheetId)
    {
        text(1, 1, "Value");
        double[] values = [4, 7, 9, 11, 12, 16, 18, 19, 23, 27, 32, 38, 41, 47];
        for (var index = 0; index < values.Length; index++)
            number((uint)index + 2, 1, values[index]);
        return new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, (uint)values.Length + 1, 1));
    }

    private static GridRange PopulateScatter(Action<uint, uint, string> text, Action<uint, uint, double> number, SheetId sheetId)
    {
        text(1, 1, "X");
        text(1, 2, "Y");
        double[] xs = [1, 2, 3, 4, 5, 6];
        double[] ys = [3, 5, 4, 8, 7, 10];
        for (var index = 0; index < xs.Length; index++)
        {
            var row = (uint)index + 2;
            number(row, 1, xs[index]);
            number(row, 2, ys[index]);
        }

        return new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 7, 2));
    }

    private static GridRange PopulateBubble(Action<uint, uint, string> text, Action<uint, uint, double> number, SheetId sheetId)
    {
        text(1, 1, "X");
        text(1, 2, "Y");
        text(1, 3, "Size");
        (double X, double Y, double Size)[] rows = [(1, 4, 7), (2, 5, 10), (3, 7, 16), (4, 6, 12), (5, 9, 18)];
        for (var index = 0; index < rows.Length; index++)
        {
            var row = (uint)index + 2;
            number(row, 1, rows[index].X);
            number(row, 2, rows[index].Y);
            number(row, 3, rows[index].Size);
        }

        return new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 6, 3));
    }

    private static GridRange PopulateStock(Action<uint, uint, string> text, Action<uint, uint, double> number, SheetId sheetId)
    {
        text(1, 1, "Date");
        text(1, 2, "High");
        text(1, 3, "Low");
        text(1, 4, "Close");
        for (var index = 0; index < 5; index++)
        {
            var row = (uint)index + 2;
            number(row, 1, new DateTime(2026, 1, 5).AddDays(index).ToOADate());
            number(row, 2, 110 + index * 3);
            number(row, 3, 96 + index * 2);
            number(row, 4, 102 + index * 2.5);
        }

        return new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 6, 4));
    }

    private static GridRange PopulateSurface(Action<uint, uint, string> text, Action<uint, uint, double> number, SheetId sheetId)
    {
        text(1, 1, "");
        text(1, 2, "Q1");
        text(1, 3, "Q2");
        text(1, 4, "Q3");
        text(1, 5, "Q4");
        string[] regions = ["North", "South", "East", "West"];
        double[,] values =
        {
            { 12, 18, 21, 24 },
            { 10, 14, 19, 22 },
            { 8, 13, 17, 21 },
            { 6, 11, 15, 19 }
        };
        for (var rowIndex = 0; rowIndex < regions.Length; rowIndex++)
        {
            var row = (uint)rowIndex + 2;
            text(row, 1, regions[rowIndex]);
            for (var colIndex = 0; colIndex < 4; colIndex++)
                number(row, (uint)colIndex + 2, values[rowIndex, colIndex]);
        }

        return new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 5));
    }

    private static GridRange PopulateBoxAndWhisker(Action<uint, uint, string> text, Action<uint, uint, double> number, SheetId sheetId)
    {
        text(1, 1, "Sample");
        text(1, 2, "North");
        text(1, 3, "South");
        double[] north = [10, 13, 15, 16, 18, 21, 23, 24];
        double[] south = [8, 11, 14, 16, 19, 20, 25, 29];
        for (var index = 0; index < north.Length; index++)
        {
            var row = (uint)index + 2;
            text(row, 1, (index + 1).ToString(CultureInfo.InvariantCulture));
            number(row, 2, north[index]);
            number(row, 3, south[index]);
        }

        return new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 9, 3));
    }

    private static string PopulateExcelWorksheet(dynamic worksheet, ChartCase chartCase)
    {
        return chartCase.Kind switch
        {
            ChartFixtureKind.Histogram => PopulateExcelHistogram(worksheet),
            ChartFixtureKind.SingleSeries => PopulateExcelSingleSeries(worksheet),
            ChartFixtureKind.Scatter => PopulateExcelScatter(worksheet),
            ChartFixtureKind.Bubble => PopulateExcelBubble(worksheet),
            ChartFixtureKind.Stock => PopulateExcelStock(worksheet),
            ChartFixtureKind.Surface => PopulateExcelSurface(worksheet),
            ChartFixtureKind.BoxAndWhisker => PopulateExcelBoxAndWhisker(worksheet),
            _ => PopulateExcelCategorySeries(worksheet)
        };
    }

    private static string PopulateExcelCategorySeries(dynamic worksheet)
    {
        object[,] values =
        {
            { "Category", "North", "South" },
            { "Q1", 18, 12 },
            { "Q2", 24, 17 },
            { "Q3", 21, 22 },
            { "Q4", 30, 25 }
        };
        worksheet.Range["A1:C5"].Value2 = values;
        return "A1:C5";
    }

    private static string PopulateExcelSingleSeries(dynamic worksheet)
    {
        object[,] values =
        {
            { "Category", "Value" },
            { "Opening", 120 },
            { "Sales", 45 },
            { "Returns", -18 },
            { "Costs", -32 },
            { "Closing", 115 }
        };
        worksheet.Range["A1:B6"].Value2 = values;
        return "A1:B6";
    }

    private static string PopulateExcelHistogram(dynamic worksheet)
    {
        object[,] values =
        {
            { "Value" },
            { 4 },
            { 7 },
            { 9 },
            { 11 },
            { 12 },
            { 16 },
            { 18 },
            { 19 },
            { 23 },
            { 27 },
            { 32 },
            { 38 },
            { 41 },
            { 47 }
        };
        worksheet.Range["A1:A15"].Value2 = values;
        return "A1:A15";
    }

    private static string PopulateExcelScatter(dynamic worksheet)
    {
        object[,] values =
        {
            { "X", "Y" },
            { 1, 3 },
            { 2, 5 },
            { 3, 4 },
            { 4, 8 },
            { 5, 7 },
            { 6, 10 }
        };
        worksheet.Range["A1:B7"].Value2 = values;
        return "A1:B7";
    }

    private static string PopulateExcelBubble(dynamic worksheet)
    {
        object[,] values =
        {
            { "X", "Y", "Size" },
            { 1, 4, 7 },
            { 2, 5, 10 },
            { 3, 7, 16 },
            { 4, 6, 12 },
            { 5, 9, 18 }
        };
        worksheet.Range["A1:C6"].Value2 = values;
        return "A1:C6";
    }

    private static string PopulateExcelStock(dynamic worksheet)
    {
        string[] headers = ["Date", "High", "Low", "Close"];
        for (var col = 0; col < headers.Length; col++)
            worksheet.Cells.Item(1, col + 1).Value2 = headers[col];

        for (var index = 0; index < 5; index++)
        {
            var row = index + 2;
            worksheet.Cells.Item(row, 1).Value2 = new DateTime(2026, 1, 5).AddDays(index).ToOADate();
            worksheet.Cells.Item(row, 2).Value2 = 110 + index * 3;
            worksheet.Cells.Item(row, 3).Value2 = 96 + index * 2;
            worksheet.Cells.Item(row, 4).Value2 = 102 + index * 2.5;
        }

        worksheet.Range["A2:A6"].NumberFormat = "m/d/yyyy";
        return "A1:D6";
    }

    private static string PopulateExcelSurface(dynamic worksheet)
    {
        object[,] values =
        {
            { "", "Q1", "Q2", "Q3", "Q4" },
            { "North", 12, 18, 21, 24 },
            { "South", 10, 14, 19, 22 },
            { "East", 8, 13, 17, 21 },
            { "West", 6, 11, 15, 19 }
        };
        worksheet.Range["A1:E5"].Value2 = values;
        return "A1:E5";
    }

    private static string PopulateExcelBoxAndWhisker(dynamic worksheet)
    {
        object[,] values =
        {
            { "Sample", "North", "South" },
            { "1", 10, 8 },
            { "2", 13, 11 },
            { "3", 15, 14 },
            { "4", 16, 16 },
            { "5", 18, 19 },
            { "6", 21, 20 },
            { "7", 23, 25 },
            { "8", 24, 29 }
        };
        worksheet.Range["A1:C9"].Value2 = values;
        return "A1:C9";
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

    private static void SaveImage(ImageSource image, string path)
    {
        if (image is not BitmapSource bitmap)
            throw new InvalidOperationException($"Unsupported FreeX renderer image type: {image.GetType().FullName}");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void WriteResults(string runDirectory, IReadOnlyList<ChartCompareResult> results)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Chart,Type,Family,FreeXRendererPng,FreeXXlsxOpenedInExcel,FreeXExcelChartCount,FreeXExcelPng,ExcelNativeCreated,ExcelNativePng,ExcelLoadedByFreeX,ExcelRoundTripOpenedInExcel,ExcelRoundTripChartCount,ExcelRoundTripPng,Passed,Notes,Error");
        foreach (var result in results)
        {
            csv.AppendCsvRow(
                result.Chart,
                result.Type,
                result.Family,
                result.FreeXRendererPng,
                result.FreeXXlsxOpenedInExcel,
                result.FreeXExcelChartCount,
                result.FreeXExcelPng,
                result.ExcelNativeCreated,
                result.ExcelNativePng,
                result.ExcelLoadedByFreeX,
                result.ExcelRoundTripOpenedInExcel,
                result.ExcelRoundTripChartCount,
                result.ExcelRoundTripPng,
                result.Passed,
                result.Notes,
                result.Error);
        }

        File.WriteAllText(Path.Combine(runDirectory, "chart_compare_results.csv"), csv.ToString(), Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(runDirectory, "chart_compare_results.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
        File.WriteAllText(Path.Combine(runDirectory, "README.md"), CreateMarkdownSummary(results), Encoding.UTF8);
    }

    private static string CreateMarkdownSummary(IReadOnlyList<ChartCompareResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# FreeX / Excel Chart Interop Comparison");
        builder.AppendLine();
        builder.AppendLine("| Chart | FreeX renderer | FreeX XLSX opens in Excel | Excel native | Excel -> FreeX -> Excel | Notes |");
        builder.AppendLine("|---|---:|---:|---:|---:|---|");
        foreach (var result in results)
        {
            builder.AppendLine($"| {result.Chart} | {Mark(result.FreeXRendererPng)} | {Mark(result.FreeXXlsxOpenedInExcel && result.FreeXExcelPng)} | {Mark(result.ExcelNativeCreated && result.ExcelNativePng)} | {Mark(result.ExcelRoundTripOpenedInExcel && result.ExcelRoundTripPng)} | {EscapeMarkdown(result.SummaryNote)} |");
        }

        builder.AppendLine();
        builder.AppendLine($"Passed: {results.Count(result => result.Passed)}/{results.Count}");
        builder.AppendLine();
        builder.AppendLine("Generated folders:");
        builder.AppendLine("- `freex-xlsx/`: workbooks written by FreeX.");
        builder.AppendLine("- `excel-xlsx/`: matching workbooks authored by Excel COM.");
        builder.AppendLine("- `excel-roundtrip-xlsx/`: Excel-authored workbooks loaded and saved by FreeX.");
        builder.AppendLine("- `png-*`: chart image exports from FreeX renderer or Excel.");
        return builder.ToString();
    }

    private static List<ChartCase> CreateCases() =>
    [
        new("Column", ChartType.Column, 51, ChartFixtureKind.CategorySeries),
        new("StackedColumn", ChartType.StackedColumn, 52, ChartFixtureKind.CategorySeries),
        new("PercentStackedColumn", ChartType.PercentStackedColumn, 53, ChartFixtureKind.CategorySeries),
        new("ThreeDColumn", ChartType.ThreeDColumn, 54, ChartFixtureKind.CategorySeries),
        new("Line", ChartType.Line, 4, ChartFixtureKind.CategorySeries),
        new("ThreeDLine", ChartType.ThreeDLine, -4101, ChartFixtureKind.CategorySeries),
        new("Pie", ChartType.Pie, 5, ChartFixtureKind.SingleSeries, ShowLegend: false),
        new("ThreeDPie", ChartType.ThreeDPie, -4102, ChartFixtureKind.SingleSeries, ShowLegend: false),
        new("Doughnut", ChartType.Doughnut, -4120, ChartFixtureKind.SingleSeries, ShowLegend: false),
        new("Bar", ChartType.Bar, 57, ChartFixtureKind.CategorySeries),
        new("StackedBar", ChartType.StackedBar, 58, ChartFixtureKind.CategorySeries),
        new("PercentStackedBar", ChartType.PercentStackedBar, 59, ChartFixtureKind.CategorySeries),
        new("ThreeDBar", ChartType.ThreeDBar, 60, ChartFixtureKind.CategorySeries),
        new("Scatter", ChartType.Scatter, -4169, ChartFixtureKind.Scatter, FirstColIsCategories: false),
        new("Bubble", ChartType.Bubble, 15, ChartFixtureKind.Bubble, FirstColIsCategories: false),
        new("Area", ChartType.Area, 1, ChartFixtureKind.CategorySeries),
        new("ThreeDArea", ChartType.ThreeDArea, -4098, ChartFixtureKind.CategorySeries),
        new("Radar", ChartType.Radar, -4151, ChartFixtureKind.CategorySeries),
        new("Stock", ChartType.Stock, 88, ChartFixtureKind.Stock),
        new("Surface", ChartType.Surface, 85, ChartFixtureKind.Surface),
        new("ThreeDSurface", ChartType.ThreeDSurface, 83, ChartFixtureKind.Surface),
        new("Treemap", ChartType.Treemap, 117, ChartFixtureKind.SingleSeries, ShowLegend: false),
        new("Sunburst", ChartType.Sunburst, 120, ChartFixtureKind.SingleSeries, ShowLegend: false),
        new("Histogram", ChartType.Histogram, 118, ChartFixtureKind.Histogram, FirstColIsCategories: false, ShowLegend: false),
        new("Pareto", ChartType.Pareto, 122, ChartFixtureKind.SingleSeries, ShowLegend: false),
        new("BoxAndWhisker", ChartType.BoxAndWhisker, 121, ChartFixtureKind.BoxAndWhisker),
        new("Waterfall", ChartType.Waterfall, 119, ChartFixtureKind.SingleSeries, ShowLegend: false),
        new("Funnel", ChartType.Funnel, 123, ChartFixtureKind.SingleSeries, ShowLegend: false)
    ];

    private static string CreateDefaultRunDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
            throw new InvalidOperationException("USERPROFILE could not be resolved.");

        return Path.Combine(
            userProfile,
            "freex-xlsx-verify",
            "chart-interop",
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
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

    private static void KillOrphanExcelProcesses(HashSet<int> baselineExcelPids)
    {
        foreach (var process in Process.GetProcessesByName(ExcelProcessName))
        {
            if (baselineExcelPids.Contains(process.Id))
                continue;

            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
                Console.WriteLine($"Killed orphan EXCEL PID {process.Id}.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to kill orphan EXCEL PID {process.Id}: {ex.Message}");
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

    private static string AppendError(string? existing, string error) =>
        string.IsNullOrWhiteSpace(existing) ? error : $"{existing} | {error}";

    private static string Mark(bool value) => value ? "yes" : "no";

    private static string EscapeMarkdown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Replace("|", "\\|", StringComparison.Ordinal);

    private static void WriteUsage()
    {
        Console.WriteLine(
            """
            Usage:
              dotnet run --project tools/FreeX.ChartInteropCompare -- [options]

            Options:
              --out <directory>  Output directory. Defaults to %USERPROFILE%\freex-xlsx-verify\chart-interop\<timestamp>.
              --help             Show this help text.
            """);
    }
}

internal sealed record ChartCase(
    string Name,
    ChartType Type,
    int ExcelChartType,
    ChartFixtureKind Kind,
    bool FirstColIsCategories = true,
    bool ShowLegend = true)
{
    public string Family => ChartTypeSupport.IsChartExFamily(Type)
        ? "chartEx"
        : "classic";
}

internal enum ChartFixtureKind
{
    CategorySeries,
    SingleSeries,
    Scatter,
    Bubble,
    Stock,
    Surface,
    Histogram,
    BoxAndWhisker
}

internal sealed record ExcelChartExport(int ChartCount, bool ChartExported);

internal sealed class ChartCompareResult(string chart, string type, string family)
{
    private readonly List<string> _notes = [];

    public string Chart { get; } = chart;
    public string Type { get; } = type;
    public string Family { get; } = family;
    public bool FreeXRendererPng { get; set; }
    public string? FreeXRendererPngPath { get; set; }
    public bool FreeXXlsxSaved { get; set; }
    public string? FreeXXlsxPath { get; set; }
    public bool FreeXXlsxOpenedInExcel { get; set; }
    public int FreeXExcelChartCount { get; set; }
    public bool FreeXExcelPng { get; set; }
    public string? FreeXExcelPngPath { get; set; }
    public bool ExcelNativeCreated { get; set; }
    public string? ExcelNativeXlsxPath { get; set; }
    public bool ExcelNativePng { get; set; }
    public string? ExcelNativePngPath { get; set; }
    public bool ExcelLoadedByFreeX { get; set; }
    public bool ExcelRoundTripSavedByFreeX { get; set; }
    public string? ExcelRoundTripXlsxPath { get; set; }
    public bool ExcelRoundTripOpenedInExcel { get; set; }
    public int ExcelRoundTripChartCount { get; set; }
    public bool ExcelRoundTripPng { get; set; }
    public string? ExcelRoundTripPngPath { get; set; }
    public string? Error { get; set; }
    public string Notes => string.Join("; ", _notes);
    public string SummaryNote => string.IsNullOrWhiteSpace(Error) ? Notes : $"{Notes} {Error}".Trim();
    public bool Passed =>
        string.IsNullOrWhiteSpace(Error) &&
        FreeXRendererPng &&
        FreeXXlsxOpenedInExcel &&
        FreeXExcelPng &&
        ExcelNativeCreated &&
        ExcelNativePng &&
        ExcelLoadedByFreeX &&
        ExcelRoundTripOpenedInExcel &&
        ExcelRoundTripPng;

    public void AddNote(string note)
    {
        if (!string.IsNullOrWhiteSpace(note))
            _notes.Add(note);
    }
}

internal sealed record ComparisonDirectories(
    string FreeXXlsx,
    string ExcelXlsx,
    string ExcelRoundTripXlsx,
    string FreeXRendererPng,
    string FreeXExcelPng,
    string ExcelNativePng,
    string ExcelRoundTripPng)
{
    public static ComparisonDirectories Create(string root)
    {
        var directories = new ComparisonDirectories(
            Path.Combine(root, "freex-xlsx"),
            Path.Combine(root, "excel-xlsx"),
            Path.Combine(root, "excel-roundtrip-xlsx"),
            Path.Combine(root, "png-freex-renderer"),
            Path.Combine(root, "png-excel-rendered-freex-xlsx"),
            Path.Combine(root, "png-excel-rendered-native"),
            Path.Combine(root, "png-excel-rendered-roundtrip"));

        foreach (var directory in directories.All())
            Directory.CreateDirectory(directory);

        return directories;
    }

    private IEnumerable<string> All()
    {
        yield return FreeXXlsx;
        yield return ExcelXlsx;
        yield return ExcelRoundTripXlsx;
        yield return FreeXRendererPng;
        yield return FreeXExcelPng;
        yield return ExcelNativePng;
        yield return ExcelRoundTripPng;
    }
}

internal sealed record CompareOptions(string? OutputDirectory, bool ShowHelp)
{
    public static CompareOptions Parse(string[] args)
    {
        string? outputDirectory = null;
        var showHelp = false;
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help" or "-h" or "/?":
                    showHelp = true;
                    break;
                case "--out":
                    if (index + 1 >= args.Length)
                        throw new ArgumentException("--out requires a directory.");
                    outputDirectory = Path.GetFullPath(args[++index]);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return new CompareOptions(outputDirectory, showHelp);
    }
}

internal static class CsvBuilderExtensions
{
    public static void AppendCsvRow(this StringBuilder builder, params object?[] values)
    {
        builder.AppendLine(string.Join(",", values.Select(Format)));
    }

    private static string Format(object? value)
    {
        var text = value switch
        {
            null => "",
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
