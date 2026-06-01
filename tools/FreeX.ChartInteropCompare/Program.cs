using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
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
    private const int AverageHashSize = 16;
    private const double MinimumNonWhiteRatio = 0.01;
    private const int XlOpenXmlWorkbook = 51;
    private const double ExcelPointsPerPixel = 72.0 / 96.0;
    private const string ExcelProcessName = "EXCEL";

    public static int Run(string[] args)
    {
        var options = CompareOptions.Parse(args);
        if (options.ShowHelp)
        {
            WriteUsage();
            return 0;
        }

        if (options.ListCharts)
        {
            WriteChartList(CreateCases());
            return 0;
        }

        var automationCulture = CultureInfo.GetCultureInfo("en-US");
        Thread.CurrentThread.CurrentCulture = automationCulture;
        Thread.CurrentThread.CurrentUICulture = automationCulture;
        CultureInfo.CurrentCulture = automationCulture;
        CultureInfo.CurrentUICulture = automationCulture;

        var runDirectory = options.OutputDirectory ?? CreateDefaultRunDirectory();
        Directory.CreateDirectory(runDirectory);

        var directories = ComparisonDirectories.Create(runDirectory);
        var cases = FilterCases(CreateCases(), options);
        if (cases.Count == 0)
            throw new InvalidOperationException("No chart cases matched the requested filter.");

        var results = cases.Select(chartCase => new ChartCompareResult(chartCase.Name, chartCase.Type.ToString(), chartCase.Family)).ToList();

        Console.WriteLine("FreeX / Excel chart interop comparison");
        Console.WriteLine($"Run directory: {runDirectory}");
        Console.WriteLine($"Chart cases: {cases.Count}");
        Console.WriteLine($"Visual thresholds: classic={options.ClassicVisualHashThreshold}, chartEx={options.ChartExVisualHashThreshold}, known-gap={options.KnownGapVisualHashThreshold}, roundtrip={options.RoundTripVisualHashThreshold}");

        for (var index = 0; index < cases.Count; index++)
        {
            var chartCase = cases[index];
            var result = results[index];
            Console.WriteLine($"[{index + 1}/{cases.Count}] FreeX fixture: {chartCase.Name}");
            GenerateFreeXFixture(chartCase, directories, result);
        }

        for (var index = 0; index < cases.Count; index++)
        {
            var chartCase = cases[index];
            var result = results[index];
            Console.WriteLine($"[{index + 1}/{cases.Count}] Excel interop: {chartCase.Name}");
            RunExcelInteropCase(chartCase, directories, result);
        }

        EvaluateVisualParity(directories, results, options);
        WriteResults(runDirectory, results);
        TryWriteVisualContactSheets(runDirectory, results);
        Console.WriteLine($"Results: {Path.Combine(runDirectory, "chart_compare_results.csv")}");
        Console.WriteLine($"Visual metrics: {Path.Combine(runDirectory, "visual_metrics.csv")}");
        Console.WriteLine($"Summary: {Path.Combine(runDirectory, "README.md")}");

        var openabilityFailed = results.Count(result => !result.OpenabilityPassed);
        var rendererFailed = results.Count(result => result.OpenabilityPassed && !result.FreeXRendererPng);
        var visualFailed = results.Count(result => result.OpenabilityPassed && !result.VisualGatePassed);
        var knownVisualGapCharts = results.Count(result => result.KnownVisualGap);
        var knownVisualGapAllowances = results.Count(result => result.VisualStatus == VisualStatuses.KnownGap);

        Console.WriteLine(openabilityFailed == 0
            ? $"Openability/export: PASS {results.Count}/{results.Count}"
            : $"Openability/export: FAIL {results.Count - openabilityFailed}/{results.Count}; {openabilityFailed} failed.");
        Console.WriteLine(visualFailed == 0
            ? $"Visual gate: PASS {results.Count - openabilityFailed}/{results.Count - openabilityFailed} evaluated; {knownVisualGapCharts} known-gap chart(s), {knownVisualGapAllowances} allowance(s) used."
            : $"Visual gate: FAIL {visualFailed} mismatch(es); {knownVisualGapCharts} known-gap chart(s), {knownVisualGapAllowances} allowance(s) used.");

        if (openabilityFailed > 0)
            return 1;
        if (visualFailed > 0)
            return 2;
        if (rendererFailed > 0)
            return 3;
        return 0;
    }

    private static void RunExcelInteropCase(
        ChartCase chartCase,
        ComparisonDirectories directories,
        ChartCompareResult result)
    {
        var baselineExcelPids = GetExcelProcessIds();
        var ownedExcelPids = new HashSet<int>();
        object? excel = null;
        try
        {
            excel = CreateExcelApplication();
            ownedExcelPids = GetExcelProcessIds().Except(baselineExcelPids).ToHashSet();
            dynamic app = excel;

            ExportFreeXWorkbookThroughExcel(app, chartCase, directories, result);
            GenerateExcelNativeFixture(app, chartCase, directories, result);
            RoundTripExcelWorkbookThroughFreeX(app, chartCase, directories, result);
        }
        catch (Exception ex)
        {
            result.Error = AppendError(result.Error, $"Excel automation failed: {ex.GetType().Name}: {ex.Message}");
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
                    Console.Error.WriteLine($"Excel.Quit failed during cleanup for {chartCase.Name}: {ex.Message}");
                }

                ReleaseComObject(excel);
            }

            WaitForExcelProcessesToExit(ownedExcelPids, 2000);
            KillExcelProcesses(ownedExcelPids);
        }
    }

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

    private static void EvaluateVisualParity(
        ComparisonDirectories directories,
        IReadOnlyList<ChartCompareResult> results,
        CompareOptions options)
    {
        foreach (var result in results)
        {
            var expectation = VisualExpectation.For(result, options);
            result.VisualHashThreshold = expectation.HashThreshold;
            result.KnownVisualGap = expectation.KnownGapReason is not null;
            result.KnownVisualGapReason = expectation.KnownGapReason;
            result.KnownVisualGapThreshold = expectation.KnownGapReason is null
                ? null
                : options.KnownGapVisualHashThreshold;
            result.RoundTripVisualHashThreshold = expectation.RoundTripHashThreshold;

            if (!result.OpenabilityPassed)
            {
                result.VisualStatus = VisualStatuses.SkippedOpenability;
                continue;
            }

            var native = ReadPngMetrics(result.ExcelNativePngPath);
            var freexXlsx = ReadPngMetrics(result.FreeXExcelPngPath);
            var roundTrip = ReadPngMetrics(result.ExcelRoundTripPngPath);
            var freexRenderer = ReadPngMetrics(result.FreeXRendererPngPath);

            result.ExcelNativeNonWhiteRatio = native?.NonWhiteRatio;
            result.FreeXXlsxExcelNonWhiteRatio = freexXlsx?.NonWhiteRatio;
            result.ExcelRoundTripNonWhiteRatio = roundTrip?.NonWhiteRatio;
            result.FreeXRendererNonWhiteRatio = freexRenderer?.NonWhiteRatio;
            result.ExcelNativeImageSize = native?.SizeText;
            result.FreeXXlsxExcelImageSize = freexXlsx?.SizeText;
            result.ExcelRoundTripImageSize = roundTrip?.SizeText;
            result.FreeXRendererImageSize = freexRenderer?.SizeText;

            if (native is not null && freexXlsx is not null)
                result.HashDistanceNativeVsFreeXXlsx = HashDistance(native.AverageHash, freexXlsx.AverageHash);
            if (native is not null && roundTrip is not null)
                result.HashDistanceNativeVsRoundTrip = HashDistance(native.AverageHash, roundTrip.AverageHash);
            if (native is not null && freexRenderer is not null)
                result.HashDistanceNativeVsFreeXRenderer = HashDistance(native.AverageHash, freexRenderer.AverageHash);
            result.ExcelNativeRoundTripXlsxByteIdentical = FilesByteEqual(
                result.ExcelNativeXlsxPath,
                result.ExcelRoundTripXlsxPath);

            var failures = new List<string>();
            AddImageFailure(failures, "Excel-native PNG", native);
            AddImageFailure(failures, "Excel-rendered FreeX XLSX PNG", freexXlsx);
            AddImageFailure(failures, "Excel round-trip PNG", roundTrip);

            var usedKnownGapAllowance = false;
            if (result.HashDistanceNativeVsRoundTrip is int roundTripDistance &&
                roundTripDistance > options.RoundTripVisualHashThreshold)
            {
                if (result.ExcelNativeRoundTripXlsxByteIdentical)
                {
                    result.AddNote($"Round-trip PNG hash distance {roundTripDistance} ignored because the Excel-native and FreeX round-tripped XLSX packages are byte-identical.");
                }
                else if (expectation.KnownGapReason is not null && roundTripDistance <= expectation.RoundTripHashThreshold)
                {
                    usedKnownGapAllowance = true;
                    result.AddNote($"Known visual gap tolerated: {expectation.KnownGapReason} (round-trip distance {roundTripDistance}, threshold {options.RoundTripVisualHashThreshold}, known-gap threshold {expectation.RoundTripHashThreshold}).");
                }
                else
                {
                    failures.Add($"round-trip hash distance {roundTripDistance} exceeded {expectation.RoundTripHashThreshold}");
                }
            }

            if (result.HashDistanceNativeVsFreeXXlsx is not int distance)
            {
                failures.Add("native-vs-FreeX XLSX hash distance could not be computed");
            }
            else if (distance > expectation.HashThreshold)
            {
                if (expectation.KnownGapReason is not null && distance <= options.KnownGapVisualHashThreshold)
                {
                    usedKnownGapAllowance = true;
                    result.AddNote($"Known visual gap tolerated: {expectation.KnownGapReason} (distance {distance}, threshold {expectation.HashThreshold}, known-gap threshold {options.KnownGapVisualHashThreshold}).");
                }
                else
                {
                    failures.Add($"native-vs-FreeX XLSX hash distance {distance} exceeded allowed threshold {expectation.AllowedThresholdText(options)}");
                }
            }

            if (failures.Count > 0)
            {
                result.VisualStatus = VisualStatuses.Fail;
                result.VisualFailure = string.Join("; ", failures);
            }
            else
            {
                result.VisualStatus = usedKnownGapAllowance
                    ? VisualStatuses.KnownGap
                    : VisualStatuses.Pass;
            }
        }

        WriteVisualMetrics(Path.Combine(directories.Root, "visual_metrics.csv"), results);
    }

    private static void AddImageFailure(List<string> failures, string label, PngMetrics? metrics)
    {
        if (metrics is null)
        {
            failures.Add($"{label} missing");
            return;
        }

        if (metrics.NonWhiteRatio < MinimumNonWhiteRatio)
            failures.Add($"{label} appears blank (non-white ratio {metrics.NonWhiteRatio.ToString("0.####", CultureInfo.InvariantCulture)})");
    }

    private static PngMetrics? ReadPngMetrics(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];
        BitmapSource bitmap = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        var nonWhite = 0;
        var total = width * height;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (red, green, blue) = ReadCompositedPixel(pixels, stride, x, y);
                if (red < 248 || green < 248 || blue < 248)
                    nonWhite++;
            }
        }

        var samples = new double[AverageHashSize * AverageHashSize];
        var sampleIndex = 0;
        for (var row = 0; row < AverageHashSize; row++)
        {
            var y = Math.Min(height - 1, (int)((row + 0.5) * height / AverageHashSize));
            for (var column = 0; column < AverageHashSize; column++)
            {
                var x = Math.Min(width - 1, (int)((column + 0.5) * width / AverageHashSize));
                var (red, green, blue) = ReadCompositedPixel(pixels, stride, x, y);
                samples[sampleIndex++] = (red * 0.299) + (green * 0.587) + (blue * 0.114);
            }
        }

        var average = samples.Average();
        var hash = samples.Select(value => value < average).ToArray();
        return new PngMetrics(width, height, nonWhite / (double)total, hash);
    }

    private static (byte Red, byte Green, byte Blue) ReadCompositedPixel(byte[] pixels, int stride, int x, int y)
    {
        var offset = (y * stride) + (x * 4);
        var blue = pixels[offset];
        var green = pixels[offset + 1];
        var red = pixels[offset + 2];
        var alpha = pixels[offset + 3] / 255.0;
        return (
            (byte)Math.Round((red * alpha) + (255 * (1 - alpha))),
            (byte)Math.Round((green * alpha) + (255 * (1 - alpha))),
            (byte)Math.Round((blue * alpha) + (255 * (1 - alpha))));
    }

    private static int HashDistance(IReadOnlyList<bool> left, IReadOnlyList<bool> right)
    {
        var count = Math.Min(left.Count, right.Count);
        var distance = Math.Abs(left.Count - right.Count);
        for (var index = 0; index < count; index++)
        {
            if (left[index] != right[index])
                distance++;
        }

        return distance;
    }

    private static bool FilesByteEqual(string? leftPath, string? rightPath)
    {
        if (string.IsNullOrWhiteSpace(leftPath) ||
            string.IsNullOrWhiteSpace(rightPath) ||
            !File.Exists(leftPath) ||
            !File.Exists(rightPath))
        {
            return false;
        }

        using var left = File.OpenRead(leftPath);
        using var right = File.OpenRead(rightPath);
        if (left.Length != right.Length)
            return false;

        Span<byte> leftBuffer = stackalloc byte[8192];
        Span<byte> rightBuffer = stackalloc byte[8192];
        while (true)
        {
            var leftRead = left.Read(leftBuffer);
            var rightRead = right.Read(rightBuffer);
            if (leftRead != rightRead)
                return false;
            if (leftRead == 0)
                return true;
            if (!leftBuffer[..leftRead].SequenceEqual(rightBuffer[..rightRead]))
                return false;
        }
    }

    private static void WriteVisualMetrics(string path, IReadOnlyList<ChartCompareResult> results)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Chart,Family,VisualStatus,KnownVisualGap,VisualThreshold,KnownGapThreshold,RoundTripThreshold,NativeRoundTripXlsxByteIdentical,FreeXRendererNonWhite,ExcelNativeNonWhite,ExcelFreeXXlsxNonWhite,ExcelRoundTripNonWhite,HashDistance_Native_vs_FreeXXlsx,HashDistance_Native_vs_RoundTrip,HashDistance_Native_vs_FreeXRenderer,NativeSize,FreeXXlsxExcelSize,RoundTripSize,FreeXRendererSize,VisualFailure,KnownGapReason");
        foreach (var result in results)
        {
            csv.AppendCsvRow(
                result.Chart,
                result.Family,
                result.VisualStatus,
                result.KnownVisualGap,
                result.VisualHashThreshold,
                result.KnownVisualGapThreshold,
                result.RoundTripVisualHashThreshold,
                result.ExcelNativeRoundTripXlsxByteIdentical,
                result.FreeXRendererNonWhiteRatio,
                result.ExcelNativeNonWhiteRatio,
                result.FreeXXlsxExcelNonWhiteRatio,
                result.ExcelRoundTripNonWhiteRatio,
                result.HashDistanceNativeVsFreeXXlsx,
                result.HashDistanceNativeVsRoundTrip,
                result.HashDistanceNativeVsFreeXRenderer,
                result.ExcelNativeImageSize,
                result.FreeXXlsxExcelImageSize,
                result.ExcelRoundTripImageSize,
                result.FreeXRendererImageSize,
                result.VisualFailure,
                result.KnownVisualGapReason);
        }

        File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
    }

    private static void TryWriteVisualContactSheets(string runDirectory, IReadOnlyList<ChartCompareResult> results)
    {
        try
        {
            WriteVisualContactSheets(runDirectory, results);
        }
        catch (Exception ex)
        {
            var error = $"Visual contact sheet generation failed: {ex.GetType().Name}: {ex.Message}";
            Console.Error.WriteLine(error);
            File.WriteAllText(Path.Combine(runDirectory, "visual_contact_sheet_errors.txt"), error, Encoding.UTF8);
        }
    }

    private static void WriteVisualContactSheets(string runDirectory, IReadOnlyList<ChartCompareResult> results)
    {
        WriteVisualContactSheet(Path.Combine(runDirectory, "visual_contact_sheet_all.png"), results, "all");
        foreach (var group in results.GroupBy(result => result.Family, StringComparer.OrdinalIgnoreCase))
        {
            WriteVisualContactSheet(
                Path.Combine(runDirectory, $"visual_contact_sheet_{SanitizeFileName(group.Key)}.png"),
                group.ToList(),
                group.Key);
        }
    }

    private static void WriteVisualContactSheet(string path, IReadOnlyList<ChartCompareResult> results, string label)
    {
        if (results.Count == 0)
            return;

        const int rowLabelWidth = 160;
        const int columnWidth = 220;
        const int headerHeight = 46;
        const int rowHeight = 176;
        const int thumbnailHeight = 126;
        string[] headers = ["FreeX renderer", "Excel FreeX XLSX", "Excel native", "Excel round-trip"];

        var width = rowLabelWidth + (columnWidth * headers.Length);
        var height = headerHeight + (rowHeight * results.Count);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            context.DrawText(CreateText($"Visual contact sheet: {label}", 15, Brushes.Black, FontWeights.SemiBold), new Point(12, 6));
            for (var column = 0; column < headers.Length; column++)
            {
                var x = rowLabelWidth + (column * columnWidth);
                context.DrawText(CreateText(headers[column], 12, Brushes.Black, FontWeights.SemiBold), new Point(x + 8, 26));
            }

            for (var index = 0; index < results.Count; index++)
            {
                var result = results[index];
                var y = headerHeight + (index * rowHeight);
                var rowBrush = index % 2 == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(248, 248, 248));
                context.DrawRectangle(rowBrush, null, new Rect(0, y, width, rowHeight));
                context.DrawLine(new Pen(Brushes.Gainsboro, 1), new Point(0, y), new Point(width, y));

                context.DrawText(CreateText(result.Chart, 13, Brushes.Black, FontWeights.SemiBold), new Point(10, y + 10));
                context.DrawText(CreateText(result.VisualStatus, 11, StatusBrush(result.VisualStatus), FontWeights.Normal), new Point(10, y + 32));
                if (result.HashDistanceNativeVsFreeXXlsx is int distance)
                    context.DrawText(CreateText($"d={distance}", 11, Brushes.DimGray, FontWeights.Normal), new Point(10, y + 50));

                DrawImageCell(context, result.FreeXRendererPngPath, rowLabelWidth, y + 8, columnWidth, thumbnailHeight);
                DrawImageCell(context, result.FreeXExcelPngPath, rowLabelWidth + columnWidth, y + 8, columnWidth, thumbnailHeight);
                DrawImageCell(context, result.ExcelNativePngPath, rowLabelWidth + (2 * columnWidth), y + 8, columnWidth, thumbnailHeight);
                DrawImageCell(context, result.ExcelRoundTripPngPath, rowLabelWidth + (3 * columnWidth), y + 8, columnWidth, thumbnailHeight);
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void DrawImageCell(DrawingContext context, string? path, double x, double y, double width, double height)
    {
        var bounds = new Rect(x + 8, y + 22, width - 16, height);
        context.DrawRectangle(Brushes.White, new Pen(Brushes.Gainsboro, 1), bounds);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            context.DrawText(CreateText("missing", 12, Brushes.DimGray, FontWeights.Normal), new Point(bounds.X + 8, bounds.Y + 8));
            return;
        }

        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var image = decoder.Frames[0];
        var scale = Math.Min(bounds.Width / image.PixelWidth, bounds.Height / image.PixelHeight);
        var drawWidth = image.PixelWidth * scale;
        var drawHeight = image.PixelHeight * scale;
        var imageBounds = new Rect(
            bounds.X + ((bounds.Width - drawWidth) / 2),
            bounds.Y + ((bounds.Height - drawHeight) / 2),
            drawWidth,
            drawHeight);
        context.DrawImage(image, imageBounds);
    }

    private static FormattedText CreateText(string text, double fontSize, Brush brush, FontWeight weight) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            fontSize,
            brush,
            1.0);

    private static Brush StatusBrush(string status) => status switch
    {
        VisualStatuses.Pass => Brushes.ForestGreen,
        VisualStatuses.KnownGap => Brushes.DarkOrange,
        VisualStatuses.Fail => Brushes.Firebrick,
        _ => Brushes.DimGray
    };

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(invalid.Contains(character) ? '_' : char.ToLowerInvariant(character));
        return builder.ToString();
    }

    private static void WriteResults(string runDirectory, IReadOnlyList<ChartCompareResult> results)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Chart,Type,Family,FreeXRendererPng,FreeXXlsxOpenedInExcel,FreeXExcelChartCount,FreeXExcelPng,ExcelNativeCreated,ExcelNativePng,ExcelLoadedByFreeX,ExcelRoundTripOpenedInExcel,ExcelRoundTripChartCount,ExcelRoundTripPng,OpenabilityPassed,VisualStatus,VisualGatePassed,Passed,FailureCategory,Notes,OpenabilityError,VisualFailure");
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
                result.OpenabilityPassed,
                result.VisualStatus,
                result.VisualGatePassed,
                result.Passed,
                result.FailureCategory,
                result.Notes,
                result.Error,
                result.VisualFailure);
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
        builder.AppendLine("## Gate Summary");
        builder.AppendLine();
        builder.AppendLine("| Gate | Result |");
        builder.AppendLine("|---|---:|");
        builder.AppendLine($"| Openability/export | {results.Count(result => result.OpenabilityPassed)}/{results.Count} |");
        builder.AppendLine($"| FreeX renderer PNG | {results.Count(result => result.FreeXRendererPng)}/{results.Count} |");
        builder.AppendLine($"| Visual gate | {results.Count(result => result.VisualGatePassed)}/{results.Count(result => result.OpenabilityPassed)} evaluated |");
        builder.AppendLine($"| Known visual gap charts | {results.Count(result => result.KnownVisualGap)} |");
        builder.AppendLine($"| Known-gap threshold allowances used | {results.Count(result => result.VisualStatus == VisualStatuses.KnownGap)} |");
        builder.AppendLine($"| Byte-identical round-trip packages | {results.Count(result => result.ExcelNativeRoundTripXlsxByteIdentical)}/{results.Count(result => result.ExcelNativeCreated && result.ExcelLoadedByFreeX)} |");
        builder.AppendLine($"| Full pass | {results.Count(result => result.Passed)}/{results.Count} |");
        builder.AppendLine();
        builder.AppendLine("Visual status values: `pass` is within the family hash threshold; `known-gap` exceeds the normal threshold but is inside the known-gap allowance; `fail` is a blocking visual mismatch or blank/missing image; `skipped-openability` means Excel open/export did not pass first.");
        builder.AppendLine();
        builder.AppendLine("## Per-Family Visual Summary");
        builder.AppendLine();
        builder.AppendLine("| Family | Charts | Openability pass | Visual pass | Known gaps | Visual fail | Max native-vs-FreeX hash | Threshold(s) |");
        builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");
        foreach (var group in results.GroupBy(result => result.Family, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var evaluated = group.Where(result => result.OpenabilityPassed).ToList();
            var maxDistance = evaluated
                .Select(result => result.HashDistanceNativeVsFreeXXlsx)
                .Where(distance => distance.HasValue)
                .Select(distance => distance!.Value)
                .DefaultIfEmpty()
                .Max();
            var thresholds = string.Join(
                ", ",
                group.Select(result => result.VisualHashThreshold.ToString(CultureInfo.InvariantCulture))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal));
            builder.AppendLine($"| {group.Key} | {group.Count()} | {group.Count(result => result.OpenabilityPassed)} | {group.Count(result => result.VisualStatus == VisualStatuses.Pass)} | {group.Count(result => result.VisualStatus == VisualStatuses.KnownGap)} | {group.Count(result => result.VisualStatus == VisualStatuses.Fail)} | {maxDistance} | {thresholds} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Chart Matrix");
        builder.AppendLine();
        builder.AppendLine("| Chart | Family | Openability/export | Visual status | Native vs FreeX hash | Threshold | Known gap | Excel round-trip hash | Notes |");
        builder.AppendLine("|---|---|---:|---|---:|---:|---|---:|---|");
        foreach (var result in results)
        {
            builder.AppendLine($"| {result.Chart} | {result.Family} | {Mark(result.OpenabilityPassed)} | {result.VisualStatus} | {Metric(result.HashDistanceNativeVsFreeXXlsx)} | {result.VisualHashThreshold} | {EscapeMarkdown(result.KnownVisualGapReason)} | {Metric(result.HashDistanceNativeVsRoundTrip)} | {EscapeMarkdown(result.SummaryNote)} |");
        }

        builder.AppendLine();
        builder.AppendLine("Generated folders:");
        builder.AppendLine("- `freex-xlsx/`: workbooks written by FreeX.");
        builder.AppendLine("- `excel-xlsx/`: matching workbooks authored by Excel COM.");
        builder.AppendLine("- `excel-roundtrip-xlsx/`: Excel-authored workbooks loaded and saved by FreeX.");
        builder.AppendLine("- `png-*`: chart image exports from FreeX renderer or Excel.");
        builder.AppendLine("- `visual_metrics.csv`: nonblank and perceptual hash-distance metrics.");
        builder.AppendLine("- `visual_contact_sheet_*.png`: side-by-side image evidence.");
        return builder.ToString();
    }

    private static List<ChartCase> FilterCases(IEnumerable<ChartCase> cases, CompareOptions options)
    {
        var filtered = cases;
        if (options.ChartFilters.Count > 0)
        {
            filtered = filtered.Where(chartCase =>
                options.ChartFilters.Contains(chartCase.Name) ||
                options.ChartFilters.Contains(chartCase.Type.ToString()));
        }

        if (options.FamilyFilters.Count > 0)
            filtered = filtered.Where(chartCase => options.FamilyFilters.Contains(chartCase.Family));

        return filtered.ToList();
    }

    private static void WriteChartList(IEnumerable<ChartCase> cases)
    {
        Console.WriteLine("Available chart cases:");
        foreach (var chartCase in cases)
            Console.WriteLine($"  {chartCase.Name} ({chartCase.Family})");
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

    private static string AppendError(string? existing, string error) =>
        string.IsNullOrWhiteSpace(existing) ? error : $"{existing} | {error}";

    private static string Mark(bool value) => value ? "yes" : "no";

    private static string Metric(int? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "";

    private static string EscapeMarkdown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Replace("|", "\\|", StringComparison.Ordinal);

    private static void WriteUsage()
    {
        Console.WriteLine(
            """
            Usage:
              dotnet run --project tools/FreeX.ChartInteropCompare -- [options]

            Options:
              --out <directory>                  Output directory. Defaults to %USERPROFILE%\freex-xlsx-verify\chart-interop\<timestamp>.
              --chart <name>[,<name>]            Run only the named chart case(s). Can be repeated.
              --family <classic|chartEx>         Run only the requested chart family. Can be repeated.
              --classic-visual-threshold <0-256> Native-vs-FreeX hash threshold for classic charts. Default: 96.
              --chartex-visual-threshold <0-256> Native-vs-FreeX hash threshold for chartEx charts. Default: 72.
              --known-gap-threshold <0-256>      Allowed hash threshold for declared known visual gaps. Default: 128.
              --roundtrip-threshold <0-256>      Native-vs-roundtrip hash threshold. Default: 4.
              --list-charts                      Print chart cases and exit.
              --help                             Show this help text.

            Exit codes:
              0  Openability/export and visual gate passed (known gaps may be allowed).
              1  Openability/export failure.
              2  Visual mismatch failure after openability passed.
              3  FreeX renderer PNG failure after openability and visual gate passed.
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

internal static class VisualStatuses
{
    public const string NotEvaluated = "not-evaluated";
    public const string SkippedOpenability = "skipped-openability";
    public const string Pass = "pass";
    public const string KnownGap = "known-gap";
    public const string Fail = "fail";
}

internal sealed record PngMetrics(int Width, int Height, double NonWhiteRatio, IReadOnlyList<bool> AverageHash)
{
    public string SizeText => $"{Width}x{Height}";
}

internal sealed record VisualExpectation(int HashThreshold, int RoundTripHashThreshold, string? KnownGapReason)
{
    private static readonly IReadOnlyDictionary<string, string> KnownGaps =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> KnownRoundTripGaps =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static VisualExpectation For(ChartCompareResult result, CompareOptions options)
    {
        var threshold = string.Equals(result.Family, "chartEx", StringComparison.OrdinalIgnoreCase)
            ? options.ChartExVisualHashThreshold
            : options.ClassicVisualHashThreshold;
        var roundTripThreshold = KnownRoundTripGaps.Contains(result.Chart)
            ? Math.Max(options.RoundTripVisualHashThreshold, 12)
            : options.RoundTripVisualHashThreshold;
        KnownGaps.TryGetValue(result.Chart, out var reason);
        return new VisualExpectation(threshold, roundTripThreshold, reason);
    }

    public string AllowedThresholdText(CompareOptions options) =>
        KnownGapReason is null
            ? HashThreshold.ToString(CultureInfo.InvariantCulture)
            : $"{HashThreshold} (known-gap allowance {options.KnownGapVisualHashThreshold})";
}

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
    public string VisualStatus { get; set; } = VisualStatuses.NotEvaluated;
    public bool KnownVisualGap { get; set; }
    public string? KnownVisualGapReason { get; set; }
    public int VisualHashThreshold { get; set; }
    public int? KnownVisualGapThreshold { get; set; }
    public int RoundTripVisualHashThreshold { get; set; }
    public double? FreeXRendererNonWhiteRatio { get; set; }
    public double? ExcelNativeNonWhiteRatio { get; set; }
    public double? FreeXXlsxExcelNonWhiteRatio { get; set; }
    public double? ExcelRoundTripNonWhiteRatio { get; set; }
    public int? HashDistanceNativeVsFreeXXlsx { get; set; }
    public int? HashDistanceNativeVsRoundTrip { get; set; }
    public int? HashDistanceNativeVsFreeXRenderer { get; set; }
    public string? FreeXRendererImageSize { get; set; }
    public string? ExcelNativeImageSize { get; set; }
    public string? FreeXXlsxExcelImageSize { get; set; }
    public string? ExcelRoundTripImageSize { get; set; }
    public bool ExcelNativeRoundTripXlsxByteIdentical { get; set; }
    public string? VisualFailure { get; set; }
    public string Notes => string.Join("; ", _notes);
    public string SummaryNote => string.Join(
            " ",
            new[] { Notes, Error, VisualFailure }.Where(value => !string.IsNullOrWhiteSpace(value)))
        .Trim();
    public bool OpenabilityPassed =>
        string.IsNullOrWhiteSpace(Error) &&
        FreeXXlsxOpenedInExcel &&
        FreeXExcelPng &&
        ExcelNativeCreated &&
        ExcelNativePng &&
        ExcelLoadedByFreeX &&
        ExcelRoundTripOpenedInExcel &&
        ExcelRoundTripPng;
    public bool VisualGatePassed =>
        VisualStatus is VisualStatuses.Pass or VisualStatuses.KnownGap;
    public string FailureCategory =>
        !OpenabilityPassed ? "openability" :
        !FreeXRendererPng ? "freex-renderer" :
        !VisualGatePassed ? "visual-mismatch" :
        "";
    public bool Passed =>
        OpenabilityPassed &&
        FreeXRendererPng &&
        VisualGatePassed;

    public void AddNote(string note)
    {
        if (!string.IsNullOrWhiteSpace(note))
            _notes.Add(note);
    }
}

internal sealed record ComparisonDirectories(
    string Root,
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
            root,
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
        yield return Root;
        yield return FreeXXlsx;
        yield return ExcelXlsx;
        yield return ExcelRoundTripXlsx;
        yield return FreeXRendererPng;
        yield return FreeXExcelPng;
        yield return ExcelNativePng;
        yield return ExcelRoundTripPng;
    }
}

internal sealed record CompareOptions(
    string? OutputDirectory,
    IReadOnlySet<string> ChartFilters,
    IReadOnlySet<string> FamilyFilters,
    int ClassicVisualHashThreshold,
    int ChartExVisualHashThreshold,
    int KnownGapVisualHashThreshold,
    int RoundTripVisualHashThreshold,
    bool ListCharts,
    bool ShowHelp)
{
    private const int MaxHashThreshold = 256;

    public static CompareOptions Parse(string[] args)
    {
        string? outputDirectory = null;
        var chartFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var familyFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var classicVisualHashThreshold = 96;
        var chartExVisualHashThreshold = 72;
        var knownGapVisualHashThreshold = 128;
        var roundTripVisualHashThreshold = 4;
        var listCharts = false;
        var showHelp = false;
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help" or "-h" or "/?":
                    showHelp = true;
                    break;
                case "--list-charts":
                    listCharts = true;
                    break;
                case "--out":
                    if (index + 1 >= args.Length)
                        throw new ArgumentException("--out requires a directory.");
                    outputDirectory = Path.GetFullPath(args[++index]);
                    break;
                case "--chart" or "--charts":
                    if (index + 1 >= args.Length)
                        throw new ArgumentException($"{arg} requires a chart name or comma-separated list.");
                    AddFilterValues(chartFilters, args[++index]);
                    break;
                case "--family":
                    if (index + 1 >= args.Length)
                        throw new ArgumentException("--family requires classic or chartEx.");
                    AddFilterValues(familyFilters, args[++index]);
                    break;
                case "--classic-visual-threshold":
                    classicVisualHashThreshold = ReadHashThreshold(args, ref index, arg);
                    break;
                case "--chartex-visual-threshold":
                    chartExVisualHashThreshold = ReadHashThreshold(args, ref index, arg);
                    break;
                case "--known-gap-threshold":
                    knownGapVisualHashThreshold = ReadHashThreshold(args, ref index, arg);
                    break;
                case "--roundtrip-threshold":
                    roundTripVisualHashThreshold = ReadHashThreshold(args, ref index, arg);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        ValidateFamilies(familyFilters);
        return new CompareOptions(
            outputDirectory,
            chartFilters,
            familyFilters,
            classicVisualHashThreshold,
            chartExVisualHashThreshold,
            knownGapVisualHashThreshold,
            roundTripVisualHashThreshold,
            listCharts,
            showHelp);
    }

    private static void AddFilterValues(HashSet<string> filters, string value)
    {
        foreach (var part in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            filters.Add(part);
    }

    private static int ReadHashThreshold(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{optionName} requires an integer from 0 to 256.");
        if (!int.TryParse(args[++index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var threshold) ||
            threshold < 0 ||
            threshold > MaxHashThreshold)
        {
            throw new ArgumentException($"{optionName} requires an integer from 0 to {MaxHashThreshold}.");
        }

        return threshold;
    }

    private static void ValidateFamilies(IEnumerable<string> familyFilters)
    {
        foreach (var family in familyFilters)
        {
            if (!string.Equals(family, "classic", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(family, "chartEx", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unknown chart family '{family}'. Expected classic or chartEx.");
            }
        }
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
