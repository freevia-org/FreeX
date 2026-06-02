using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FreeX.App.UI;
using FreeX.Core.IO;
using FreeX.Core.Model;

internal static partial class ChartInteropCompare
{
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
}
