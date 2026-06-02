using System.Reflection;
using FreeX.Core.IO;
using FreeX.Core.Model;
using static ExcelSmokeCom;

internal static class ExcelSmokeFixtures
{
    private const int XlOpenXmlWorkbook = 51;
    private const int XlNoChange = 1;
    private const int XlLocalSessionChanges = 2;

    public static IReadOnlyList<string> GenerateChartFixtures(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var generated = new List<string>
        {
            SaveWorkbook(CreateHistogramWorkbook(), Path.Combine(outputDirectory, "FreeX_histogram_smoke.xlsx")),
            SaveWorkbook(CreateWaterfallWorkbook(), Path.Combine(outputDirectory, "FreeX_waterfall_smoke.xlsx")),
        };

        foreach (var file in generated)
            Console.WriteLine($"Generated: {file}");

        return generated;
    }

    public static string GenerateFreeXNonChartFixture(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var generated = SaveWorkbook(CreateNonChartWorkbook(), Path.Combine(outputDirectory, "FreeX_nonchart_smoke.xlsx"));
        Console.WriteLine($"Generated: {generated}");
        return generated;
    }

    public static void GenerateExcelAuthoredFixture(dynamic workbooks, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        object? workbook = null;
        object? worksheet = null;
        try
        {
            workbook = workbooks.Add();
            worksheet = ((dynamic)workbook).Worksheets[1];
            ((dynamic)worksheet).Name = "ExcelData";

            SetExcelCellValue(worksheet, 1, 1, "Item");
            SetExcelCellValue(worksheet, 1, 2, "Amount");
            SetExcelCellValue(worksheet, 1, 3, "When");
            SetExcelCellValue(worksheet, 1, 4, "Complete");

            SetExcelCellValue(worksheet, 2, 1, "Alpha");
            SetExcelCellValue(worksheet, 2, 2, 125.50);
            SetExcelCellValue(worksheet, 2, 3, new DateTime(2026, 6, 1).ToOADate());
            SetExcelCellValue(worksheet, 2, 4, true);

            SetExcelCellValue(worksheet, 3, 1, "Beta");
            SetExcelCellValue(worksheet, 3, 2, 88.25);
            SetExcelCellValue(worksheet, 3, 3, new DateTime(2026, 6, 2).ToOADate());
            SetExcelCellValue(worksheet, 3, 4, false);

            SetExcelCellValue(worksheet, 4, 1, "Gamma");
            SetExcelCellValue(worksheet, 4, 2, 210.00);
            SetExcelCellValue(worksheet, 4, 3, new DateTime(2026, 6, 3).ToOADate());
            SetExcelCellValue(worksheet, 4, 4, true);

            SetExcelCellValue(worksheet, 6, 1, "Total");
            SetExcelCellFormula(worksheet, 6, 2, "=SUM(B2:B4)");
            ApplyExcelRangeFormat(worksheet, "A1:D1", range =>
            {
                range.Font.Bold = true;
                range.Font.Color = ToOleColor(255, 255, 255);
                range.Interior.Color = ToOleColor(31, 78, 121);
            });
            ApplyExcelRangeFormat(worksheet, "B2:B6", range => range.NumberFormat = "$#,##0.00");
            ApplyExcelRangeFormat(worksheet, "C2:C4", range => range.NumberFormat = "yyyy-mm-dd");
            AutoFitExcelColumns(worksheet, "A:D");

            ((dynamic)workbook).SaveAs(
                outputPath,
                XlOpenXmlWorkbook,
                Missing.Value,
                Missing.Value,
                false,
                false,
                XlNoChange,
                XlLocalSessionChanges,
                false,
                Missing.Value,
                Missing.Value,
                true);

            ((dynamic)workbook).Close(false);
            Console.WriteLine($"Generated: {outputPath}");
        }
        finally
        {
            try
            {
                if (workbook is not null)
                    ((dynamic)workbook).Close(false);
            }
            catch
            {
                // The workbook may already be closed after SaveAs.
            }

            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
    }

    private static string SaveWorkbook(Workbook workbook, string path)
    {
        using var stream = File.Create(path);
        new XlsxFileAdapter().Save(workbook, stream);
        return path;
    }

    private static Workbook CreateNonChartWorkbook()
    {
        var workbook = new Workbook("FreeXNonChartSmoke");
        var sheet = workbook.AddSheet("Data");
        sheet.FrozenRows = 1;
        sheet.ColumnWidths[1] = 16;
        sheet.ColumnWidths[2] = 12;
        sheet.ColumnWidths[3] = 14;
        sheet.ColumnWidths[4] = 12;

        var headerStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FontColor = CellColor.White,
            FillColor = CellColor.FromArgb(31, 78, 121),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var moneyStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var percentStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.0%" });

        SetStyledCell(sheet, 1, 1, new TextValue("Region"), headerStyle);
        SetStyledCell(sheet, 1, 2, new TextValue("Units"), headerStyle);
        SetStyledCell(sheet, 1, 3, new TextValue("Revenue"), headerStyle);
        SetStyledCell(sheet, 1, 4, new TextValue("Margin"), headerStyle);

        (string Region, double Units, double Revenue, double Margin)[] rows =
        [
            ("North", 42, 12500.25, 0.18),
            ("South", 37, 9800.00, 0.16),
            ("East", 55, 14210.75, 0.21),
            ("West", 31, 8700.50, 0.14),
            ("Online", 64, 21300.00, 0.27),
        ];

        var totalRevenue = 0.0;
        for (var index = 0; index < rows.Length; index++)
        {
            var row = (uint)index + 2;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(rows[index].Region));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(rows[index].Units));
            SetStyledCell(sheet, row, 3, new NumberValue(rows[index].Revenue), moneyStyle);
            SetStyledCell(sheet, row, 4, new NumberValue(rows[index].Margin), percentStyle);
            totalRevenue += rows[index].Revenue;
        }

        sheet.SetCell(new CellAddress(sheet.Id, 8, 2), new TextValue("Total revenue"));
        var totalCell = Cell.FromFormula("SUM(C2:C6)");
        totalCell.Value = new NumberValue(totalRevenue);
        totalCell.StyleId = moneyStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 8, 3), totalCell);
        sheet.Comments[new CellAddress(sheet.Id, 8, 3)] = "Cached formula value included for Excel reopen validation.";
        sheet.Hyperlinks[new CellAddress(sheet.Id, 10, 1)] = "https://github.com/tony-xmelon/FreeX";
        sheet.HyperlinkMetadata[new CellAddress(sheet.Id, 10, 1)] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "FreeX repository",
            "");
        sheet.SetCell(new CellAddress(sheet.Id, 10, 1), new TextValue("FreeX project"));

        var summary = workbook.AddSheet("Summary");
        summary.SetCell(new CellAddress(summary.Id, 1, 1), new TextValue("Workbook"));
        summary.SetCell(new CellAddress(summary.Id, 1, 2), new TextValue("FreeX non-chart smoke"));
        summary.SetCell(new CellAddress(summary.Id, 2, 1), new TextValue("Generated"));
        summary.SetCell(new CellAddress(summary.Id, 2, 2), new TextValue("2026-06-01"));

        workbook.DefineNamedRange(
            "SalesData",
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4)));

        return workbook;
    }

    private static void SetStyledCell(Sheet sheet, uint row, uint col, ScalarValue value, StyleId styleId)
    {
        var cell = Cell.FromValue(value);
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);
    }

    private static Workbook CreateHistogramWorkbook()
    {
        var workbook = new Workbook("HistogramSmoke");
        var sheet = workbook.AddSheet("Histogram");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));

        double[] values = [4, 7, 9, 11, 12, 16, 18, 19, 23, 27, 32, 38, 41, 47];
        for (var index = 0; index < values.Length; index++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)index + 2, 1), new NumberValue(values[index]));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Histogram,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, (uint)values.Length + 1, 1)),
            Title = "Histogram Smoke",
            ShowLegend = false,
            HistogramBinning = new HistogramBinningModel(HistogramBinningMode.BinCount, BinCount: 4),
            Left = 320,
            Top = 40,
            Width = 500,
            Height = 320,
        });

        return workbook;
    }

    private static Workbook CreateWaterfallWorkbook()
    {
        var workbook = new Workbook("WaterfallSmoke");
        var sheet = workbook.AddSheet("Waterfall");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Step"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));

        (string Label, double Amount)[] rows =
        [
            ("Opening", 120),
            ("Sales", 45),
            ("Returns", -18),
            ("Costs", -32),
            ("Closing", 115),
        ];

        for (var index = 0; index < rows.Length; index++)
        {
            var row = (uint)index + 2;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(rows[index].Label));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(rows[index].Amount));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Waterfall,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, (uint)rows.Length + 1, 2)),
            Title = "Waterfall Smoke",
            ShowLegend = false,
            WaterfallTotalPointIndices = [0, rows.Length - 1],
            Left = 320,
            Top = 40,
            Width = 500,
            Height = 320,
        });

        return workbook;
    }

    private static void SetExcelCellValue(object worksheet, int row, int col, object value)
    {
        object? cell = null;
        try
        {
            cell = ((dynamic)worksheet).Cells[row, col];
            ((dynamic)cell).Value2 = value;
        }
        finally
        {
            ReleaseComObject(cell);
        }
    }

    private static void SetExcelCellFormula(object worksheet, int row, int col, string formula)
    {
        object? cell = null;
        try
        {
            cell = ((dynamic)worksheet).Cells[row, col];
            ((dynamic)cell).Formula = formula;
        }
        finally
        {
            ReleaseComObject(cell);
        }
    }

    private static void ApplyExcelRangeFormat(object worksheet, string address, Action<dynamic> apply)
    {
        object? range = null;
        try
        {
            range = ((dynamic)worksheet).Range(address);
            apply((dynamic)range);
        }
        finally
        {
            ReleaseComObject(range);
        }
    }

    private static void AutoFitExcelColumns(object worksheet, string address)
    {
        object? range = null;
        object? columns = null;
        try
        {
            range = ((dynamic)worksheet).Range(address);
            columns = ((dynamic)range).Columns;
            ((dynamic)columns).AutoFit();
        }
        finally
        {
            ReleaseComObject(columns);
            ReleaseComObject(range);
        }
    }

    private static int ToOleColor(byte red, byte green, byte blue) =>
        red | (green << 8) | (blue << 16);
}
