using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using FreeX.Core.IO;
using FreeX.Core.Model;
using static ExcelSmokeCom;

internal static class ExcelSmokeFixtures
{
    private const int XlOpenXmlWorkbook = 51;
    private const int XlNoChange = 1;
    private const int XlLocalSessionChanges = 2;
    private const int XlYes = 1;
    private const int XlDatabase = 1;
    private const int XlSrcRange = 1;
    private const int XlRowField = 1;
    private const int XlColumnField = 2;
    private const int XlPageField = 3;
    private const int XlSum = -4157;
    private const int XlCount = -4112;
    private const int XlAverage = -4106;
    private const int XlPercentOfGrandTotal = 8;
    private const int XlDescending = -4121;
    private const int XlOverThenDown = 2;
    private const int XlTimeline = 2;
    private const int XlCompactRow = 0;
    private const int XlTabularRow = 1;
    private const int XlOutlineRow = 2;
    private const int XlRepeatLabels = 2;
    private const int XlAtBottom = 2;
    private const int XlCaptionBeginsWith = 17;
    private const int XlValueIsGreaterThan = 9;
    private const int XlPercentOfColumn = 7;
    private const int XlRunningTotal = 5;
    private const int XlValidateList = 3;
    private const int XlValidAlertStop = 1;
    private const int XlBetween = 1;
    private const int XlCellValue = 1;
    private const int XlGreater = 5;
    private const int MsoTextOrientationHorizontal = 1;

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

    public static IReadOnlyList<string> GenerateFreeXFeatureFixtures(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        (string FileName, Func<Workbook> Create)[] fixtures =
        [
            ("FreeX_feature_grid_formulas_smoke.xlsx", CreateFeatureGridAndFormulasWorkbook),
            ("FreeX_feature_validation_cf_smoke.xlsx", CreateFeatureValidationAndConditionalFormattingWorkbook),
            ("FreeX_feature_tables_smoke.xlsx", CreateFeatureStructuredTableWorkbook),
            ("FreeX_feature_objects_links_smoke.xlsx", CreateFeatureObjectsAndLinksWorkbook),
            ("FreeX_feature_images_sparklines_smoke.xlsx", CreateFeatureImagesAndSparklinesWorkbook),
            ("FreeX_feature_shapes_text_smoke.xlsx", CreateFeatureShapesAndTextWorkbook),
            ("FreeX_feature_pivots_smoke.xlsx", CreateFeaturePivotWorkbook),
            ("FreeX_feature_protection_page_smoke.xlsx", CreateFeatureProtectionAndPageSetupWorkbook),
        ];

        var generated = new List<string>(fixtures.Length);
        foreach (var fixture in fixtures)
        {
            var path = SaveWorkbook(fixture.Create(), Path.Combine(outputDirectory, fixture.FileName));
            generated.Add(path);
            Console.WriteLine($"Generated: {path}");
        }

        return generated;
    }

    public static IReadOnlyList<string> GetExcelPivotCorpusFixturePaths(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        return
        [
            Path.Combine(outputDirectory, "Excel_native_pivot_basic_row_column_001.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_table_source_filters_001.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_grouping_show_values_001.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_multiple_pivots_one_cache_001.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_report_filters_001.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_slicer_timeline_001.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_filters_sorts_002.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_layout_options_002.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_date_grouping_003.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_calculated_field_item_003.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_show_items_no_data_004.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_layout_matrix_004.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_subtotal_grand_totals_004.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_named_range_source_004.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_show_values_as_variants_004.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_chrome_style_flags_004.xlsx"),
        ];
    }

    public static void GenerateExcelAuthoredFixture(dynamic workbooks, string outputPath)
    {
        var fileName = Path.GetFileName(outputPath);
        if (fileName.StartsWith("Excel_native_pivot_", StringComparison.OrdinalIgnoreCase))
        {
            GenerateExcelNativePivotCorpusFixture(workbooks, outputPath, fileName);
            return;
        }

        if (fileName.StartsWith("Excel_native_comment_", StringComparison.OrdinalIgnoreCase))
        {
            GenerateExcelNativeCommentCorpusFixture(workbooks, outputPath, fileName);
            return;
        }

        if (fileName.StartsWith("Excel_native_cf_", StringComparison.OrdinalIgnoreCase))
        {
            GenerateExcelNativeCfCorpusFixture(workbooks, outputPath, fileName);
            return;
        }

        if (fileName.StartsWith("Excel_native_table_", StringComparison.OrdinalIgnoreCase))
        {
            GenerateExcelNativeTableCorpusFixture(workbooks, outputPath, fileName);
            return;
        }

        if (fileName.StartsWith("Excel_native_sparkline_", StringComparison.OrdinalIgnoreCase))
        {
            GenerateExcelNativeSparklineCorpusFixture(workbooks, outputPath, fileName);
            return;
        }

        if (fileName.StartsWith("Excel_native_cellstyle_", StringComparison.OrdinalIgnoreCase))
        {
            GenerateExcelNativeCellStyleCorpusFixture(workbooks, outputPath, fileName);
            return;
        }

        if (fileName.StartsWith("Excel_native_shapes_", StringComparison.OrdinalIgnoreCase))
        {
            GenerateExcelNativeShapesCorpusFixture(workbooks, outputPath, fileName);
            return;
        }

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

            SetExcelCellValue(worksheet, 5, 1, "Excel smoke link");
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
            AddExcelTable(worksheet, "A1:D4", "ExcelAuthoredSmokeTable");
            AddExcelListValidation(worksheet, "A2:A10", "Alpha,Beta,Gamma");
            AddExcelConditionalFormat(worksheet, "B2:B4", 100);
            AddExcelComment(worksheet, "D2", "Excel-authored note for FreeX save/reopen validation.");
            AddExcelHyperlink(worksheet, "A5", "https://example.com/freex-excel-smoke", "Excel smoke link");
            AddExcelNamedRange(workbook, "ExcelAuthoredAmounts", "=ExcelData!$B$2:$B$4");
            AddExcelTextBox(worksheet, "Excel-authored text box");
            AddExcelPivotTable(workbook, worksheet);
            AutoFitExcelColumns(worksheet, "A:D");
            ProtectExcelWorksheetAndWorkbook(workbook, worksheet);

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

    private static void GenerateExcelNativePivotCorpusFixture(dynamic workbooks, string outputPath, string fileName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        object? workbook = null;
        object? dataSheet = null;
        try
        {
            workbook = workbooks.Add();
            dataSheet = ((dynamic)workbook).Worksheets[1];
            ((dynamic)dataSheet).Name = "SalesData";
            PopulateNativePivotSalesData(workbook, dataSheet);

            if (fileName.Contains("basic_row_column", StringComparison.OrdinalIgnoreCase))
                AddNativePivotBasicRowColumn(workbook, dataSheet);
            else if (fileName.Contains("table_source_filters", StringComparison.OrdinalIgnoreCase))
                AddNativePivotTableSourceFilters(workbook, dataSheet);
            else if (fileName.Contains("grouping_show_values", StringComparison.OrdinalIgnoreCase))
                AddNativePivotGroupingShowValues(workbook, dataSheet);
            else if (fileName.Contains("multiple_pivots_one_cache", StringComparison.OrdinalIgnoreCase))
                AddNativePivotMultiplePivotsOneCache(workbook, dataSheet);
            else if (fileName.Contains("report_filters", StringComparison.OrdinalIgnoreCase))
                AddNativePivotReportFilters(workbook, dataSheet);
            else if (fileName.Contains("slicer_timeline", StringComparison.OrdinalIgnoreCase))
                AddNativePivotSlicerTimeline(workbook, dataSheet);
            else if (fileName.Contains("filters_sorts", StringComparison.OrdinalIgnoreCase))
                AddNativePivotFiltersSorts(workbook, dataSheet);
            else if (fileName.Contains("layout_options", StringComparison.OrdinalIgnoreCase))
                AddNativePivotLayoutOptions(workbook, dataSheet);
            else if (fileName.Contains("date_grouping", StringComparison.OrdinalIgnoreCase))
                AddNativePivotDateGrouping(workbook, dataSheet);
            else if (fileName.Contains("calculated_field_item", StringComparison.OrdinalIgnoreCase))
                AddNativePivotCalculatedFieldItem(workbook, dataSheet);
            else if (fileName.Contains("show_items_no_data", StringComparison.OrdinalIgnoreCase))
                AddNativePivotShowItemsWithNoData(workbook, dataSheet);
            else if (fileName.Contains("layout_matrix", StringComparison.OrdinalIgnoreCase))
                AddNativePivotLayoutMatrix(workbook, dataSheet);
            else if (fileName.Contains("subtotal_grand_totals", StringComparison.OrdinalIgnoreCase))
                AddNativePivotSubtotalGrandTotals(workbook, dataSheet);
            else if (fileName.Contains("named_range_source", StringComparison.OrdinalIgnoreCase))
                AddNativePivotNamedRangeSource(workbook, dataSheet);
            else if (fileName.Contains("show_values_as_variants", StringComparison.OrdinalIgnoreCase))
                AddNativePivotShowValuesAsVariants(workbook, dataSheet);
            else if (fileName.Contains("chrome_style_flags", StringComparison.OrdinalIgnoreCase))
                AddNativePivotChromeStyleFlags(workbook, dataSheet);
            else
                throw new InvalidOperationException($"Unknown Excel native PivotTable fixture: {fileName}");

            AutoFitExcelColumns(dataSheet, "A:G");
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
            workbook = null;

            if (fileName.Contains("show_items_no_data", StringComparison.OrdinalIgnoreCase))
                PatchNativePivotShowItemsWithNoDataFlags(outputPath);

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

            ReleaseComObject(dataSheet);
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

    private static Workbook CreateFeatureGridAndFormulasWorkbook()
    {
        var workbook = new Workbook("FreeXFeatureGridFormulasSmoke");
        var data = workbook.AddSheet("Data");
        var summary = workbook.AddSheet("Summary");
        var headerStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FontColor = CellColor.White,
            FillColor = new CellColor(68, 114, 196),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var dateStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "yyyy-mm-dd" });
        var moneyStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var percentStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.0%" });

        data.FrozenRows = 1;
        data.ColumnWidths[1] = 14;
        data.ColumnWidths[2] = 12;
        data.ColumnWidths[3] = 14;
        data.ColumnWidths[4] = 12;
        Set(data, "A1", new TextValue("Region"), headerStyle);
        Set(data, "B1", new TextValue("Date"), headerStyle);
        Set(data, "C1", new TextValue("Revenue"), headerStyle);
        Set(data, "D1", new TextValue("Margin"), headerStyle);
        Set(data, "E1", new TextValue("Complete"), headerStyle);
        Set(data, "F1", new TextValue("Error"), headerStyle);

        (string Region, DateTime Date, double Revenue, double Margin, bool Complete)[] rows =
        [
            ("North", new DateTime(2026, 1, 31), 12500.25, 0.18, true),
            ("South", new DateTime(2026, 2, 28), 9800.00, 0.16, false),
            ("East", new DateTime(2026, 3, 31), 14210.75, 0.21, true),
            ("West", new DateTime(2026, 4, 30), 8700.50, 0.14, false),
        ];

        for (var index = 0; index < rows.Length; index++)
        {
            var row = (uint)index + 2;
            Set(data, row, 1, new TextValue(rows[index].Region));
            Set(data, row, 2, DateTimeValue.FromDateTime(rows[index].Date), dateStyle);
            Set(data, row, 3, new NumberValue(rows[index].Revenue), moneyStyle);
            Set(data, row, 4, new NumberValue(rows[index].Margin), percentStyle);
            Set(data, row, 5, new BoolValue(rows[index].Complete));
        }

        Set(data, "F2", ErrorValue.NA);
        workbook.DefineNamedRange("FeatureRevenue", Range(data, "C2", "C5"));
        workbook.DefineNamedRange("FeatureRegions", Range(data, "A2", "A5"));
        Formula(summary, "A1", "SUM(FeatureRevenue)");
        Formula(summary, "A2", "AVERAGE(Data!D2:D5)");
        Formula(summary, "A3", "COUNTIF(Data!E2:E5,TRUE)");
        Formula(summary, "A4", "INDEX(FeatureRegions,2)");
        return workbook;
    }

    private static Workbook CreateFeatureValidationAndConditionalFormattingWorkbook()
    {
        var workbook = new Workbook("FreeXFeatureValidationCfSmoke");
        var sheet = workbook.AddSheet("Validation CF");
        Set(sheet, "A1", new TextValue("Status"));
        Set(sheet, "B1", new TextValue("Score"));
        Set(sheet, "C1", new TextValue("Comment"));
        Set(sheet, "D1", new TextValue("Trend"));

        string[] statuses = ["Open", "Review", "Closed", "Open", "Review"];
        double[] scores = [25, 55, 82, 91, 44];
        for (var index = 0; index < statuses.Length; index++)
        {
            var row = (uint)index + 2;
            Set(sheet, row, 1, new TextValue(statuses[index]));
            Set(sheet, row, 2, new NumberValue(scores[index]));
            Set(sheet, row, 3, new TextValue(index % 2 == 0 ? "needs review" : "ok"));
            Set(sheet, row, 4, new NumberValue(scores[index] - 50));
        }

        var listValidation = new DataValidation
        {
            AppliesTo = Range(sheet, "A2", "A20"),
            Type = DvType.List,
            Formula1 = "Open,Review,Closed",
            PromptTitle = "Status",
            PromptMessage = "Pick one status.",
            ErrorTitle = "Invalid status",
            ErrorMessage = "Use Open, Review, or Closed."
        };
        listValidation.AdditionalRanges.Add(Range(sheet, "F2", "F20"));
        sheet.DataValidations.Add(listValidation);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, "B2", "B20"),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "0",
            Formula2 = "100"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, "C2", "C20"),
            Type = DvType.TextLength,
            Operator = DvOperator.LessThanOrEqual,
            Formula1 = "80"
        });

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "B2", "B6"),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "80",
            FormatIfTrue = new CellStyle
            {
                FillColor = new CellColor(198, 239, 206),
                FontColor = new CellColor(0, 97, 0),
                Bold = true
            }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "B2", "B6"),
            Priority = 2,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "D2", "D6"),
            Priority = 3,
            RuleType = CfRuleType.DataBar,
            DataBarMinThresholdType = CfThresholdType.Number,
            DataBarMinThresholdValue = "-50",
            DataBarMaxThresholdType = CfThresholdType.Number,
            DataBarMaxThresholdValue = "50",
            DataBarAxisPosition = "middle",
            DataBarNegativeFillColor = new RgbColor(220, 80, 80)
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "C2", "C6"),
            Priority = 4,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "review",
            FormulaText = "NOT(ISERROR(SEARCH(\"review\",C2)))",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 235, 156) }
        });
        return workbook;
    }

    private static Workbook CreateFeatureStructuredTableWorkbook()
    {
        var workbook = new Workbook("FreeXFeatureTablesSmoke");
        var sheet = workbook.AddSheet("Table");
        Set(sheet, "A1", new TextValue("Region"));
        Set(sheet, "B1", new TextValue("Category"));
        Set(sheet, "C1", new TextValue("Amount"));
        Set(sheet, "A2", new TextValue("North"));
        Set(sheet, "B2", new TextValue("Hardware"));
        Set(sheet, "C2", new NumberValue(100));
        Set(sheet, "A3", new TextValue("South"));
        Set(sheet, "B3", new TextValue("Services"));
        Set(sheet, "C3", new NumberValue(125));
        Set(sheet, "A4", new TextValue("North"));
        Set(sheet, "B4", new TextValue("Software"));
        Set(sheet, "C4", new NumberValue(150));
        Set(sheet, "A5", new TextValue("Total"));
        Set(sheet, "C5", new NumberValue(375));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "FeatureSalesTable",
            DisplayName = "FeatureSalesTable",
            Range = Range(sheet, "A1", "C5"),
            HasAutoFilter = true,
            TotalsRowShown = true,
            StyleName = "TableStyleMedium9",
            ShowRowStripes = true,
            ShowFirstColumn = true,
            PackagePart = "xl/tables/table1.xml"
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Category"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Amount", TotalsRowFunction: "sum"));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["North", "South"]));
        sheet.StructuredTables.Add(table);
        return workbook;
    }

    private static Workbook CreateFeatureObjectsAndLinksWorkbook()
    {
        var workbook = new Workbook("FreeXFeatureObjectsLinksSmoke");
        var sheet = workbook.AddSheet("Objects Links");
        var hyperlinkStyle = RegisterHyperlinkStyle(workbook);
        Set(sheet, "A1", new TextValue("Web link"), hyperlinkStyle);
        Set(sheet, "A2", new TextValue("Email link"), hyperlinkStyle);
        Set(sheet, "A3", new TextValue("Sheet link"), hyperlinkStyle);
        Set(sheet, "B1", new TextValue("Comment target"));
        sheet.Hyperlinks[Addr(sheet, "A1")] = "https://example.com/freex";
        sheet.HyperlinkMetadata[Addr(sheet, "A1")] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open FreeX smoke target");
        sheet.Hyperlinks[Addr(sheet, "A2")] = "mailto:review@example.com";
        sheet.HyperlinkMetadata[Addr(sheet, "A2")] = new HyperlinkMetadata(
            HyperlinkTargetKind.EmailAddress,
            "Send review email");
        sheet.Hyperlinks[Addr(sheet, "A3")] = "Objects Links!B1";
        sheet.HyperlinkMetadata[Addr(sheet, "A3")] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump to comment target",
            "Objects Links!B1");
        sheet.Comments[Addr(sheet, "B1")] = "Comment package should open, save, and reopen in desktop Excel.";
        return workbook;
    }

    private static Workbook CreateFeatureImagesAndSparklinesWorkbook()
    {
        var workbook = new Workbook("FreeXFeatureImagesSparklinesSmoke");
        var sheet = workbook.AddSheet("Images Sparklines");
        Set(sheet, "A1", new NumberValue(5));
        Set(sheet, "B1", new NumberValue(7));
        Set(sheet, "C1", new NumberValue(9));
        Set(sheet, "A2", new NumberValue(3));
        Set(sheet, "B2", new NumberValue(4));
        Set(sheet, "C2", new NumberValue(8));
        sheet.BackgroundImage = new WorksheetBackgroundImage(MinimalPngBytes(), "image/png", "feature-background.png");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Feature Image 1",
            Anchor = Addr(sheet, "F2"),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64,
            Title = "Feature image title",
            AltText = "Feature smoke image"
        });
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = Range(sheet, "A1", "C1"),
            Location = Addr(sheet, "D1"),
            Kind = SparklineKind.Line
        });
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = Range(sheet, "A2", "C2"),
            Location = Addr(sheet, "D2"),
            Kind = SparklineKind.Column
        });
        return workbook;
    }

    private static Workbook CreateFeatureShapesAndTextWorkbook()
    {
        var workbook = new Workbook("FreeXFeatureShapesTextSmoke");
        var sheet = workbook.AddSheet("Shapes Text");
        Set(sheet, "A1", new TextValue("Drawing objects"));
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Name = "Feature Text Box 1",
            Anchor = Addr(sheet, "B2"),
            Text = "FreeX text box",
            Width = 200,
            Height = 90,
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25),
            Title = "Feature text box title",
            AltText = "Feature text box"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Feature Ellipse 1",
            Anchor = Addr(sheet, "D5"),
            Kind = DrawingShapeKind.Ellipse,
            Width = 140,
            Height = 90,
            FillColor = new CellColor(221, 235, 247),
            GradientFillEndColor = new CellColor(189, 215, 238),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.5),
            HasShadowEffect = true,
            Title = "Feature ellipse title",
            AltText = "Feature ellipse"
        });
        return workbook;
    }

    private static Workbook CreateFeatureProtectionAndPageSetupWorkbook()
    {
        var workbook = new Workbook("FreeXFeatureProtectionPageSmoke");
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        workbook.FullCalculationOnLoad = true;
        workbook.ForceFullCalculation = true;
        workbook.IsStructureProtected = true;
        workbook.StructureProtectionPassword = "structure";

        var sheet = workbook.AddSheet("Print Protect");
        Set(sheet, "A1", new TextValue("Protected print fixture"));
        Set(sheet, "A2", new NumberValue(42));
        sheet.TabColor = new CellColor(0, 176, 80);
        sheet.FrozenRows = 1;
        sheet.IsProtected = true;
        sheet.ProtectionPassword = "fixture";
        sheet.AllowEditRanges.Add(Range(sheet, "A2", "B5"));
        sheet.PrintArea = Range(sheet, "A1", "D40");
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, 1);
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        sheet.PageHeader = new WorksheetHeaderFooter("FreeX", "Feature smoke", "2026");
        sheet.PageFooter = new WorksheetHeaderFooter("", "Page &P of &N", "");
        sheet.RowPageBreaks.Add(20);
        sheet.ColumnPageBreaks.Add(4);

        var hidden = workbook.AddSheet("Hidden Meta");
        Set(hidden, "A1", new TextValue("Hidden metadata fixture"));
        hidden.IsHidden = true;
        return workbook;
    }

    private static Workbook CreateFeaturePivotWorkbook()
    {
        var workbook = new Workbook("FreeXFeaturePivotsSmoke");
        workbook.NumberFormatCatalog[165] = "#,##0.0 \"kg\"";
        var sheet = workbook.AddSheet("Pivot Data");

        Set(sheet, "A1", new TextValue("Region"));
        Set(sheet, "B1", new TextValue("Category"));
        Set(sheet, "C1", new TextValue("Amount"));
        Set(sheet, "A2", new TextValue("North"));
        Set(sheet, "B2", new TextValue("Hardware"));
        Set(sheet, "C2", new NumberValue(100));
        Set(sheet, "A3", new TextValue("South"));
        Set(sheet, "B3", new TextValue("Software"));
        Set(sheet, "C3", new NumberValue(125));
        Set(sheet, "A4", new TextValue("North"));
        Set(sheet, "B4", new TextValue("Services"));
        Set(sheet, "C4", new NumberValue(80));

        Set(sheet, "E1", new TextValue("Region"));
        Set(sheet, "F1", new TextValue("Sum of Amount"));
        Set(sheet, "E2", new TextValue("North"));
        Set(sheet, "F2", new NumberValue(180));
        Set(sheet, "E3", new TextValue("Grand Total"));
        Set(sheet, "F3", new NumberValue(180));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:C4",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RefreshOnLoad = true,
            PreserveSourceSortFilter = false,
            RecordCount = 3,
            CreatedVersion = 8,
            MinRefreshableVersion = 4,
            RefreshedVersion = 8,
            RefreshedBy = "FreeX Smoke",
            RefreshedDateIso = "2026-06-03T00:00:00Z"
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["North", "South"]));
        cache.Fields.Add(new PivotCacheFieldModel("Category", ContainsString: true, SharedItems: ["Hardware", "Software", "Services"]));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", 165, ContainsNumber: true, MinValue: 80, MaxValue: 125));
        workbook.PivotCaches.Add(cache);

        var style = new PivotTableStyleModel
        {
            Name = "FreeXSmokePivotStyle",
            AppliesToPivotTables = true,
            AppliesToTables = false
        };
        style.Elements.Add(new PivotTableStyleElementModel("wholeTable", 0));
        style.Elements.Add(new PivotTableStyleElementModel("headerRow", 1));
        workbook.PivotTableStyles.Add(style);

        var pivot = new PivotTableModel
        {
            Name = "FreeXSmokePivot",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "C4"),
            TargetRange = Range(sheet, "E1", "F3"),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
            StyleName = style.Name,
            ShowRowStripes = true,
            RepeatItemLabels = false,
            DataCaption = "Values",
            GrandTotalCaption = "Grand Total",
            MissingCaption = "(blank)"
        };
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "Hardware"));
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["North"]));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum", 165, null, PivotShowValuesAs.None, null, null, "#,##0.0 \"kg\""));
        sheet.PivotTables.Add(pivot);
        return workbook;
    }

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static void Set(Sheet sheet, string a1, ScalarValue value) =>
        sheet.SetCell(Addr(sheet, a1), value);

    private static void Set(Sheet sheet, uint row, uint col, ScalarValue value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), value);

    private static void Set(Sheet sheet, string a1, ScalarValue value, StyleId styleId)
    {
        var address = Addr(sheet, a1);
        sheet.SetCell(address, value);
        sheet.GetCell(address)!.StyleId = styleId;
    }

    private static void Set(Sheet sheet, uint row, uint col, ScalarValue value, StyleId styleId)
    {
        var address = new CellAddress(sheet.Id, row, col);
        sheet.SetCell(address, value);
        sheet.GetCell(address)!.StyleId = styleId;
    }

    private static void Formula(Sheet sheet, string a1, string formula) =>
        sheet.SetFormula(Addr(sheet, a1), formula);

    private static StyleId RegisterHyperlinkStyle(Workbook workbook) =>
        workbook.RegisterStyle(new CellStyle
        {
            Underline = true,
            FontColor = new CellColor(5, 99, 193)
        });

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

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

    private static void AddExcelTable(object worksheet, string address, string tableName)
    {
        object? range = null;
        object? listObjects = null;
        object? table = null;
        try
        {
            range = ((dynamic)worksheet).Range(address);
            listObjects = ((dynamic)worksheet).ListObjects;
            table = ((dynamic)listObjects).Add(XlSrcRange, range, Missing.Value, XlYes);
            ((dynamic)table).Name = tableName;
            ((dynamic)table).TableStyle = "TableStyleMedium2";
        }
        finally
        {
            ReleaseComObject(table);
            ReleaseComObject(listObjects);
            ReleaseComObject(range);
        }
    }

    private static void AddExcelListValidation(object worksheet, string address, string formula)
    {
        object? range = null;
        object? validation = null;
        try
        {
            range = ((dynamic)worksheet).Range(address);
            validation = ((dynamic)range).Validation;
            ((dynamic)validation).Delete();
            ((dynamic)validation).Add(XlValidateList, XlValidAlertStop, XlBetween, formula);
        }
        finally
        {
            ReleaseComObject(validation);
            ReleaseComObject(range);
        }
    }

    private static void AddExcelConditionalFormat(object worksheet, string address, double threshold)
    {
        object? range = null;
        object? conditions = null;
        object? condition = null;
        try
        {
            range = ((dynamic)worksheet).Range(address);
            conditions = ((dynamic)range).FormatConditions;
            condition = ((dynamic)conditions).Add(XlCellValue, XlGreater, threshold.ToString(CultureInfo.InvariantCulture));
            ((dynamic)condition).Interior.Color = ToOleColor(198, 239, 206);
            ((dynamic)condition).Font.Color = ToOleColor(0, 97, 0);
        }
        finally
        {
            ReleaseComObject(condition);
            ReleaseComObject(conditions);
            ReleaseComObject(range);
        }
    }

    private static void AddExcelComment(object worksheet, string address, string text)
    {
        object? range = null;
        object? comment = null;
        try
        {
            range = ((dynamic)worksheet).Range(address);
            comment = ((dynamic)range).AddComment(text);
        }
        finally
        {
            ReleaseComObject(comment);
            ReleaseComObject(range);
        }
    }

    private static void AddExcelHyperlink(object worksheet, string address, string target, string displayText)
    {
        object? range = null;
        object? hyperlinks = null;
        object? hyperlink = null;
        try
        {
            range = ((dynamic)worksheet).Range(address);
            hyperlinks = ((dynamic)worksheet).Hyperlinks;
            hyperlink = ((dynamic)hyperlinks).Add(range, target, Missing.Value, displayText, displayText);
        }
        finally
        {
            ReleaseComObject(hyperlink);
            ReleaseComObject(hyperlinks);
            ReleaseComObject(range);
        }
    }

    private static void AddExcelNamedRange(object workbook, string name, string refersTo)
    {
        object? names = null;
        object? namedRange = null;
        try
        {
            names = ((dynamic)workbook).Names;
            namedRange = ((dynamic)names).Add(name, refersTo);
        }
        finally
        {
            ReleaseComObject(namedRange);
            ReleaseComObject(names);
        }
    }

    private static void AddExcelTextBox(object worksheet, string text)
    {
        object? shapes = null;
        object? shape = null;
        object? textFrame = null;
        object? characters = null;
        try
        {
            shapes = ((dynamic)worksheet).Shapes;
            shape = ((dynamic)shapes).AddTextbox(MsoTextOrientationHorizontal, 320, 40, 180, 64);
            ((dynamic)shape).Name = "ExcelAuthoredSmokeTextBox";
            textFrame = ((dynamic)shape).TextFrame;
            characters = ((dynamic)textFrame).Characters();
            ((dynamic)characters).Text = text;
        }
        finally
        {
            ReleaseComObject(characters);
            ReleaseComObject(textFrame);
            ReleaseComObject(shape);
            ReleaseComObject(shapes);
        }
    }

    private static void ProtectExcelWorksheetAndWorkbook(object workbook, object worksheet)
    {
        ((dynamic)worksheet).Protect("fixture");
        ((dynamic)workbook).Protect("structure", true, false);
    }

    private static void AddExcelPivotTable(object workbook, object sourceWorksheet)
    {
        object? worksheets = null;
        object? pivotSheet = null;
        object? pivotCaches = null;
        object? pivotCache = null;
        object? pivotTable = null;
        object? itemField = null;
        object? completeField = null;
        object? amountField = null;
        object? dataField = null;
        try
        {
            worksheets = ((dynamic)workbook).Worksheets;
            pivotSheet = ((dynamic)worksheets).Add(Missing.Value, sourceWorksheet);
            ((dynamic)pivotSheet).Name = "ExcelPivot";
            SetExcelCellValue(pivotSheet, 1, 1, "Excel-authored PivotTable");

            pivotCaches = ((dynamic)workbook).PivotCaches();
            pivotCache = ((dynamic)pivotCaches).Create(XlDatabase, "'ExcelData'!R1C1:R4C4");
            pivotTable = ((dynamic)pivotCache).CreatePivotTable("'ExcelPivot'!R3C1", "ExcelAuthoredSmokePivot");

            itemField = ((dynamic)pivotTable).PivotFields("Item");
            ((dynamic)itemField).Orientation = XlRowField;

            completeField = ((dynamic)pivotTable).PivotFields("Complete");
            ((dynamic)completeField).Orientation = XlColumnField;

            amountField = ((dynamic)pivotTable).PivotFields("Amount");
            dataField = ((dynamic)pivotTable).AddDataField(amountField, "Sum of Amount", XlSum);
            ((dynamic)dataField).NumberFormat = "$#,##0.00";

            ((dynamic)pivotTable).TableStyle2 = "PivotStyleMedium9";
            AutoFitExcelColumns(pivotSheet, "A:D");
        }
        finally
        {
            ReleaseComObject(dataField);
            ReleaseComObject(amountField);
            ReleaseComObject(completeField);
            ReleaseComObject(itemField);
            ReleaseComObject(pivotTable);
            ReleaseComObject(pivotCache);
            ReleaseComObject(pivotCaches);
            ReleaseComObject(pivotSheet);
            ReleaseComObject(worksheets);
        }
    }

    private static void PopulateNativePivotSalesData(object workbook, object worksheet)
    {
        string[] headers = ["Region", "Category", "Channel", "SaleDate", "Month", "QuantityBucket", "Sales"];
        for (var col = 0; col < headers.Length; col++)
            SetExcelCellValue(worksheet, 1, col + 1, headers[col]);

        (string Region, string Category, string Channel, DateTime Date, string Bucket, double Sales)[] rows =
        [
            ("North", "Hardware", "Online", new DateTime(2026, 1, 5), "Small", 1250),
            ("North", "Software", "Direct", new DateTime(2026, 1, 12), "Medium", 2140),
            ("South", "Hardware", "Online", new DateTime(2026, 1, 22), "Large", 3160),
            ("East", "Services", "Partner", new DateTime(2026, 2, 3), "Small", 980),
            ("West", "Software", "Direct", new DateTime(2026, 2, 10), "Medium", 1875),
            ("North", "Services", "Partner", new DateTime(2026, 2, 19), "Large", 4280),
            ("South", "Software", "Online", new DateTime(2026, 3, 2), "Small", 1420),
            ("East", "Hardware", "Direct", new DateTime(2026, 3, 15), "Medium", 2360),
            ("West", "Services", "Partner", new DateTime(2026, 3, 24), "Large", 3625),
            ("North", "Hardware", "Online", new DateTime(2026, 4, 4), "Small", 1310),
            ("South", "Services", "Direct", new DateTime(2026, 4, 13), "Medium", 2440),
            ("East", "Software", "Partner", new DateTime(2026, 4, 21), "Large", 3890),
        ];

        for (var index = 0; index < rows.Length; index++)
        {
            var row = index + 2;
            SetExcelCellValue(worksheet, row, 1, rows[index].Region);
            SetExcelCellValue(worksheet, row, 2, rows[index].Category);
            SetExcelCellValue(worksheet, row, 3, rows[index].Channel);
            SetExcelCellValue(worksheet, row, 4, rows[index].Date.ToOADate());
            SetExcelCellValue(worksheet, row, 5, rows[index].Date.ToString("MMM yyyy", CultureInfo.InvariantCulture));
            SetExcelCellValue(worksheet, row, 6, rows[index].Bucket);
            SetExcelCellValue(worksheet, row, 7, rows[index].Sales);
        }

        ApplyExcelRangeFormat(worksheet, "A1:G1", range =>
        {
            range.Font.Bold = true;
            range.Font.Color = ToOleColor(255, 255, 255);
            range.Interior.Color = ToOleColor(31, 78, 121);
        });
        ApplyExcelRangeFormat(worksheet, "D2:D13", range => range.NumberFormat = "yyyy-mm-dd");
        ApplyExcelRangeFormat(worksheet, "G2:G13", range => range.NumberFormat = "$#,##0");
        AddExcelTable(worksheet, "A1:G13", "NativeSalesTable");
        AddExcelNamedRange(workbook, "NativeSalesRange", "=SalesData!$A$1:$G$13");
    }

    private static void AddNativePivotBasicRowColumn(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? cache = null;
        object? pivot = null;
        object? regionField = null;
        object? categoryField = null;
        object? salesField = null;
        object? dataField = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot Basic");
            SetExcelCellValue(pivotSheet, 1, 1, "Native row/column PivotTable");
            cache = CreateWorksheetRangePivotCache(workbook, "'SalesData'!R1C1:R13C7");
            pivot = ((dynamic)cache).CreatePivotTable("'Pivot Basic'!R3C1", "NativePivotBasic");

            regionField = ((dynamic)pivot).PivotFields("Region");
            ((dynamic)regionField).Orientation = XlRowField;
            categoryField = ((dynamic)pivot).PivotFields("Category");
            ((dynamic)categoryField).Orientation = XlColumnField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            dataField = ((dynamic)pivot).AddDataField(salesField, "Sum of Sales", XlSum);
            ((dynamic)dataField).NumberFormat = "$#,##0";
            ((dynamic)pivot).TableStyle2 = "PivotStyleMedium9";
            RefreshExcelPivotTable(pivot);
            AutoFitExcelColumns(pivotSheet, "A:F");
        }
        finally
        {
            ReleaseComObject(dataField);
            ReleaseComObject(salesField);
            ReleaseComObject(categoryField);
            ReleaseComObject(regionField);
            ReleaseComObject(pivot);
            ReleaseComObject(cache);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void AddNativePivotTableSourceFilters(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? table = null;
        object? cache = null;
        object? pivot = null;
        object? regionField = null;
        object? hiddenRegionItem = null;
        object? categoryField = null;
        object? salesField = null;
        object? dataField = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot Filters");
            SetExcelCellValue(pivotSheet, 1, 1, "Native table-source PivotTable with row item filter");
            table = ((dynamic)sourceWorksheet).ListObjects["NativeSalesTable"];
            cache = ((dynamic)workbook).PivotCaches().Create(XlDatabase, ((dynamic)table).Range);
            pivot = ((dynamic)cache).CreatePivotTable("'Pivot Filters'!R3C1", "NativePivotTableSourceFilters");

            regionField = ((dynamic)pivot).PivotFields("Region");
            ((dynamic)regionField).Orientation = XlRowField;
            categoryField = ((dynamic)pivot).PivotFields("Category");
            ((dynamic)categoryField).Orientation = XlColumnField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            dataField = ((dynamic)pivot).AddDataField(salesField, "Sum of Sales", XlSum);
            ((dynamic)dataField).NumberFormat = "$#,##0";
            RefreshExcelPivotTable(pivot);
            hiddenRegionItem = ((dynamic)regionField).PivotItems("West");
            ((dynamic)hiddenRegionItem).Visible = false;
            ((dynamic)pivot).TableStyle2 = "PivotStyleMedium4";
            RefreshExcelPivotTable(pivot);
            AutoFitExcelColumns(pivotSheet, "A:F");
        }
        finally
        {
            ReleaseComObject(dataField);
            ReleaseComObject(salesField);
            ReleaseComObject(categoryField);
            ReleaseComObject(hiddenRegionItem);
            ReleaseComObject(regionField);
            ReleaseComObject(pivot);
            ReleaseComObject(cache);
            ReleaseComObject(table);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void AddNativePivotGroupingShowValues(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? cache = null;
        object? pivot = null;
        object? monthField = null;
        object? bucketField = null;
        object? salesField = null;
        object? dataField = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot Buckets");
            SetExcelCellValue(pivotSheet, 1, 1, "Native PivotTable show-values-as coverage");
            cache = CreateWorksheetRangePivotCache(workbook, "'SalesData'!R1C1:R13C7");
            pivot = ((dynamic)cache).CreatePivotTable("'Pivot Buckets'!R3C1", "NativePivotGroupingShowValues");

            monthField = ((dynamic)pivot).PivotFields("Month");
            ((dynamic)monthField).Orientation = XlRowField;
            bucketField = ((dynamic)pivot).PivotFields("QuantityBucket");
            ((dynamic)bucketField).Orientation = XlColumnField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            dataField = ((dynamic)pivot).AddDataField(salesField, "% of Sales", XlSum);
            ((dynamic)dataField).NumberFormat = "0.0%";
            ((dynamic)dataField).Calculation = XlPercentOfGrandTotal;
            ((dynamic)pivot).TableStyle2 = "PivotStyleLight16";
            RefreshExcelPivotTable(pivot);
            AutoFitExcelColumns(pivotSheet, "A:F");
        }
        finally
        {
            ReleaseComObject(dataField);
            ReleaseComObject(salesField);
            ReleaseComObject(bucketField);
            ReleaseComObject(monthField);
            ReleaseComObject(pivot);
            ReleaseComObject(cache);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void AddNativePivotMultiplePivotsOneCache(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? cache = null;
        object? pivot1 = null;
        object? pivot2 = null;
        object? regionField = null;
        object? categoryField = null;
        object? salesField1 = null;
        object? salesField2 = null;
        object? dataField1 = null;
        object? dataField2 = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot Shared Cache");
            SetExcelCellValue(pivotSheet, 1, 1, "Multiple native PivotTables sharing one cache");
            cache = CreateWorksheetRangePivotCache(workbook, "'SalesData'!R1C1:R13C7");
            pivot1 = ((dynamic)cache).CreatePivotTable("'Pivot Shared Cache'!R3C1", "NativePivotSharedCacheA");
            pivot2 = ((dynamic)cache).CreatePivotTable("'Pivot Shared Cache'!R3C6", "NativePivotSharedCacheB");

            regionField = ((dynamic)pivot1).PivotFields("Region");
            ((dynamic)regionField).Orientation = XlRowField;
            salesField1 = ((dynamic)pivot1).PivotFields("Sales");
            dataField1 = ((dynamic)pivot1).AddDataField(salesField1, "Average Sales", XlAverage);
            ((dynamic)dataField1).NumberFormat = "$#,##0";
            ((dynamic)pivot1).TableStyle2 = "PivotStyleMedium2";

            categoryField = ((dynamic)pivot2).PivotFields("Category");
            ((dynamic)categoryField).Orientation = XlRowField;
            salesField2 = ((dynamic)pivot2).PivotFields("Sales");
            dataField2 = ((dynamic)pivot2).AddDataField(salesField2, "Count of Sales", XlCount);
            ((dynamic)pivot2).TableStyle2 = "PivotStyleDark3";
            RefreshExcelPivotTable(pivot1);
            RefreshExcelPivotTable(pivot2);
            AutoFitExcelColumns(pivotSheet, "A:K");
        }
        finally
        {
            ReleaseComObject(dataField2);
            ReleaseComObject(salesField2);
            ReleaseComObject(categoryField);
            ReleaseComObject(dataField1);
            ReleaseComObject(salesField1);
            ReleaseComObject(regionField);
            ReleaseComObject(pivot2);
            ReleaseComObject(pivot1);
            ReleaseComObject(cache);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void AddNativePivotReportFilters(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? cache = null;
        object? pivot = null;
        object? regionField = null;
        object? channelField = null;
        object? hiddenChannelItem = null;
        object? categoryField = null;
        object? monthField = null;
        object? salesField = null;
        object? dataField = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot Report Filters");
            SetExcelCellValue(pivotSheet, 1, 1, "Native PivotTable report filters / page fields");
            cache = CreateWorksheetRangePivotCache(workbook, "'SalesData'!R1C1:R13C7");
            pivot = ((dynamic)cache).CreatePivotTable("'Pivot Report Filters'!R3C1", "NativePivotReportFilters");

            regionField = ((dynamic)pivot).PivotFields("Region");
            ((dynamic)regionField).Orientation = XlPageField;
            ((dynamic)regionField).CurrentPage = "North";
            channelField = ((dynamic)pivot).PivotFields("Channel");
            ((dynamic)channelField).Orientation = XlPageField;
            ((dynamic)channelField).EnableMultiplePageItems = true;
            hiddenChannelItem = ((dynamic)channelField).PivotItems("Partner");
            ((dynamic)hiddenChannelItem).Visible = false;
            categoryField = ((dynamic)pivot).PivotFields("Category");
            ((dynamic)categoryField).Orientation = XlRowField;
            monthField = ((dynamic)pivot).PivotFields("Month");
            ((dynamic)monthField).Orientation = XlColumnField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            dataField = ((dynamic)pivot).AddDataField(salesField, "Sum of Sales", XlSum);
            ((dynamic)dataField).NumberFormat = "$#,##0";
            ((dynamic)pivot).PageFieldOrder = XlOverThenDown;
            ((dynamic)pivot).PageFieldWrapCount = 2;
            ((dynamic)pivot).TableStyle2 = "PivotStyleMedium5";
            RefreshExcelPivotTable(pivot);
            AutoFitExcelColumns(pivotSheet, "A:F");
        }
        finally
        {
            ReleaseComObject(dataField);
            ReleaseComObject(salesField);
            ReleaseComObject(monthField);
            ReleaseComObject(categoryField);
            ReleaseComObject(hiddenChannelItem);
            ReleaseComObject(channelField);
            ReleaseComObject(regionField);
            ReleaseComObject(pivot);
            ReleaseComObject(cache);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void AddNativePivotSlicerTimeline(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? cache = null;
        object? pivot = null;
        object? regionField = null;
        object? monthField = null;
        object? salesField = null;
        object? dataField = null;
        object? slicerCaches = null;
        object? regionSlicerCache = null;
        object? regionSlicerItems = null;
        object? southSlicerItem = null;
        object? regionSlicers = null;
        object? regionSlicer = null;
        object? timelineCache = null;
        object? timelineState = null;
        object? timelineSlicers = null;
        object? timeline = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot Slicer Timeline");
            SetExcelCellValue(pivotSheet, 1, 1, "Native PivotTable slicer / timeline");
            cache = CreateWorksheetRangePivotCache(workbook, "'SalesData'!R1C1:R13C7");
            pivot = ((dynamic)cache).CreatePivotTable("'Pivot Slicer Timeline'!R3C1", "NativePivotSlicerTimeline");

            regionField = ((dynamic)pivot).PivotFields("Region");
            ((dynamic)regionField).Orientation = XlRowField;
            monthField = ((dynamic)pivot).PivotFields("Month");
            ((dynamic)monthField).Orientation = XlColumnField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            dataField = ((dynamic)pivot).AddDataField(salesField, "Sum of Sales", XlSum);
            ((dynamic)dataField).NumberFormat = "$#,##0";
            ((dynamic)pivot).TableStyle2 = "PivotStyleMedium4";
            RefreshExcelPivotTable(pivot);

            slicerCaches = ((dynamic)workbook).SlicerCaches;
            regionSlicerCache = ((dynamic)slicerCaches).Add2(pivot, "Region", "NativePivotRegionSlicer");
            ((dynamic)regionSlicerCache).CrossFilterType = 1;
            regionSlicerItems = ((dynamic)regionSlicerCache).SlicerItems;
            southSlicerItem = ((dynamic)regionSlicerItems).Item("South");
            ((dynamic)southSlicerItem).Selected = false;
            regionSlicers = ((dynamic)regionSlicerCache).Slicers;
            regionSlicer = ((dynamic)regionSlicers).Add(
                pivotSheet,
                Missing.Value,
                "NativePivotRegionSlicer",
                "Region",
                42,
                360,
                150,
                150);
            ((dynamic)regionSlicer).Style = "SlicerStyleLight2";
            ((dynamic)regionSlicer).NumberOfColumns = 1;

            timelineCache = ((dynamic)slicerCaches).Add2(pivot, "SaleDate", "NativePivotSaleDateTimeline", XlTimeline);
            timelineState = ((dynamic)timelineCache).TimelineState;
            ((dynamic)timelineState).SetFilterDateRange(new DateTime(2026, 2, 1), new DateTime(2026, 4, 30));
            timelineSlicers = ((dynamic)timelineCache).Slicers;
            timeline = ((dynamic)timelineSlicers).Add(
                pivotSheet,
                Missing.Value,
                "NativePivotSaleDateTimeline",
                "SaleDate",
                42,
                528,
                260,
                120);
            ((dynamic)timeline).Style = "TimeSlicerStyleLight2";

            RefreshExcelPivotTable(pivot);
            AutoFitExcelColumns(pivotSheet, "A:L");
        }
        finally
        {
            ReleaseComObject(timeline);
            ReleaseComObject(timelineSlicers);
            ReleaseComObject(timelineState);
            ReleaseComObject(timelineCache);
            ReleaseComObject(regionSlicer);
            ReleaseComObject(regionSlicers);
            ReleaseComObject(southSlicerItem);
            ReleaseComObject(regionSlicerItems);
            ReleaseComObject(regionSlicerCache);
            ReleaseComObject(slicerCaches);
            ReleaseComObject(dataField);
            ReleaseComObject(salesField);
            ReleaseComObject(monthField);
            ReleaseComObject(regionField);
            ReleaseComObject(pivot);
            ReleaseComObject(cache);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void AddNativePivotFiltersSorts(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? cache = null;
        object? pivot = null;
        object? regionField = null;
        object? categoryField = null;
        object? salesField = null;
        object? dataField = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot Sort Filter");
            SetExcelCellValue(pivotSheet, 1, 1, "Native PivotTable label filter, value filter, and sort");
            cache = CreateWorksheetRangePivotCache(workbook, "'SalesData'!R1C1:R13C7");
            pivot = ((dynamic)cache).CreatePivotTable("'Pivot Sort Filter'!R3C1", "NativePivotFiltersSorts");

            regionField = ((dynamic)pivot).PivotFields("Region");
            ((dynamic)regionField).Orientation = XlRowField;
            categoryField = ((dynamic)pivot).PivotFields("Category");
            ((dynamic)categoryField).Orientation = XlColumnField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            dataField = ((dynamic)pivot).AddDataField(salesField, "Sum of Sales", XlSum);
            ((dynamic)dataField).NumberFormat = "$#,##0";
            RefreshExcelPivotTable(pivot);

            ((dynamic)regionField).PivotFilters.Add(XlCaptionBeginsWith, Missing.Value, "N");
            ((dynamic)categoryField).PivotFilters.Add(XlValueIsGreaterThan, dataField, 2_500);
            ((dynamic)regionField).AutoSort(XlDescending, "Sum of Sales");
            ((dynamic)pivot).TableStyle2 = "PivotStyleMedium10";
            RefreshExcelPivotTable(pivot);
            AutoFitExcelColumns(pivotSheet, "A:F");
        }
        finally
        {
            ReleaseComObject(dataField);
            ReleaseComObject(salesField);
            ReleaseComObject(categoryField);
            ReleaseComObject(regionField);
            ReleaseComObject(pivot);
            ReleaseComObject(cache);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void AddNativePivotLayoutOptions(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? cache = null;
        object? pivot = null;
        object? regionField = null;
        object? channelField = null;
        object? categoryField = null;
        object? salesField = null;
        object? dataField = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot Layout");
            SetExcelCellValue(pivotSheet, 1, 1, "Native PivotTable layout and display options");
            cache = CreateWorksheetRangePivotCache(workbook, "'SalesData'!R1C1:R13C7");
            pivot = ((dynamic)cache).CreatePivotTable("'Pivot Layout'!R3C1", "NativePivotLayoutOptions");

            regionField = ((dynamic)pivot).PivotFields("Region");
            ((dynamic)regionField).Orientation = XlRowField;
            channelField = ((dynamic)pivot).PivotFields("Channel");
            ((dynamic)channelField).Orientation = XlRowField;
            categoryField = ((dynamic)pivot).PivotFields("Category");
            ((dynamic)categoryField).Orientation = XlColumnField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            dataField = ((dynamic)pivot).AddDataField(salesField, "Sum of Sales", XlSum);
            ((dynamic)dataField).NumberFormat = "$#,##0";
            RefreshExcelPivotTable(pivot);

            ((dynamic)pivot).RowAxisLayout(XlTabularRow);
            ((dynamic)pivot).ColumnGrand = false;
            ((dynamic)pivot).RowGrand = true;
            ((dynamic)pivot).DisplayFieldCaptions = false;
            ((dynamic)pivot).ShowTableStyleRowStripes = true;
            ((dynamic)pivot).ShowTableStyleColumnStripes = true;
            ((dynamic)regionField).RepeatLabels = true;
            ((dynamic)channelField).RepeatLabels = true;
            ((dynamic)regionField).Subtotals = CreateExcelBooleanArray(false);
            ((dynamic)channelField).Subtotals = CreateExcelBooleanArray(false);
            ((dynamic)pivot).RepeatAllLabels(XlRepeatLabels);
            ((dynamic)pivot).TableStyle2 = "PivotStyleMedium13";
            RefreshExcelPivotTable(pivot);
            AutoFitExcelColumns(pivotSheet, "A:G");
        }
        finally
        {
            ReleaseComObject(dataField);
            ReleaseComObject(salesField);
            ReleaseComObject(categoryField);
            ReleaseComObject(channelField);
            ReleaseComObject(regionField);
            ReleaseComObject(pivot);
            ReleaseComObject(cache);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void AddNativePivotDateGrouping(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? cache = null;
        object? pivot = null;
        object? saleDateField = null;
        object? salesField = null;
        object? dataField = null;
        object? application = null;
        object? groupingCell = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot Date Group");
            SetExcelCellValue(pivotSheet, 1, 1, "Native PivotTable true date grouping");
            cache = CreateWorksheetRangePivotCache(workbook, "'SalesData'!R1C1:R13C7");
            pivot = ((dynamic)cache).CreatePivotTable("'Pivot Date Group'!R3C1", "NativePivotDateGrouping");

            saleDateField = ((dynamic)pivot).PivotFields("SaleDate");
            ((dynamic)saleDateField).Orientation = XlRowField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            dataField = ((dynamic)pivot).AddDataField(salesField, "Sum of Sales", XlSum);
            ((dynamic)dataField).NumberFormat = "$#,##0";
            ((dynamic)pivot).TableStyle2 = "PivotStyleMedium6";
            RefreshExcelPivotTable(pivot);

            application = ((dynamic)workbook).Application;
            ((dynamic)pivotSheet).Activate();
            groupingCell = ((dynamic)pivotSheet).Range("A4");
            ((dynamic)groupingCell).Select();
            ((dynamic)application).Selection.Group(true, true, Missing.Value, CreateExcelDatePeriodsArray(months: true, years: true));

            RefreshExcelPivotTable(pivot);
            AutoFitExcelColumns(pivotSheet, "A:D");
        }
        finally
        {
            ReleaseComObject(groupingCell);
            ReleaseComObject(application);
            ReleaseComObject(dataField);
            ReleaseComObject(salesField);
            ReleaseComObject(saleDateField);
            ReleaseComObject(pivot);
            ReleaseComObject(cache);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void AddNativePivotCalculatedFieldItem(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? cache = null;
        object? pivot = null;
        object? regionField = null;
        object? categoryField = null;
        object? salesField = null;
        object? calculatedFields = null;
        object? calculatedField = null;
        object? calculatedItems = null;
        object? calculatedItem = null;
        object? salesDataField = null;
        object? calculatedDataField = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot Calculations");
            SetExcelCellValue(pivotSheet, 1, 1, "Native PivotTable calculated field and item");
            cache = CreateWorksheetRangePivotCache(workbook, "'SalesData'!R1C1:R13C7");
            pivot = ((dynamic)cache).CreatePivotTable("'Pivot Calculations'!R3C1", "NativePivotCalculatedFieldItem");

            regionField = ((dynamic)pivot).PivotFields("Region");
            ((dynamic)regionField).Orientation = XlRowField;
            categoryField = ((dynamic)pivot).PivotFields("Category");
            ((dynamic)categoryField).Orientation = XlColumnField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            salesDataField = ((dynamic)pivot).AddDataField(salesField, "Sum of Sales", XlSum);
            ((dynamic)salesDataField).NumberFormat = "$#,##0";
            RefreshExcelPivotTable(pivot);

            calculatedFields = ((dynamic)pivot).CalculatedFields();
            calculatedField = ((dynamic)calculatedFields).Add("Sales Bonus", "=Sales*0.10");
            calculatedDataField = ((dynamic)pivot).AddDataField(calculatedField, "Sum of Sales Bonus", XlSum);
            ((dynamic)calculatedDataField).NumberFormat = "$#,##0";

            calculatedItems = ((dynamic)regionField).CalculatedItems();
            calculatedItem = ((dynamic)calculatedItems).Add("North South", "=North+South");
            ((dynamic)pivot).TableStyle2 = "PivotStyleMedium7";
            RefreshExcelPivotTable(pivot);
            AutoFitExcelColumns(pivotSheet, "A:H");
        }
        finally
        {
            ReleaseComObject(calculatedDataField);
            ReleaseComObject(salesDataField);
            ReleaseComObject(calculatedItem);
            ReleaseComObject(calculatedItems);
            ReleaseComObject(calculatedField);
            ReleaseComObject(calculatedFields);
            ReleaseComObject(salesField);
            ReleaseComObject(categoryField);
            ReleaseComObject(regionField);
            ReleaseComObject(pivot);
            ReleaseComObject(cache);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void AddNativePivotShowItemsWithNoData(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? cache = null;
        object? pivot = null;
        object? regionField = null;
        object? categoryField = null;
        object? salesField = null;
        object? dataField = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot No Data Items");
            SetExcelCellValue(pivotSheet, 1, 1, "Native PivotTable show items with no data");
            cache = CreateWorksheetRangePivotCache(workbook, "'SalesData'!R1C1:R13C7");
            pivot = ((dynamic)cache).CreatePivotTable("'Pivot No Data Items'!R3C1", "NativePivotShowItemsWithNoData");

            regionField = ((dynamic)pivot).PivotFields("Region");
            ((dynamic)regionField).Orientation = XlRowField;
            categoryField = ((dynamic)pivot).PivotFields("Category");
            ((dynamic)categoryField).Orientation = XlRowField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            dataField = ((dynamic)pivot).AddDataField(salesField, "Sum of Sales", XlSum);
            ((dynamic)dataField).NumberFormat = "$#,##0";
            RefreshExcelPivotTable(pivot);

            ((dynamic)categoryField).ShowAllItems = true;
            ((dynamic)pivot).RowAxisLayout(XlOutlineRow);
            ((dynamic)pivot).TableStyle2 = "PivotStyleMedium14";
            RefreshExcelPivotTable(pivot);
            AutoFitExcelColumns(pivotSheet, "A:D");
        }
        finally
        {
            ReleaseComObject(dataField);
            ReleaseComObject(salesField);
            ReleaseComObject(categoryField);
            ReleaseComObject(regionField);
            ReleaseComObject(pivot);
            ReleaseComObject(cache);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void AddNativePivotLayoutMatrix(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? cache = null;
        object? compactPivot = null;
        object? outlinePivot = null;
        object? tabularPivot = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot Layout Matrix");
            SetExcelCellValue(pivotSheet, 1, 1, "Native PivotTable compact / outline / tabular layout matrix");
            cache = CreateWorksheetRangePivotCache(workbook, "'SalesData'!R1C1:R13C7");
            compactPivot = ((dynamic)cache).CreatePivotTable("'Pivot Layout Matrix'!R3C1", "NativePivotCompactLayout");
            outlinePivot = ((dynamic)cache).CreatePivotTable("'Pivot Layout Matrix'!R3C5", "NativePivotOutlineLayout");
            tabularPivot = ((dynamic)cache).CreatePivotTable("'Pivot Layout Matrix'!R3C9", "NativePivotTabularLayout");

            ConfigureLayoutMatrixPivot(compactPivot, XlCompactRow, "Compact Layout", "PivotStyleLight9");
            ConfigureLayoutMatrixPivot(outlinePivot, XlOutlineRow, "Outline Layout", "PivotStyleMedium3");
            ConfigureLayoutMatrixPivot(tabularPivot, XlTabularRow, "Tabular Layout", "PivotStyleMedium9");

            RefreshExcelPivotTable(compactPivot);
            RefreshExcelPivotTable(outlinePivot);
            RefreshExcelPivotTable(tabularPivot);
            AutoFitExcelColumns(pivotSheet, "A:L");
        }
        finally
        {
            ReleaseComObject(tabularPivot);
            ReleaseComObject(outlinePivot);
            ReleaseComObject(compactPivot);
            ReleaseComObject(cache);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void AddNativePivotSubtotalGrandTotals(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? cache = null;
        object? pivot = null;
        object? regionField = null;
        object? channelField = null;
        object? categoryField = null;
        object? salesField = null;
        object? dataField = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot Totals");
            SetExcelCellValue(pivotSheet, 1, 1, "Native PivotTable subtotal and grand-total permutations");
            cache = CreateWorksheetRangePivotCache(workbook, "'SalesData'!R1C1:R13C7");
            pivot = ((dynamic)cache).CreatePivotTable("'Pivot Totals'!R3C1", "NativePivotSubtotalGrandTotals");

            regionField = ((dynamic)pivot).PivotFields("Region");
            ((dynamic)regionField).Orientation = XlRowField;
            channelField = ((dynamic)pivot).PivotFields("Channel");
            ((dynamic)channelField).Orientation = XlRowField;
            categoryField = ((dynamic)pivot).PivotFields("Category");
            ((dynamic)categoryField).Orientation = XlColumnField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            dataField = ((dynamic)pivot).AddDataField(salesField, "Sum of Sales", XlSum);
            ((dynamic)dataField).NumberFormat = "$#,##0";
            RefreshExcelPivotTable(pivot);

            ((dynamic)pivot).RowAxisLayout(XlOutlineRow);
            ((dynamic)pivot).RowGrand = false;
            ((dynamic)pivot).ColumnGrand = true;
            ((dynamic)regionField).LayoutSubtotalLocation = XlAtBottom;
            ((dynamic)channelField).Subtotals = CreateExcelBooleanArray(false);
            ((dynamic)pivot).TableStyle2 = "PivotStyleMedium12";
            RefreshExcelPivotTable(pivot);
            AutoFitExcelColumns(pivotSheet, "A:H");
        }
        finally
        {
            ReleaseComObject(dataField);
            ReleaseComObject(salesField);
            ReleaseComObject(categoryField);
            ReleaseComObject(channelField);
            ReleaseComObject(regionField);
            ReleaseComObject(pivot);
            ReleaseComObject(cache);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void AddNativePivotNamedRangeSource(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? cache = null;
        object? pivot = null;
        object? regionField = null;
        object? channelField = null;
        object? salesField = null;
        object? dataField = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot Named Range");
            SetExcelCellValue(pivotSheet, 1, 1, "Native PivotTable named-range source");
            cache = CreateWorksheetRangePivotCache(workbook, "NativeSalesRange");
            pivot = ((dynamic)cache).CreatePivotTable("'Pivot Named Range'!R3C1", "NativePivotNamedRangeSource");

            regionField = ((dynamic)pivot).PivotFields("Region");
            ((dynamic)regionField).Orientation = XlRowField;
            channelField = ((dynamic)pivot).PivotFields("Channel");
            ((dynamic)channelField).Orientation = XlColumnField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            dataField = ((dynamic)pivot).AddDataField(salesField, "Sum of Sales", XlSum);
            ((dynamic)dataField).NumberFormat = "$#,##0";
            ((dynamic)pivot).TableStyle2 = "PivotStyleMedium8";
            RefreshExcelPivotTable(pivot);
            AutoFitExcelColumns(pivotSheet, "A:F");
        }
        finally
        {
            ReleaseComObject(dataField);
            ReleaseComObject(salesField);
            ReleaseComObject(channelField);
            ReleaseComObject(regionField);
            ReleaseComObject(pivot);
            ReleaseComObject(cache);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void AddNativePivotShowValuesAsVariants(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? cache = null;
        object? percentPivot = null;
        object? runningPivot = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot Value Modes");
            SetExcelCellValue(pivotSheet, 1, 1, "Native PivotTable Show Values As variants");
            cache = CreateWorksheetRangePivotCache(workbook, "'SalesData'!R1C1:R13C7");
            percentPivot = ((dynamic)cache).CreatePivotTable("'Pivot Value Modes'!R3C1", "NativePivotPercentOfColumn");
            runningPivot = ((dynamic)cache).CreatePivotTable("'Pivot Value Modes'!R3C7", "NativePivotRunningTotal");

            ConfigurePercentOfColumnPivot(percentPivot);
            ConfigureRunningTotalPivot(runningPivot);

            RefreshExcelPivotTable(percentPivot);
            RefreshExcelPivotTable(runningPivot);
            AutoFitExcelColumns(pivotSheet, "A:L");
        }
        finally
        {
            ReleaseComObject(runningPivot);
            ReleaseComObject(percentPivot);
            ReleaseComObject(cache);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void AddNativePivotChromeStyleFlags(object workbook, object sourceWorksheet)
    {
        object? pivotSheet = null;
        object? cache = null;
        object? pivot = null;
        object? regionField = null;
        object? categoryField = null;
        object? salesField = null;
        object? dataField = null;
        try
        {
            pivotSheet = AddWorksheetAfter(workbook, sourceWorksheet, "Pivot Chrome Style");
            SetExcelCellValue(pivotSheet, 1, 1, "Native PivotTable field chrome and style flags");
            cache = CreateWorksheetRangePivotCache(workbook, "'SalesData'!R1C1:R13C7");
            pivot = ((dynamic)cache).CreatePivotTable("'Pivot Chrome Style'!R3C1", "NativePivotChromeStyleFlags");

            regionField = ((dynamic)pivot).PivotFields("Region");
            ((dynamic)regionField).Orientation = XlRowField;
            categoryField = ((dynamic)pivot).PivotFields("Category");
            ((dynamic)categoryField).Orientation = XlColumnField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            dataField = ((dynamic)pivot).AddDataField(salesField, "Sum of Sales", XlSum);
            ((dynamic)dataField).NumberFormat = "$#,##0";
            RefreshExcelPivotTable(pivot);

            ((dynamic)pivot).DisplayFieldCaptions = true;
            ((dynamic)pivot).ShowDrillIndicators = false;
            ((dynamic)pivot).ShowTableStyleRowHeaders = false;
            ((dynamic)pivot).ShowTableStyleColumnHeaders = true;
            ((dynamic)pivot).ShowTableStyleRowStripes = true;
            ((dynamic)pivot).ShowTableStyleColumnStripes = false;
            ((dynamic)pivot).TableStyle2 = "PivotStyleLight14";
            RefreshExcelPivotTable(pivot);
            AutoFitExcelColumns(pivotSheet, "A:F");
        }
        finally
        {
            ReleaseComObject(dataField);
            ReleaseComObject(salesField);
            ReleaseComObject(categoryField);
            ReleaseComObject(regionField);
            ReleaseComObject(pivot);
            ReleaseComObject(cache);
            ReleaseComObject(pivotSheet);
        }
    }

    private static void ConfigureLayoutMatrixPivot(object pivot, int rowAxisLayout, string dataFieldCaption, string style)
    {
        object? regionField = null;
        object? categoryField = null;
        object? salesField = null;
        object? dataField = null;
        try
        {
            regionField = ((dynamic)pivot).PivotFields("Region");
            ((dynamic)regionField).Orientation = XlRowField;
            categoryField = ((dynamic)pivot).PivotFields("Category");
            ((dynamic)categoryField).Orientation = XlRowField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            dataField = ((dynamic)pivot).AddDataField(salesField, dataFieldCaption, XlSum);
            ((dynamic)dataField).NumberFormat = "$#,##0";
            RefreshExcelPivotTable(pivot);
            ((dynamic)pivot).RowAxisLayout(rowAxisLayout);
            ((dynamic)pivot).TableStyle2 = style;
        }
        finally
        {
            ReleaseComObject(dataField);
            ReleaseComObject(salesField);
            ReleaseComObject(categoryField);
            ReleaseComObject(regionField);
        }
    }

    private static void ConfigurePercentOfColumnPivot(object pivot)
    {
        object? regionField = null;
        object? categoryField = null;
        object? salesField = null;
        object? dataField = null;
        try
        {
            regionField = ((dynamic)pivot).PivotFields("Region");
            ((dynamic)regionField).Orientation = XlRowField;
            categoryField = ((dynamic)pivot).PivotFields("Category");
            ((dynamic)categoryField).Orientation = XlColumnField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            dataField = ((dynamic)pivot).AddDataField(salesField, "% Column Sales", XlSum);
            ((dynamic)dataField).NumberFormat = "0.0%";
            ((dynamic)dataField).Calculation = XlPercentOfColumn;
            ((dynamic)pivot).TableStyle2 = "PivotStyleMedium6";
        }
        finally
        {
            ReleaseComObject(dataField);
            ReleaseComObject(salesField);
            ReleaseComObject(categoryField);
            ReleaseComObject(regionField);
        }
    }

    private static void ConfigureRunningTotalPivot(object pivot)
    {
        object? monthField = null;
        object? salesField = null;
        object? dataField = null;
        try
        {
            monthField = ((dynamic)pivot).PivotFields("Month");
            ((dynamic)monthField).Orientation = XlRowField;
            salesField = ((dynamic)pivot).PivotFields("Sales");
            dataField = ((dynamic)pivot).AddDataField(salesField, "Running Total Sales", XlSum);
            ((dynamic)dataField).NumberFormat = "$#,##0";
            ((dynamic)dataField).Calculation = XlRunningTotal;
            ((dynamic)dataField).BaseField = "Month";
            ((dynamic)pivot).TableStyle2 = "PivotStyleMedium10";
        }
        finally
        {
            ReleaseComObject(dataField);
            ReleaseComObject(salesField);
            ReleaseComObject(monthField);
        }
    }

    private static object[] CreateExcelBooleanArray(bool value)
    {
        var values = new object[12];
        for (var index = 0; index < values.Length; index++)
            values[index] = value;
        return values;
    }

    private static object[] CreateExcelDatePeriodsArray(
        bool seconds = false,
        bool minutes = false,
        bool hours = false,
        bool days = false,
        bool months = false,
        bool quarters = false,
        bool years = false) =>
    [
        seconds,
        minutes,
        hours,
        days,
        months,
        quarters,
        years
    ];

    private static object AddWorksheetAfter(object workbook, object afterWorksheet, string name)
    {
        var sheet = ((dynamic)workbook).Worksheets.Add(Missing.Value, afterWorksheet);
        ((dynamic)sheet).Name = name;
        return sheet;
    }

    private static object CreateWorksheetRangePivotCache(object workbook, string sourceData) =>
        ((dynamic)workbook).PivotCaches().Create(XlDatabase, sourceData);

    private static void PatchNativePivotShowItemsWithNoDataFlags(string workbookPath)
    {
        using var archive = ZipFile.Open(workbookPath, ZipArchiveMode.Update);
        var entry = archive.GetEntry("xl/pivotTables/pivotTable1.xml")
            ?? throw new InvalidOperationException("Expected native PivotTable definition part was not found.");

        XDocument document;
        using (var stream = entry.Open())
        {
            document = XDocument.Load(stream);
        }

        if (document.Root is null)
            throw new InvalidOperationException("Native PivotTable definition part is empty.");

        document.Root.SetAttributeValue("showEmptyRow", "1");

        entry.Delete();
        var replacement = archive.CreateEntry("xl/pivotTables/pivotTable1.xml");
        using var output = replacement.Open();
        document.Save(output);
    }

    private static void RefreshExcelPivotTable(object pivotTable)
    {
        try
        {
            ((dynamic)pivotTable).RefreshTable();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Excel failed to refresh generated native PivotTable fixture.", ex);
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

    // =========================================================================
    // Comment / Note corpus fixtures
    // =========================================================================

    /// <summary>
    /// Returns the output paths for all six comment corpus fixtures.
    /// The files that contain only legacy (COM-authored) notes are generated
    /// via Excel COM; the files that require threaded comments are authored via
    /// the FreeX model API and saved through <see cref="XlsxFileAdapter"/>.
    /// </summary>
    public static IReadOnlyList<string> GetExcelCommentCorpusFixturePaths(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        return
        [
            Path.Combine(outputDirectory, "Excel_native_comment_single_note_001.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_comment_single_note_shown_002.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_comment_multiple_notes_003.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_comment_threaded_single_004.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_comment_threaded_replies_005.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_comment_mixed_006.xlsx"),
        ];
    }

    /// <summary>
    /// Per-file dispatch called from <see cref="GenerateExcelAuthoredFixture"/> for filenames
    /// that start with <c>Excel_native_comment_</c>.
    /// </summary>
    private static void GenerateExcelNativeCommentCorpusFixture(dynamic workbooks, string outputPath, string fileName)
    {
        if (fileName.StartsWith("Excel_native_comment_single_note_001", StringComparison.OrdinalIgnoreCase))
            GenerateCommentFixture_SingleNote(workbooks, outputPath);
        else if (fileName.StartsWith("Excel_native_comment_single_note_shown_002", StringComparison.OrdinalIgnoreCase))
            GenerateCommentFixture_SingleNoteShown(workbooks, outputPath);
        else if (fileName.StartsWith("Excel_native_comment_multiple_notes_003", StringComparison.OrdinalIgnoreCase))
            GenerateCommentFixture_MultipleNotes(workbooks, outputPath);
        else if (fileName.StartsWith("Excel_native_comment_threaded_single_004", StringComparison.OrdinalIgnoreCase))
            GenerateCommentFixture_ThreadedSingle(outputPath);
        else if (fileName.StartsWith("Excel_native_comment_threaded_replies_005", StringComparison.OrdinalIgnoreCase))
            GenerateCommentFixture_ThreadedReplies(outputPath);
        else if (fileName.StartsWith("Excel_native_comment_mixed_006", StringComparison.OrdinalIgnoreCase))
            GenerateCommentFixture_Mixed(workbooks, outputPath);
        else
            throw new ArgumentException($"Unknown comment corpus fixture: {fileName}");
    }

    // -------------------------------------------------------------------------
    // case 001 — single legacy note, hidden box (default)
    // -------------------------------------------------------------------------
    private static void GenerateCommentFixture_SingleNote(dynamic workbooks, string outputPath)
    {
        if (File.Exists(outputPath)) File.Delete(outputPath);
        object? workbook = null;
        object? worksheet = null;
        try
        {
            workbook = workbooks.Add();
            worksheet = ((dynamic)workbook).Worksheets[1];
            ((dynamic)worksheet).Name = "Notes";

            // Anchor data so the note cell is visible
            SetExcelCellValue(worksheet, 1, 1, "Item");
            SetExcelCellValue(worksheet, 1, 2, "Value");
            SetExcelCellValue(worksheet, 2, 1, "Alpha");
            SetExcelCellValue(worksheet, 2, 2, 42.0);
            SetExcelCellValue(worksheet, 3, 1, "Beta");
            SetExcelCellValue(worksheet, 3, 2, 88.0);

            // Legacy note on C3 (hidden box — default)
            AddExcelComment(worksheet, "C3", "Single hidden note on C3.");

            SaveExcelWorkbook(workbook, outputPath);
        }
        finally
        {
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 002 — single legacy note with box pinned visible
    // -------------------------------------------------------------------------
    private static void GenerateCommentFixture_SingleNoteShown(dynamic workbooks, string outputPath)
    {
        if (File.Exists(outputPath)) File.Delete(outputPath);
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? comment = null;
        try
        {
            workbook = workbooks.Add();
            worksheet = ((dynamic)workbook).Worksheets[1];
            ((dynamic)worksheet).Name = "Notes";

            SetExcelCellValue(worksheet, 1, 1, "Item");
            SetExcelCellValue(worksheet, 1, 2, "Value");
            SetExcelCellValue(worksheet, 2, 1, "Alpha");
            SetExcelCellValue(worksheet, 2, 2, 42.0);
            SetExcelCellValue(worksheet, 3, 1, "Beta");
            SetExcelCellValue(worksheet, 3, 2, 88.0);

            // Author note on C3 with Visible = true (pinned open)
            range = ((dynamic)worksheet).Range("C3");
            comment = ((dynamic)range).AddComment("Pinned note — bold header\nBody text on second line.");
            // Make the box visible (pinned)
            ((dynamic)comment).Visible = true;
            // Apply bold to first 17 chars ("Pinned note — bold")
            try
            {
                var chars = ((dynamic)comment).Text();
                ((dynamic)comment).Shape.TextFrame.Characters(1, 11).Font.Bold = true;
            }
            catch { /* formatting is best-effort */ }

            SaveExcelWorkbook(workbook, outputPath);
        }
        finally
        {
            ReleaseComObject(comment);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 003 — multiple notes on scattered cells
    // -------------------------------------------------------------------------
    private static void GenerateCommentFixture_MultipleNotes(dynamic workbooks, string outputPath)
    {
        if (File.Exists(outputPath)) File.Delete(outputPath);
        object? workbook = null;
        object? worksheet = null;
        try
        {
            workbook = workbooks.Add();
            worksheet = ((dynamic)workbook).Worksheets[1];
            ((dynamic)worksheet).Name = "Notes";

            SetExcelCellValue(worksheet, 1, 1, "Region");
            SetExcelCellValue(worksheet, 1, 2, "Q1");
            SetExcelCellValue(worksheet, 1, 3, "Q2");
            SetExcelCellValue(worksheet, 1, 4, "Total");

            SetExcelCellValue(worksheet, 2, 1, "North");
            SetExcelCellValue(worksheet, 2, 2, 100.0);
            SetExcelCellValue(worksheet, 2, 3, 120.0);
            SetExcelCellValue(worksheet, 2, 4, 220.0);

            SetExcelCellValue(worksheet, 4, 1, "South");
            SetExcelCellValue(worksheet, 4, 2, 90.0);
            SetExcelCellValue(worksheet, 4, 3, 85.0);
            SetExcelCellValue(worksheet, 4, 4, 175.0);

            SetExcelCellValue(worksheet, 6, 1, "East");
            SetExcelCellValue(worksheet, 7, 1, "West");

            // Notes on four scattered cells
            AddExcelComment(worksheet, "B2", "Note on B2: Q1 North figure.");
            AddExcelComment(worksheet, "D2", "Note on D2: Total row check needed.");
            AddExcelComment(worksheet, "B4", "Note on B4: Q1 South — verify source.");
            AddExcelComment(worksheet, "A6", "Note on A6: East data pending.");

            SaveExcelWorkbook(workbook, outputPath);
        }
        finally
        {
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 004 — single threaded comment (FreeX model API)
    // -------------------------------------------------------------------------
    private static void GenerateCommentFixture_ThreadedSingle(string outputPath)
    {
        if (File.Exists(outputPath)) File.Delete(outputPath);

        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Threaded");

        // Populate a small visible grid
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new TextValue("Item") });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new Cell { Value = new TextValue("Value") });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell { Value = new TextValue("Alpha") });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new Cell { Value = new NumberValue(42) });
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new Cell { Value = new TextValue("Beta") });
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new Cell { Value = new NumberValue(88) });

        // Threaded comment on C3
        var address = new CellAddress(sheet.Id, 3, 3);
        sheet.ThreadedComments[address] = new ThreadedComment("Please review this value.", "FreeX")
        {
            CreatedAtUtc = new DateTimeOffset(2026, 6, 24, 9, 0, 0, TimeSpan.Zero),
        };

        SaveFreeXWorkbook(workbook, outputPath);
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 005 — threaded comment with replies (FreeX model API)
    // -------------------------------------------------------------------------
    private static void GenerateCommentFixture_ThreadedReplies(string outputPath)
    {
        if (File.Exists(outputPath)) File.Delete(outputPath);

        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Threaded");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell { Value = new TextValue("Category") });
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new Cell { Value = new TextValue("Amount") });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new Cell { Value = new TextValue("Alpha") });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new Cell { Value = new NumberValue(500) });
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new Cell { Value = new TextValue("Beta") });
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new Cell { Value = new NumberValue(300) });
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new Cell { Value = new TextValue("Gamma") });
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new Cell { Value = new NumberValue(200) });

        var t0 = new DateTimeOffset(2026, 6, 24, 8, 0, 0, TimeSpan.Zero);

        // Threaded comment with two replies on B3
        var address = new CellAddress(sheet.Id, 3, 2);
        sheet.ThreadedComments[address] = new ThreadedComment("Is this figure correct?", "Quinn")
        {
            CreatedAtUtc = t0,
            ModifiedAtUtc = t0,
            Replies =
            [
                new CommentReply("Looks right to me.", "FreeX")
                {
                    CreatedAtUtc = t0.AddMinutes(10),
                    ModifiedAtUtc = t0.AddMinutes(10),
                },
                new CommentReply("Confirmed — resolving.", "Quinn")
                {
                    CreatedAtUtc = t0.AddMinutes(25),
                    ModifiedAtUtc = t0.AddMinutes(25),
                },
            ],
            IsResolved = true,
        };

        // Second unresolved thread on D5
        var address2 = new CellAddress(sheet.Id, 5, 4);
        sheet.ThreadedComments[address2] = new ThreadedComment("Double-check data source.", "FreeX")
        {
            CreatedAtUtc = t0.AddHours(1),
            Replies =
            [
                new CommentReply("Checked — source is correct.", "Quinn")
                {
                    CreatedAtUtc = t0.AddHours(1).AddMinutes(5),
                },
            ],
        };

        SaveFreeXWorkbook(workbook, outputPath);
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 006 — mixed: one legacy note + one threaded comment
    // -------------------------------------------------------------------------
    private static void GenerateCommentFixture_Mixed(dynamic workbooks, string outputPath)
    {
        if (File.Exists(outputPath)) File.Delete(outputPath);

        // Step 1 — author the legacy note via Excel COM and save an intermediate file.
        var tempPath = outputPath + ".tmp.xlsx";
        object? workbook = null;
        object? worksheet = null;
        try
        {
            workbook = workbooks.Add();
            worksheet = ((dynamic)workbook).Worksheets[1];
            ((dynamic)worksheet).Name = "Mixed";

            SetExcelCellValue(worksheet, 1, 1, "Label");
            SetExcelCellValue(worksheet, 1, 2, "Score");
            SetExcelCellValue(worksheet, 2, 1, "Alpha");
            SetExcelCellValue(worksheet, 2, 2, 75.0);
            SetExcelCellValue(worksheet, 3, 1, "Beta");
            SetExcelCellValue(worksheet, 3, 2, 90.0);
            SetExcelCellValue(worksheet, 5, 1, "Delta");
            SetExcelCellValue(worksheet, 5, 2, 60.0);

            // Legacy note on B2
            AddExcelComment(worksheet, "B2", "Legacy note on B2: verify score.");

            SaveExcelWorkbook(workbook, tempPath);
        }
        finally
        {
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }

        // Step 2 — load through FreeX, add a threaded comment, save to final path.
        Workbook freeXWorkbook;
        using (var fs = File.OpenRead(tempPath))
            freeXWorkbook = new XlsxFileAdapter().LoadWithWarnings(fs, inspectFeatures: false).Workbook;

        var mixedSheet = freeXWorkbook.Sheets[0];
        var threadAddress = new CellAddress(mixedSheet.Id, 5, 4);
        mixedSheet.ThreadedComments[threadAddress] = new ThreadedComment("Threaded comment on D5.", "FreeX")
        {
            CreatedAtUtc = new DateTimeOffset(2026, 6, 24, 10, 0, 0, TimeSpan.Zero),
        };

        SaveFreeXWorkbook(freeXWorkbook, outputPath);
        File.Delete(tempPath);
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // Helpers shared by comment fixture generators
    // -------------------------------------------------------------------------

    private static void SaveExcelWorkbook(object workbook, string outputPath)
    {
        // COM requires Windows-style backslash paths; forward-slash paths get mangled.
        var windowsPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(windowsPath)!);
        ((dynamic)workbook).SaveAs(
            windowsPath,
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
    }

    private static void SafeCloseWorkbook(object? workbook)
    {
        if (workbook is null) return;
        try { ((dynamic)workbook).Close(false); } catch { }
    }

    private static void SaveFreeXWorkbook(Workbook workbook, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var fs = File.Create(outputPath);
        new XlsxFileAdapter().Save(workbook, fs);
    }

    // =========================================================================
    // Conditional Formatting corpus fixtures
    // =========================================================================

    // XlFormatConditionType enum values
    private const int XlColorScale = 3;
    private const int XlDataBar = 4;
    private const int XlIconSet = 6;
    private const int XlTop10 = 5;
    // XlDataBarFillType
    private const int XlDataBarFillGradient = 0;
    private const int XlDataBarFillSolid = 1;
    // XlDataBarBorderType
    private const int XlDataBarBorderNone = 0;
    private const int XlDataBarBorderSolid = 1;
    // XlIconSet enum
    private const int xl3Arrows = 1;
    private const int xl5Rating = 18;
    // XlConditionValueTypes
    private const int XlConditionValueLowestValue = 1;
    private const int XlConditionValueHighestValue = 2;
    private const int XlConditionValueNumber = 0;
    private const int XlConditionValuePercent = 3;
    private const int XlConditionValuePercentile = 4;
    // XlTop10
    private const int XlTop10Top = 1;
    private const int XlTop10Bottom = 2;
    // XlThemeColor
    private const int XlThemeColorAccent1 = 5;

    /// <summary>Returns the output paths for all nine CF corpus fixtures.</summary>
    public static IReadOnlyList<string> GetExcelCfCorpusFixturePaths(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        return
        [
            Path.Combine(outputDirectory, "Excel_native_cf_databars_pos_001.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_cf_databars_neg_002.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_cf_databars_solid_003.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_cf_colorscale3_004.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_cf_colorscale2_005.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_cf_iconset_arrows_006.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_cf_iconset_rating_007.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_cf_highlight_dxf_008.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_cf_top10_009.xlsx"),
        ];
    }

    /// <summary>Per-file dispatch for CF corpus fixtures.</summary>
    private static void GenerateExcelNativeCfCorpusFixture(dynamic workbooks, string outputPath, string fileName)
    {
        if (fileName.Contains("databars_pos_001", StringComparison.OrdinalIgnoreCase))
            GenerateCfFixture_DataBarsPos(workbooks, outputPath);
        else if (fileName.Contains("databars_neg_002", StringComparison.OrdinalIgnoreCase))
            GenerateCfFixture_DataBarsNeg(workbooks, outputPath);
        else if (fileName.Contains("databars_solid_003", StringComparison.OrdinalIgnoreCase))
            GenerateCfFixture_DataBarsSolid(workbooks, outputPath);
        else if (fileName.Contains("colorscale3_004", StringComparison.OrdinalIgnoreCase))
            GenerateCfFixture_ColorScale3(workbooks, outputPath);
        else if (fileName.Contains("colorscale2_005", StringComparison.OrdinalIgnoreCase))
            GenerateCfFixture_ColorScale2(workbooks, outputPath);
        else if (fileName.Contains("iconset_arrows_006", StringComparison.OrdinalIgnoreCase))
            GenerateCfFixture_IconSetArrows(workbooks, outputPath);
        else if (fileName.Contains("iconset_rating_007", StringComparison.OrdinalIgnoreCase))
            GenerateCfFixture_IconSetRating(workbooks, outputPath);
        else if (fileName.Contains("highlight_dxf_008", StringComparison.OrdinalIgnoreCase))
            GenerateCfFixture_HighlightDxf(workbooks, outputPath);
        else if (fileName.Contains("top10_009", StringComparison.OrdinalIgnoreCase))
            GenerateCfFixture_Top10(workbooks, outputPath);
        else
            throw new ArgumentException($"Unknown CF corpus fixture: {fileName}");
    }

    // -------------------------------------------------------------------------
    // Shared data population for CF fixtures
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes a header row + 11 numeric data rows (including negatives) into columns A and B.
    /// Returns the data range address for CF application (B2:B12).
    /// </summary>
    private static void PopulateCfData(object worksheet, bool includeNegatives)
    {
        SetExcelCellValue(worksheet, 1, 1, "Label");
        SetExcelCellValue(worksheet, 1, 2, "Value");

        double[] values = includeNegatives
            ? [10, -5, 30, -15, 45, 22, -8, 60, 5, -20, 75]
            : [10, 5, 30, 15, 45, 22, 8, 60, 5, 20, 75];

        var labels = new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K" };
        for (var i = 0; i < values.Length; i++)
        {
            SetExcelCellValue(worksheet, i + 2, 1, labels[i]);
            SetExcelCellValue(worksheet, i + 2, 2, values[i]);
        }
    }

    private static object OpenCfWorkbook(dynamic workbooks, string outputPath, string sheetName,
        out object worksheet)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        if (File.Exists(outputPath)) File.Delete(outputPath);

        var workbook = workbooks.Add();
        worksheet = ((dynamic)workbook).Worksheets[1];
        ((dynamic)worksheet).Name = sheetName;
        return workbook;
    }

    // -------------------------------------------------------------------------
    // case 001 — data bars, positive values only (gradient, default)
    // -------------------------------------------------------------------------
    private static void GenerateCfFixture_DataBarsPos(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? conditions = null;
        object? databar = null;
        try
        {
            workbook = OpenCfWorkbook(workbooks, outputPath, "DataBarsPos", out object ws);
            worksheet = ws;
            PopulateCfData(worksheet, includeNegatives: false);

            range = ((dynamic)worksheet).Range("B2:B12");
            conditions = ((dynamic)range).FormatConditions;
            databar = ((dynamic)conditions).AddDatabar();
            // Gradient fill is the default (XlDataBarFillGradient=0); no extra property needed.

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(databar);
            ReleaseComObject(conditions);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 002 — data bars with positive AND negative values (axis + red bar)
    // -------------------------------------------------------------------------
    private static void GenerateCfFixture_DataBarsNeg(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? conditions = null;
        object? databar = null;
        try
        {
            workbook = OpenCfWorkbook(workbooks, outputPath, "DataBarsNeg", out object ws);
            worksheet = ws;
            PopulateCfData(worksheet, includeNegatives: true);

            range = ((dynamic)worksheet).Range("B2:B12");
            conditions = ((dynamic)range).FormatConditions;
            databar = ((dynamic)conditions).AddDatabar();
            // Excel auto-draws axis + red negative bar when negatives exist.
            // NegativeBarFormat.ColorType and AxisColor can be accessed through
            // databar.NegativeBarFormat — leaving defaults so Excel picks red / midpoint axis.

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(databar);
            ReleaseComObject(conditions);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 003 — data bars solid fill + solid border
    // -------------------------------------------------------------------------
    private static void GenerateCfFixture_DataBarsSolid(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? conditions = null;
        object? databar = null;
        try
        {
            workbook = OpenCfWorkbook(workbooks, outputPath, "DataBarsSolid", out object ws);
            worksheet = ws;
            PopulateCfData(worksheet, includeNegatives: false);

            range = ((dynamic)worksheet).Range("B2:B12");
            conditions = ((dynamic)range).FormatConditions;
            databar = ((dynamic)conditions).AddDatabar();
            ((dynamic)databar).BarFillType = XlDataBarFillSolid;           // 1 = solid
            ((dynamic)databar).BarBorder.Type = XlDataBarBorderSolid;      // 1 = solid border
            ((dynamic)databar).BarBorder.Color.Color = ToOleColor(0, 112, 192); // blue border (FormatColor.Color)

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(databar);
            ReleaseComObject(conditions);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 004 — 3-colour scale (green → yellow → red)
    // -------------------------------------------------------------------------
    private static void GenerateCfFixture_ColorScale3(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? conditions = null;
        object? colorScale = null;
        try
        {
            workbook = OpenCfWorkbook(workbooks, outputPath, "ColorScale3", out object ws);
            worksheet = ws;
            PopulateCfData(worksheet, includeNegatives: false);

            range = ((dynamic)worksheet).Range("B2:B12");
            conditions = ((dynamic)range).FormatConditions;
            colorScale = ((dynamic)conditions).AddColorScale(3);

            // Point 1 (min) — green
            ((dynamic)colorScale).ColorScaleCriteria[1].Type = XlConditionValueLowestValue;
            ((dynamic)colorScale).ColorScaleCriteria[1].FormatColor.Color = ToOleColor(99, 190, 123);

            // Point 2 (midpoint) — yellow
            ((dynamic)colorScale).ColorScaleCriteria[2].Type = XlConditionValuePercent;
            ((dynamic)colorScale).ColorScaleCriteria[2].Value = 50;
            ((dynamic)colorScale).ColorScaleCriteria[2].FormatColor.Color = ToOleColor(255, 235, 132);

            // Point 3 (max) — red
            ((dynamic)colorScale).ColorScaleCriteria[3].Type = XlConditionValueHighestValue;
            ((dynamic)colorScale).ColorScaleCriteria[3].FormatColor.Color = ToOleColor(248, 105, 107);

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(colorScale);
            ReleaseComObject(conditions);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 005 — 2-colour scale (white → blue)
    // -------------------------------------------------------------------------
    private static void GenerateCfFixture_ColorScale2(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? conditions = null;
        object? colorScale = null;
        try
        {
            workbook = OpenCfWorkbook(workbooks, outputPath, "ColorScale2", out object ws);
            worksheet = ws;
            PopulateCfData(worksheet, includeNegatives: false);

            range = ((dynamic)worksheet).Range("B2:B12");
            conditions = ((dynamic)range).FormatConditions;
            colorScale = ((dynamic)conditions).AddColorScale(2);

            // Point 1 (min) — white
            ((dynamic)colorScale).ColorScaleCriteria[1].Type = XlConditionValueLowestValue;
            ((dynamic)colorScale).ColorScaleCriteria[1].FormatColor.Color = ToOleColor(255, 255, 255);

            // Point 2 (max) — blue
            ((dynamic)colorScale).ColorScaleCriteria[2].Type = XlConditionValueHighestValue;
            ((dynamic)colorScale).ColorScaleCriteria[2].FormatColor.Color = ToOleColor(31, 73, 125);

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(colorScale);
            ReleaseComObject(conditions);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 006 — icon set: 3 arrows (xl3Arrows)
    // -------------------------------------------------------------------------
    private static void GenerateCfFixture_IconSetArrows(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? conditions = null;
        object? iconSet = null;
        try
        {
            workbook = OpenCfWorkbook(workbooks, outputPath, "IconSetArrows", out object ws);
            worksheet = ws;
            PopulateCfData(worksheet, includeNegatives: false);

            range = ((dynamic)worksheet).Range("B2:B12");
            conditions = ((dynamic)range).FormatConditions;
            iconSet = ((dynamic)conditions).AddIconSetCondition();
            // xl3Arrows = 1 in XlIconSet enum
            ((dynamic)iconSet).IconSet = ((dynamic)worksheet).Parent.IconSets(xl3Arrows);

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(iconSet);
            ReleaseComObject(conditions);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 007 — icon set: 5 ratings (xl5Rating = 24)
    // -------------------------------------------------------------------------
    private static void GenerateCfFixture_IconSetRating(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? conditions = null;
        object? iconSet = null;
        try
        {
            workbook = OpenCfWorkbook(workbooks, outputPath, "IconSetRating", out object ws);
            worksheet = ws;
            PopulateCfData(worksheet, includeNegatives: false);

            range = ((dynamic)worksheet).Range("B2:B12");
            conditions = ((dynamic)range).FormatConditions;
            iconSet = ((dynamic)conditions).AddIconSetCondition();
            // xl5Rating = 24 in XlIconSet enum
            ((dynamic)iconSet).IconSet = ((dynamic)worksheet).Parent.IconSets(xl5Rating);

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(iconSet);
            ReleaseComObject(conditions);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 008 — highlight / DXF: cell-value > 5, fill + bold + font color +
    //            number format + box border (exercises dxf numfmt + border gaps)
    // -------------------------------------------------------------------------
    private static void GenerateCfFixture_HighlightDxf(dynamic workbooks, string outputPath)
    {
        // Excel COM cannot set FormatCondition.Borders[index].LineStyle via COM automation
        // (throws 0x800A03EC "Unable to set the LineStyle property of the Border class").
        // Strategy: write fill + font + numFmt via COM, then patch the OOXML to inject the border
        // into the <dxf> element. This gives us a valid Excel-authored file that exercises the
        // dxf border gap in FreeX rendering.

        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? conditions = null;
        object? condition = null;
        try
        {
            workbook = OpenCfWorkbook(workbooks, outputPath, "HighlightDxf", out object ws);
            worksheet = ws;
            PopulateCfData(worksheet, includeNegatives: false);

            range = ((dynamic)worksheet).Range("B2:B12");
            conditions = ((dynamic)range).FormatConditions;
            // XlCellValue=1, XlGreater=5
            condition = ((dynamic)conditions).Add(XlCellValue, XlGreater, "5");

            // Fill — light red
            ((dynamic)condition).Interior.Color = ToOleColor(255, 199, 206);

            // Font — bold + dark red
            ((dynamic)condition).Font.Bold = true;
            ((dynamic)condition).Font.Color = ToOleColor(156, 0, 6);

            // Number format (dxf numFmt — known FreeX gap)
            ((dynamic)condition).NumberFormat = "$#,##0.00";

            // NOTE: border cannot be set via COM — patched in OOXML below.

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(condition);
            ReleaseComObject(conditions);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }

        // Post-process: inject <border> into the <dxf> in styles.xml so the workbook
        // has a visible box border in the CF rule (dark red, thin, all edges).
        PatchHighlightDxfBorder(outputPath);

        Console.WriteLine($"Generated: {outputPath}");
    }

    /// <summary>
    /// Opens the .xlsx ZIP, finds the first &lt;dxf&gt; element in styles.xml that has a
    /// &lt;fill&gt; (our CF highlight rule), and appends a &lt;border&gt; child with thin dark-red
    /// edges on all four sides after the &lt;fill&gt; (CT_Dxf order: font, numFmt, fill, border).
    /// Saves back in-place.
    /// </summary>
    private static void PatchHighlightDxfBorder(string xlsxPath)
    {
        // Dark-red RGB "9C0006" = 156,0,6 → hex ARGB for OOXML
        const string darkRedHex = "FF9C0006";
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        // Build border element directly in the correct namespace (no redundant xmlns attr)
        var borderEl = new XElement(ns + "border",
            new XElement(ns + "left",
                new XAttribute("style", "thin"),
                new XElement(ns + "color", new XAttribute("rgb", darkRedHex))),
            new XElement(ns + "right",
                new XAttribute("style", "thin"),
                new XElement(ns + "color", new XAttribute("rgb", darkRedHex))),
            new XElement(ns + "top",
                new XAttribute("style", "thin"),
                new XElement(ns + "color", new XAttribute("rgb", darkRedHex))),
            new XElement(ns + "bottom",
                new XAttribute("style", "thin"),
                new XElement(ns + "color", new XAttribute("rgb", darkRedHex))));

        var tempPath = xlsxPath + ".tmp";
        using (var inZip = ZipFile.OpenRead(xlsxPath))
        using (var outZip = ZipFile.Open(tempPath, ZipArchiveMode.Create))
        {
            foreach (var entry in inZip.Entries)
            {
                var outEntry = outZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var inStream = entry.Open();
                using var outStream = outEntry.Open();

                if (!entry.FullName.Equals("xl/styles.xml", StringComparison.OrdinalIgnoreCase))
                {
                    inStream.CopyTo(outStream);
                    continue;
                }

                // Parse and patch styles.xml
                var doc = XDocument.Load(inStream);

                // Find the first <dxf> that contains <fill> (our CF rule)
                var dxfsEl = doc.Root?.Element(ns + "dxfs");
                if (dxfsEl != null)
                {
                    var targetDxf = dxfsEl.Elements(ns + "dxf")
                        .FirstOrDefault(d => d.Element(ns + "fill") != null);

                    if (targetDxf != null)
                    {
                        // CT_Dxf sequence: font, numFmt, fill, alignment, border, protection
                        // Insert <border> after <fill>
                        var fillEl = targetDxf.Element(ns + "fill");
                        fillEl?.AddAfterSelf(borderEl);
                    }
                }

                doc.Save(outStream);
            }
        }

        File.Delete(xlsxPath);
        File.Move(tempPath, xlsxPath);
    }

    // XlBordersIndex constants for DXF border in CF (same values as regular borders)
    private const int Program_XlEdgeLeft = 7;
    private const int Program_XlEdgeTop = 8;
    private const int Program_XlEdgeBottom = 9;
    private const int Program_XlEdgeRight = 10;

    // -------------------------------------------------------------------------
    // case 009 — Top 10 (Top 3) with fill
    // -------------------------------------------------------------------------
    private static void GenerateCfFixture_Top10(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? conditions = null;
        object? top10 = null;
        try
        {
            workbook = OpenCfWorkbook(workbooks, outputPath, "Top10", out object ws);
            worksheet = ws;
            PopulateCfData(worksheet, includeNegatives: false);

            range = ((dynamic)worksheet).Range("B2:B12");
            conditions = ((dynamic)range).FormatConditions;
            top10 = ((dynamic)conditions).AddTop10();
            ((dynamic)top10).TopBottom = XlTop10Top;   // 1 = top
            ((dynamic)top10).Rank = 3;
            ((dynamic)top10).Interior.Color = ToOleColor(255, 215, 0);  // gold

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(top10);
            ReleaseComObject(conditions);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // =========================================================================
    // Structured Table (ListObject) corpus fixtures
    // =========================================================================

    // XlTotalsCalculation enum values
    private const int XlTotalsCalculationNone = 0;
    private const int XlTotalsCalculationSum = 2;

    /// <summary>Returns the output paths for all eight table corpus fixtures.</summary>
    public static IReadOnlyList<string> GetExcelTableCorpusFixturePaths(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        return
        [
            Path.Combine(outputDirectory, "Excel_native_table_medium2_rowstripes_001.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_table_light1_002.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_table_totals_003.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_table_colstripes_004.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_table_firstlast_005.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_table_dark1_006.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_table_light8_007.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_table_allfeatures_008.xlsx"),
        ];
    }

    /// <summary>Per-file dispatch for structured-table corpus fixtures.</summary>
    private static void GenerateExcelNativeTableCorpusFixture(dynamic workbooks, string outputPath, string fileName)
    {
        if (fileName.Contains("medium2_rowstripes_001", StringComparison.OrdinalIgnoreCase))
            GenerateTableFixture_Medium2RowStripes(workbooks, outputPath);
        else if (fileName.Contains("light1_002", StringComparison.OrdinalIgnoreCase))
            GenerateTableFixture_Light1(workbooks, outputPath);
        else if (fileName.Contains("totals_003", StringComparison.OrdinalIgnoreCase))
            GenerateTableFixture_Totals(workbooks, outputPath);
        else if (fileName.Contains("colstripes_004", StringComparison.OrdinalIgnoreCase))
            GenerateTableFixture_ColStripes(workbooks, outputPath);
        else if (fileName.Contains("firstlast_005", StringComparison.OrdinalIgnoreCase))
            GenerateTableFixture_FirstLast(workbooks, outputPath);
        else if (fileName.Contains("dark1_006", StringComparison.OrdinalIgnoreCase))
            GenerateTableFixture_Dark1(workbooks, outputPath);
        else if (fileName.Contains("light8_007", StringComparison.OrdinalIgnoreCase))
            GenerateTableFixture_Light8(workbooks, outputPath);
        else if (fileName.Contains("allfeatures_008", StringComparison.OrdinalIgnoreCase))
            GenerateTableFixture_AllFeatures(workbooks, outputPath);
        else
            throw new ArgumentException($"Unknown table corpus fixture: {fileName}");
    }

    // -------------------------------------------------------------------------
    // Shared helpers for table fixtures
    // -------------------------------------------------------------------------

    /// <summary>
    /// Populates a header row + 8 data rows across 4 columns (A:D).
    /// Layout:  Product | Q1 | Q2 | Q3
    ///          rows 2-9 with product labels and numeric sales data.
    /// </summary>
    private static void PopulateTableData(object worksheet)
    {
        // Header row
        SetExcelCellValue(worksheet, 1, 1, "Product");
        SetExcelCellValue(worksheet, 1, 2, "Q1");
        SetExcelCellValue(worksheet, 1, 3, "Q2");
        SetExcelCellValue(worksheet, 1, 4, "Q3");

        // Data rows
        string[] products = ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta"];
        double[] q1 = [1200, 850, 2300, 470, 3100, 620, 1800, 940];
        double[] q2 = [1450, 920, 2100, 510, 2900, 680, 1950, 870];
        double[] q3 = [1380, 990, 2450, 490, 3200, 750, 2050, 1010];

        for (var i = 0; i < 8; i++)
        {
            SetExcelCellValue(worksheet, i + 2, 1, products[i]);
            SetExcelCellValue(worksheet, i + 2, 2, q1[i]);
            SetExcelCellValue(worksheet, i + 2, 3, q2[i]);
            SetExcelCellValue(worksheet, i + 2, 4, q3[i]);
        }
    }

    /// <summary>
    /// Creates a ListObject over A1:D9, applies the given style and flags,
    /// saves, and releases all COM objects.
    /// </summary>
    private static void GenerateTableFixtureCore(
        dynamic workbooks,
        string outputPath,
        string sheetName,
        string tableName,
        string tableStyle,
        bool showRowStripes,
        bool showColumnStripes,
        bool showFirstColumn,
        bool showLastColumn,
        bool showTotals,
        bool addSumOnNumericColumns)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? listObjects = null;
        object? table = null;
        object? listColumns = null;
        try
        {
            workbook = workbooks.Add();
            worksheet = ((dynamic)workbook).Worksheets[1];
            ((dynamic)worksheet).Name = sheetName;

            PopulateTableData(worksheet);

            range = ((dynamic)worksheet).Range("A1:D9");
            listObjects = ((dynamic)worksheet).ListObjects;
            table = ((dynamic)listObjects).Add(XlSrcRange, range, Missing.Value, XlYes);

            ((dynamic)table).Name = tableName;
            ((dynamic)table).TableStyle = tableStyle;
            ((dynamic)table).ShowTableStyleRowStripes = showRowStripes;
            ((dynamic)table).ShowTableStyleColumnStripes = showColumnStripes;
            ((dynamic)table).ShowTableStyleFirstColumn = showFirstColumn;
            ((dynamic)table).ShowTableStyleLastColumn = showLastColumn;

            if (showTotals)
            {
                ((dynamic)table).ShowTotals = true;

                if (addSumOnNumericColumns)
                {
                    listColumns = ((dynamic)table).ListColumns;
                    // Columns 2, 3, 4 are Q1/Q2/Q3 — apply Sum totals calculation
                    for (var colIdx = 2; colIdx <= 4; colIdx++)
                    {
                        object? col = null;
                        try
                        {
                            col = ((dynamic)listColumns)[colIdx];
                            ((dynamic)col).TotalsCalculation = XlTotalsCalculationSum;
                        }
                        finally
                        {
                            ReleaseComObject(col);
                        }
                    }
                }
            }

            AutoFitExcelColumns(worksheet, "A:D");
            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(listColumns);
            ReleaseComObject(table);
            ReleaseComObject(listObjects);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 001 — TableStyleMedium2, row stripes ON (most-common; Office-theme accent color bug)
    // -------------------------------------------------------------------------
    private static void GenerateTableFixture_Medium2RowStripes(dynamic workbooks, string outputPath) =>
        GenerateTableFixtureCore(workbooks, outputPath,
            sheetName: "TableData",
            tableName: "Table_Medium2_RowStripes",
            tableStyle: "TableStyleMedium2",
            showRowStripes: true,
            showColumnStripes: false,
            showFirstColumn: false,
            showLastColumn: false,
            showTotals: false,
            addSumOnNumericColumns: false);

    // -------------------------------------------------------------------------
    // case 002 — TableStyleLight1 (grey), row stripes
    // -------------------------------------------------------------------------
    private static void GenerateTableFixture_Light1(dynamic workbooks, string outputPath) =>
        GenerateTableFixtureCore(workbooks, outputPath,
            sheetName: "TableData",
            tableName: "Table_Light1",
            tableStyle: "TableStyleLight1",
            showRowStripes: true,
            showColumnStripes: false,
            showFirstColumn: false,
            showLastColumn: false,
            showTotals: false,
            addSumOnNumericColumns: false);

    // -------------------------------------------------------------------------
    // case 003 — TableStyleMedium2, ShowTotals=true with Sum on Q1/Q2/Q3
    // -------------------------------------------------------------------------
    private static void GenerateTableFixture_Totals(dynamic workbooks, string outputPath) =>
        GenerateTableFixtureCore(workbooks, outputPath,
            sheetName: "TableData",
            tableName: "Table_Totals",
            tableStyle: "TableStyleMedium2",
            showRowStripes: true,
            showColumnStripes: false,
            showFirstColumn: false,
            showLastColumn: false,
            showTotals: true,
            addSumOnNumericColumns: true);

    // -------------------------------------------------------------------------
    // case 004 — TableStyleMedium2, row stripes OFF, column stripes ON
    // -------------------------------------------------------------------------
    private static void GenerateTableFixture_ColStripes(dynamic workbooks, string outputPath) =>
        GenerateTableFixtureCore(workbooks, outputPath,
            sheetName: "TableData",
            tableName: "Table_ColStripes",
            tableStyle: "TableStyleMedium2",
            showRowStripes: false,
            showColumnStripes: true,
            showFirstColumn: false,
            showLastColumn: false,
            showTotals: false,
            addSumOnNumericColumns: false);

    // -------------------------------------------------------------------------
    // case 005 — TableStyleMedium2, row stripes + first column + last column emphasis
    // -------------------------------------------------------------------------
    private static void GenerateTableFixture_FirstLast(dynamic workbooks, string outputPath) =>
        GenerateTableFixtureCore(workbooks, outputPath,
            sheetName: "TableData",
            tableName: "Table_FirstLast",
            tableStyle: "TableStyleMedium2",
            showRowStripes: true,
            showColumnStripes: false,
            showFirstColumn: true,
            showLastColumn: true,
            showTotals: false,
            addSumOnNumericColumns: false);

    // -------------------------------------------------------------------------
    // case 006 — TableStyleDark1
    // -------------------------------------------------------------------------
    private static void GenerateTableFixture_Dark1(dynamic workbooks, string outputPath) =>
        GenerateTableFixtureCore(workbooks, outputPath,
            sheetName: "TableData",
            tableName: "Table_Dark1",
            tableStyle: "TableStyleDark1",
            showRowStripes: true,
            showColumnStripes: false,
            showFirstColumn: false,
            showLastColumn: false,
            showTotals: false,
            addSumOnNumericColumns: false);

    // -------------------------------------------------------------------------
    // case 007 — TableStyleLight8 (black-header light style)
    // -------------------------------------------------------------------------
    private static void GenerateTableFixture_Light8(dynamic workbooks, string outputPath) =>
        GenerateTableFixtureCore(workbooks, outputPath,
            sheetName: "TableData",
            tableName: "Table_Light8",
            tableStyle: "TableStyleLight8",
            showRowStripes: true,
            showColumnStripes: false,
            showFirstColumn: false,
            showLastColumn: false,
            showTotals: false,
            addSumOnNumericColumns: false);

    // -------------------------------------------------------------------------
    // case 008 — TableStyleMedium4 (green), ShowTotals + row stripes + first + last column
    // -------------------------------------------------------------------------
    private static void GenerateTableFixture_AllFeatures(dynamic workbooks, string outputPath) =>
        GenerateTableFixtureCore(workbooks, outputPath,
            sheetName: "TableData",
            tableName: "Table_AllFeatures",
            tableStyle: "TableStyleMedium4",
            showRowStripes: true,
            showColumnStripes: false,
            showFirstColumn: true,
            showLastColumn: true,
            showTotals: true,
            addSumOnNumericColumns: true);

    // =========================================================================
    // Sparkline corpus fixtures
    // =========================================================================
    //
    // Excel COM constants for sparklines
    private const int XlSparkLine    = 1;  // xlSparkLine
    private const int XlSparkColumn  = 2;  // xlSparkColumn
    private const int XlSparkColumnStacked100 = 3;  // xlSparkColumnStacked100 (win/loss)
    // xlSparkScale values for MinScaleType / MaxScaleType
    private const int XlSparkScaleCustom     = 1;
    private const int XlSparkScaleGroup      = 2;
    private const int XlSparkScaleIndividual = 3;

    public static IReadOnlyList<string> GetExcelSparklineCorpusFixturePaths(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        return
        [
            Path.Combine(outputDirectory, "Excel_native_sparkline_line_markers_001.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_sparkline_column_highlow_negative_002.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_sparkline_winloss_003.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_sparkline_axis_shown_004.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_sparkline_custom_minmax_005.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_sparkline_group_scaling_006.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_sparkline_series_color_007.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_sparkline_line_weight_008.xlsx"),
        ];
    }

    /// <summary>Per-file dispatch for sparkline corpus fixtures.</summary>
    private static void GenerateExcelNativeSparklineCorpusFixture(dynamic workbooks, string outputPath, string fileName)
    {
        if (fileName.Contains("line_markers_001", StringComparison.OrdinalIgnoreCase))
            GenerateSparklineFixture_LineMarkers(workbooks, outputPath);
        else if (fileName.Contains("column_highlow_negative_002", StringComparison.OrdinalIgnoreCase))
            GenerateSparklineFixture_ColumnHighLowNegative(workbooks, outputPath);
        else if (fileName.Contains("winloss_003", StringComparison.OrdinalIgnoreCase))
            GenerateSparklineFixture_WinLoss(workbooks, outputPath);
        else if (fileName.Contains("axis_shown_004", StringComparison.OrdinalIgnoreCase))
            GenerateSparklineFixture_AxisShown(workbooks, outputPath);
        else if (fileName.Contains("custom_minmax_005", StringComparison.OrdinalIgnoreCase))
            GenerateSparklineFixture_CustomMinMax(workbooks, outputPath);
        else if (fileName.Contains("group_scaling_006", StringComparison.OrdinalIgnoreCase))
            GenerateSparklineFixture_GroupScaling(workbooks, outputPath);
        else if (fileName.Contains("series_color_007", StringComparison.OrdinalIgnoreCase))
            GenerateSparklineFixture_SeriesColor(workbooks, outputPath);
        else if (fileName.Contains("line_weight_008", StringComparison.OrdinalIgnoreCase))
            GenerateSparklineFixture_LineWeight(workbooks, outputPath);
        else
            throw new ArgumentException($"Unknown sparkline corpus fixture: {fileName}");
    }

    // -------------------------------------------------------------------------
    // Shared helpers for sparkline fixtures
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes a small 5-row data block into columns A–B starting at the given row.
    /// Returns the data range address (e.g. "A2:A6") for use as the sparkline source.
    /// The sparkline cell is placed in the column immediately to the right at the top row.
    /// </summary>
    private static (string DataRange, string SparklineCell) WriteSparklineData(
        object worksheet, int startRow, int dataCol, double[] values)
    {
        for (var i = 0; i < values.Length; i++)
            SetExcelCellValue(worksheet, startRow + i, dataCol, values[i]);

        var colLetter = ColLetter(dataCol);
        var spCol = ColLetter(dataCol + values.Length + 1); // skip one blank column
        var dataRange = $"{colLetter}{startRow}:{colLetter}{startRow + values.Length - 1}";
        var sparkCell = $"{spCol}{startRow}";
        return (dataRange, sparkCell);
    }

    private static string ColLetter(int col)
    {
        // 1-based column index → letter(s) (A, B, ..., Z, AA, ...)
        var result = "";
        while (col > 0)
        {
            col--;
            result = (char)('A' + col % 26) + result;
            col /= 26;
        }
        return result;
    }

    private static object OpenSparklineWorkbook(dynamic workbooks, string outputPath, string sheetName,
        out object worksheet)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        if (File.Exists(outputPath)) File.Delete(outputPath);
        var workbook = workbooks.Add();
        worksheet = ((dynamic)workbook).Worksheets[1];
        // Keep the default sheet name "Sheet1" so that source-data addresses
        // (which all reference "Sheet1!…") remain valid.
        // The sheetName parameter is kept for API compatibility but not applied.
        _ = sheetName;
        return workbook;
    }

    // -------------------------------------------------------------------------
    // case 001 — line sparkline with all marker types visible
    // -------------------------------------------------------------------------
    private static void GenerateSparklineFixture_LineMarkers(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? groups = null;
        object? group = null;
        object? points = null;
        try
        {
            workbook = OpenSparklineWorkbook(workbooks, outputPath, "LineMarkers", out object ws);
            worksheet = ws;

            double[] data = [3, 7, 2, 9, 5, 1, 8];
            var (dataRange, sparkCell) = WriteSparklineData(worksheet, 2, 1, data);
            SetExcelCellValue(worksheet, 1, 1, "Data");
            SetExcelCellValue(worksheet, 1, ColLetterToIndex(sparkCell[..^1]) , "Sparkline");

            range = ((dynamic)worksheet).Range(sparkCell);
            groups = ((dynamic)range).SparklineGroups;
            group = ((dynamic)groups).Add(XlSparkLine, $"Sheet1!{dataRange}");
            points = ((dynamic)group).Points;
            ((dynamic)points).Markers.Visible = true;
            ((dynamic)points).Highpoint.Visible = true;
            ((dynamic)points).Lowpoint.Visible = true;
            ((dynamic)points).Firstpoint.Visible = true;
            ((dynamic)points).Lastpoint.Visible = true;
            ((dynamic)points).Negative.Visible = true;
            // Assign distinct colors so we can verify each role
            ((dynamic)points).Markers.Color.Color   = ToOleColor(70, 130, 180);   // steel blue — all markers
            ((dynamic)points).Highpoint.Color.Color  = ToOleColor(255, 0, 0);      // red — high
            ((dynamic)points).Lowpoint.Color.Color   = ToOleColor(0, 0, 255);      // blue — low
            ((dynamic)points).Firstpoint.Color.Color = ToOleColor(0, 200, 0);      // green — first
            ((dynamic)points).Lastpoint.Color.Color  = ToOleColor(255, 165, 0);    // orange — last

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(points);
            ReleaseComObject(group);
            ReleaseComObject(groups);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 002 — column sparkline, high/low/negative coloring
    // -------------------------------------------------------------------------
    private static void GenerateSparklineFixture_ColumnHighLowNegative(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? groups = null;
        object? group = null;
        object? points = null;
        try
        {
            workbook = OpenSparklineWorkbook(workbooks, outputPath, "ColHighLowNeg", out object ws);
            worksheet = ws;

            double[] data = [5, -3, 8, -1, 4, -6, 2];
            var (dataRange, sparkCell) = WriteSparklineData(worksheet, 2, 1, data);
            SetExcelCellValue(worksheet, 1, 1, "Data");

            range = ((dynamic)worksheet).Range(sparkCell);
            groups = ((dynamic)range).SparklineGroups;
            group = ((dynamic)groups).Add(XlSparkColumn, $"Sheet1!{dataRange}");
            points = ((dynamic)group).Points;
            ((dynamic)points).Highpoint.Visible = true;
            ((dynamic)points).Lowpoint.Visible  = true;
            ((dynamic)points).Negative.Visible  = true;
            ((dynamic)points).Highpoint.Color.Color = ToOleColor(0, 176, 80);   // green — high
            ((dynamic)points).Lowpoint.Color.Color  = ToOleColor(255, 192, 0);  // gold — low
            ((dynamic)points).Negative.Color.Color  = ToOleColor(255, 0, 0);    // red — negative

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(points);
            ReleaseComObject(group);
            ReleaseComObject(groups);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 003 — win/loss sparkline
    // -------------------------------------------------------------------------
    private static void GenerateSparklineFixture_WinLoss(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? groups = null;
        object? group = null;
        object? points = null;
        try
        {
            workbook = OpenSparklineWorkbook(workbooks, outputPath, "WinLoss", out object ws);
            worksheet = ws;

            double[] data = [1, -1, 1, 1, -1, 1, -1, 1];
            var (dataRange, sparkCell) = WriteSparklineData(worksheet, 2, 1, data);
            SetExcelCellValue(worksheet, 1, 1, "Data");

            range = ((dynamic)worksheet).Range(sparkCell);
            groups = ((dynamic)range).SparklineGroups;
            group = ((dynamic)groups).Add(XlSparkColumnStacked100, $"Sheet1!{dataRange}");
            points = ((dynamic)group).Points;
            ((dynamic)points).Negative.Visible = true;
            ((dynamic)points).Negative.Color.Color = ToOleColor(255, 0, 0);  // red for loss

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(points);
            ReleaseComObject(group);
            ReleaseComObject(groups);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 004 — line sparkline with axis line shown
    // -------------------------------------------------------------------------
    private static void GenerateSparklineFixture_AxisShown(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? groups = null;
        object? group = null;
        object? axes = null;
        object? hAxis = null;
        object? axisObj = null;
        try
        {
            workbook = OpenSparklineWorkbook(workbooks, outputPath, "AxisShown", out object ws);
            worksheet = ws;

            double[] data = [3, -2, 5, -1, 4, -3, 2];
            var (dataRange, sparkCell) = WriteSparklineData(worksheet, 2, 1, data);
            SetExcelCellValue(worksheet, 1, 1, "Data");

            range = ((dynamic)worksheet).Range(sparkCell);
            groups = ((dynamic)range).SparklineGroups;
            group = ((dynamic)groups).Add(XlSparkLine, $"Sheet1!{dataRange}");
            axes = ((dynamic)group).Axes;
            hAxis = ((dynamic)axes).Horizontal;
            axisObj = ((dynamic)hAxis).Axis;
            ((dynamic)axisObj).Visible = true;
            ((dynamic)axisObj).Color.Color = ToOleColor(0, 0, 128);  // navy — axis

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(axisObj);
            ReleaseComObject(hAxis);
            ReleaseComObject(axes);
            ReleaseComObject(group);
            ReleaseComObject(groups);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 005 — line sparkline with custom min/max axis bounds
    // -------------------------------------------------------------------------
    private static void GenerateSparklineFixture_CustomMinMax(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? groups = null;
        object? group = null;
        object? axes = null;
        object? vAxis = null;
        try
        {
            workbook = OpenSparklineWorkbook(workbooks, outputPath, "CustomMinMax", out object ws);
            worksheet = ws;

            double[] data = [2, 5, 3, 7, 4];
            var (dataRange, sparkCell) = WriteSparklineData(worksheet, 2, 1, data);
            SetExcelCellValue(worksheet, 1, 1, "Data");

            range = ((dynamic)worksheet).Range(sparkCell);
            groups = ((dynamic)range).SparklineGroups;
            group = ((dynamic)groups).Add(XlSparkLine, $"Sheet1!{dataRange}");
            axes = ((dynamic)group).Axes;
            vAxis = ((dynamic)axes).Vertical;
            ((dynamic)vAxis).MinScaleType = XlSparkScaleCustom;
            ((dynamic)vAxis).MaxScaleType = XlSparkScaleCustom;
            ((dynamic)vAxis).CustomMinScaleValue = 0.0;
            ((dynamic)vAxis).CustomMaxScaleValue = 10.0;

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(vAxis);
            ReleaseComObject(axes);
            ReleaseComObject(group);
            ReleaseComObject(groups);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 006 — two column sparkline groups with Group scaling
    // -------------------------------------------------------------------------
    private static void GenerateSparklineFixture_GroupScaling(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? rangeA = null;
        object? rangeB = null;
        object? groupsA = null;
        object? groupsB = null;
        object? group1 = null;
        object? group2 = null;
        object? axes1 = null;
        object? axes2 = null;
        object? vAxis1 = null;
        object? vAxis2 = null;
        try
        {
            workbook = OpenSparklineWorkbook(workbooks, outputPath, "GroupScaling", out object ws);
            worksheet = ws;

            // Group A: small values (sparkline at C2)
            double[] dataA = [1, 2, 3, 2, 1];
            SetExcelCellValue(worksheet, 1, 1, "Group A data");
            for (var i = 0; i < dataA.Length; i++)
                SetExcelCellValue(worksheet, 2 + i, 1, dataA[i]);
            SetExcelCellValue(worksheet, 1, 3, "Spark A");

            // Group B: large values (sparkline at G2)
            double[] dataB = [5, 10, 7, 9, 6];
            SetExcelCellValue(worksheet, 1, 5, "Group B data");
            for (var i = 0; i < dataB.Length; i++)
                SetExcelCellValue(worksheet, 2 + i, 5, dataB[i]);
            SetExcelCellValue(worksheet, 1, 7, "Spark B");

            // SparklineGroups must be accessed on the destination Range, not the Worksheet
            rangeA = ((dynamic)worksheet).Range("C2");
            groupsA = ((dynamic)rangeA).SparklineGroups;
            group1 = ((dynamic)groupsA).Add(XlSparkColumn, "Sheet1!A2:A6");

            rangeB = ((dynamic)worksheet).Range("G2");
            groupsB = ((dynamic)rangeB).SparklineGroups;
            group2 = ((dynamic)groupsB).Add(XlSparkColumn, "Sheet1!E2:E6");

            // Apply Group scaling to both
            axes1 = ((dynamic)group1).Axes;
            vAxis1 = ((dynamic)axes1).Vertical;
            ((dynamic)vAxis1).MinScaleType = XlSparkScaleGroup;
            ((dynamic)vAxis1).MaxScaleType = XlSparkScaleGroup;

            axes2 = ((dynamic)group2).Axes;
            vAxis2 = ((dynamic)axes2).Vertical;
            ((dynamic)vAxis2).MinScaleType = XlSparkScaleGroup;
            ((dynamic)vAxis2).MaxScaleType = XlSparkScaleGroup;

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(vAxis2);
            ReleaseComObject(vAxis1);
            ReleaseComObject(axes2);
            ReleaseComObject(axes1);
            ReleaseComObject(group2);
            ReleaseComObject(group1);
            ReleaseComObject(groupsB);
            ReleaseComObject(groupsA);
            ReleaseComObject(rangeB);
            ReleaseComObject(rangeA);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 007 — line sparkline with custom series color
    // -------------------------------------------------------------------------
    private static void GenerateSparklineFixture_SeriesColor(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? groups = null;
        object? group = null;
        object? seriesColor = null;
        try
        {
            workbook = OpenSparklineWorkbook(workbooks, outputPath, "SeriesColor", out object ws);
            worksheet = ws;

            double[] data = [4, 6, 2, 8, 5, 3, 7];
            var (dataRange, sparkCell) = WriteSparklineData(worksheet, 2, 1, data);
            SetExcelCellValue(worksheet, 1, 1, "Data");

            range = ((dynamic)worksheet).Range(sparkCell);
            groups = ((dynamic)range).SparklineGroups;
            group = ((dynamic)groups).Add(XlSparkLine, $"Sheet1!{dataRange}");
            seriesColor = ((dynamic)group).SeriesColor;
            ((dynamic)seriesColor).Color = ToOleColor(148, 0, 211);  // purple

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(seriesColor);
            ReleaseComObject(group);
            ReleaseComObject(groups);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // -------------------------------------------------------------------------
    // case 008 — line sparkline with non-default line weight
    // -------------------------------------------------------------------------
    private static void GenerateSparklineFixture_LineWeight(dynamic workbooks, string outputPath)
    {
        object? workbook = null;
        object? worksheet = null;
        object? range = null;
        object? groups = null;
        object? group = null;
        try
        {
            workbook = OpenSparklineWorkbook(workbooks, outputPath, "LineWeight", out object ws);
            worksheet = ws;

            double[] data = [3, 6, 4, 7, 2, 5, 8];
            var (dataRange, sparkCell) = WriteSparklineData(worksheet, 2, 1, data);
            SetExcelCellValue(worksheet, 1, 1, "Data");

            range = ((dynamic)worksheet).Range(sparkCell);
            groups = ((dynamic)range).SparklineGroups;
            group = ((dynamic)groups).Add(XlSparkLine, $"Sheet1!{dataRange}");
            ((dynamic)group).LineWeight = 2.25;  // thick line — distinctly visible vs default 0.75

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(group);
            ReleaseComObject(groups);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    private static int ColLetterToIndex(string col)
    {
        var index = 0;
        foreach (var c in col.ToUpperInvariant())
            index = index * 26 + (c - 'A' + 1);
        return index;
    }

    // =========================================================================
    // Cell-style baseline corpus fixtures
    // =========================================================================
    //
    // Excel COM constants used below (not already declared at the class level):
    //
    // XlLineStyle (cell.Borders.LineStyle):
    //   xlContinuous   = 1      → Thin (with Weight=XlThin) or Medium/Thick via weight
    //   xlDash         = -4115  → Dashed
    //   xlDashDot      = 4      → DashDot
    //   xlDashDotDot   = 5      → DashDotDot
    //   xlDot          = -4118  → Dotted
    //   xlDouble       = -4119  → Double
    //   xlSlantDashDot = 13     → SlantDashDot
    //   xlLineStyleNone= -4142  → No border
    //
    // XlBorderWeight:
    //   xlHairline = 1   → Hair (thinnest; only meaningful with xlContinuous)
    //   xlThin     = 2   → Thin
    //   xlMedium   = -4138 → Medium
    //   xlThick    = 4   → Thick
    //
    // XlBordersIndex (for individual sides):
    //   xlEdgeLeft   = 7
    //   xlEdgeTop    = 8
    //   xlEdgeBottom = 9
    //   xlEdgeRight  = 10
    //   xlDiagonalDown = 5
    //   xlDiagonalUp   = 6
    //
    // Interior.Pattern (XlPattern):
    //   xlPatternNone           = -4142
    //   xlPatternSolid          = 1
    //   xlPatternGray16         = 17
    //   xlPatternGray25         = -4124
    //   xlPatternGray50         = -4125
    //   xlPatternGray75         = -4126
    //   xlPatternGray8          = 18
    //   xlPatternHorizontal     = -4128
    //   xlPatternVertical       = -4166
    //   xlPatternDown           = -4121
    //   xlPatternUp             = -4162
    //   xlPatternChecker        = 9
    //   xlPatternSemiGray75     = 10
    //   xlPatternLightHorizontal= 11
    //   xlPatternLightVertical  = 12
    //   xlPatternLightDown      = 13
    //   xlPatternLightUp        = 14
    //   xlPatternGrid           = 15
    //   xlPatternCrissCross     = 16
    //
    // HorizontalAlignment (XlHAlign):
    //   xlHAlignGeneral    = 1
    //   xlHAlignLeft       = -4131
    //   xlHAlignCenter     = -4108
    //   xlHAlignRight      = -4152
    //   xlHAlignFill       = 5
    //   xlHAlignJustify    = -4130
    //   xlHAlignCenterAcrossSelection = 7   (= CenterContinuous in OOXML)
    //   xlHAlignDistributed = -4117
    //
    // VerticalAlignment (XlVAlign):
    //   xlVAlignTop        = -4160
    //   xlVAlignCenter     = -4108
    //   xlVAlignBottom     = -4107
    //
    // Orientation (text rotation encoded as degrees or special constant):
    //   xlVertical         = -4166  → stacked vertical (each char on its own line)
    //   positive degrees   = counterclockwise rotation
    //   negative degrees   → passed as 90+abs(degrees) via OOXML mapping, but COM
    //                        accepts negative integers directly for clockwise rotation
    //
    // Underline (XlUnderlineStyle):
    //   xlUnderlineStyleSingle       = 2
    //   xlUnderlineStyleDouble       = -4119
    //   xlUnderlineStyleNone         = -4142

    private const int XlContinuous      = 1;
    private const int XlDash            = -4115;
    private const int XlDashDot         = 4;
    private const int XlDashDotDot      = 5;
    private const int XlDot             = -4118;
    private const int XlDouble          = -4119;
    private const int XlSlantDashDot    = 13;
    private const int XlHairline        = 1;
    private const int XlThin            = 2;
    private const int XlMedium          = -4138;
    private const int XlThick           = 4;
    private const int XlEdgeLeft        = 7;
    private const int XlEdgeTop         = 8;
    private const int XlEdgeBottom      = 9;
    private const int XlEdgeRight       = 10;
    private const int XlDiagonalDown    = 5;
    private const int XlDiagonalUp      = 6;

    private const int XlPatternNone            = -4142;
    private const int XlPatternSolid           = 1;
    private const int XlPatternGray16          = 17;
    private const int XlPatternGray25          = -4124;
    private const int XlPatternGray50          = -4125;
    private const int XlPatternGray75          = -4126;
    private const int XlPatternGray8           = 18;
    private const int XlPatternHorizontal      = -4128;
    private const int XlPatternVertical        = -4166;
    private const int XlPatternDown            = -4121;
    private const int XlPatternUp              = -4162;
    private const int XlPatternChecker         = 9;
    private const int XlPatternLightHorizontal = 11;
    private const int XlPatternLightVertical   = 12;
    private const int XlPatternGrid            = 15;
    private const int XlPatternCrissCross      = 16;

    private const int XlHAlignLeft                    = -4131;
    private const int XlHAlignCenter                  = -4108;
    private const int XlHAlignRight                   = -4152;
    private const int XlHAlignFill                    = 5;
    private const int XlHAlignJustify                 = -4130;
    private const int XlHAlignCenterAcrossSelection   = 7;
    private const int XlHAlignDistributed             = -4117;
    private const int XlVAlignTop                     = -4160;
    private const int XlVAlignCenter                  = -4108;
    private const int XlVertical                      = -4166;

    private const int XlUnderlineStyleSingle = 2;
    private const int XlUnderlineStyleDouble = -4119;
    private const int XlUnderlineStyleNone   = -4142;

    /// <summary>Returns the output paths for all seven cell-style corpus fixtures.</summary>
    public static IReadOnlyList<string> GetExcelCellStyleCorpusFixturePaths(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        return
        [
            Path.Combine(outputDirectory, "Excel_native_cellstyle_borders_styles_001.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_cellstyle_borders_diagonal_002.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_cellstyle_fills_patterns_003.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_cellstyle_fills_gradient_004.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_cellstyle_align_rotation_005.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_cellstyle_merged_006.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_cellstyle_fonts_007.xlsx"),
        ];
    }

    /// <summary>Per-file dispatch for cell-style corpus fixtures.</summary>
    private static void GenerateExcelNativeCellStyleCorpusFixture(dynamic workbooks, string outputPath, string fileName)
    {
        if (fileName.Contains("borders_styles_001", StringComparison.OrdinalIgnoreCase))
            GenerateCellStyleFixture_BordersStyles(workbooks, outputPath);
        else if (fileName.Contains("borders_diagonal_002", StringComparison.OrdinalIgnoreCase))
            GenerateCellStyleFixture_BordersDiagonal(workbooks, outputPath);
        else if (fileName.Contains("fills_patterns_003", StringComparison.OrdinalIgnoreCase))
            GenerateCellStyleFixture_FillsPatterns(workbooks, outputPath);
        else if (fileName.Contains("fills_gradient_004", StringComparison.OrdinalIgnoreCase))
            GenerateCellStyleFixture_FillsGradient(workbooks, outputPath);
        else if (fileName.Contains("align_rotation_005", StringComparison.OrdinalIgnoreCase))
            GenerateCellStyleFixture_AlignRotation(workbooks, outputPath);
        else if (fileName.Contains("merged_006", StringComparison.OrdinalIgnoreCase))
            GenerateCellStyleFixture_Merged(workbooks, outputPath);
        else if (fileName.Contains("fonts_007", StringComparison.OrdinalIgnoreCase))
            GenerateCellStyleFixture_Fonts(workbooks, outputPath);
        else
            throw new ArgumentException($"Unknown cell-style corpus fixture: {fileName}");
    }

    // -------------------------------------------------------------------------
    // Shared helper: open a blank workbook for cell-style fixtures
    // -------------------------------------------------------------------------
    private static object OpenCellStyleWorkbook(dynamic workbooks, string outputPath, string sheetName,
        out object worksheet)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        if (File.Exists(outputPath)) File.Delete(outputPath);
        var workbook = workbooks.Add();
        worksheet = ((dynamic)workbook).Worksheets[1];
        try { ((dynamic)worksheet).Name = sheetName; } catch { /* best effort */ }
        return workbook;
    }

    // -------------------------------------------------------------------------
    // Helper: apply border (lineStyle + weight) to all four edges of a range
    // -------------------------------------------------------------------------
    private static void ApplyAllEdgeBorder(object worksheet, string address,
        int lineStyle, int weight, int color = 0 /*black*/)
    {
        ApplyExcelRangeFormat(worksheet, address, range =>
        {
            foreach (var idx in new[] { XlEdgeLeft, XlEdgeTop, XlEdgeBottom, XlEdgeRight })
            {
                object? border = null;
                try
                {
                    border = range.Borders[idx];
                    ((dynamic)border).LineStyle = lineStyle;
                    ((dynamic)border).Weight    = weight;
                    if (color != 0)
                        ((dynamic)border).Color = color;
                }
                finally
                {
                    ReleaseComObject(border);
                }
            }
        });
    }

    // =========================================================================
    // case 001 — border style matrix
    // =========================================================================
    private static void GenerateCellStyleFixture_BordersStyles(dynamic workbooks, string outputPath)
    {
        object? workbook  = null;
        object? worksheet = null;
        try
        {
            workbook  = OpenCellStyleWorkbook(workbooks, outputPath, "Borders", out object ws);
            worksheet = ws;

            // Column A = style label, Column B = styled cell
            // Rows 1-13 = each named border style; row 14 = colored border
            (string Label, int LineStyle, int Weight)[] styles =
            [
                ("Hair",            XlContinuous,   XlHairline),
                ("Thin",            XlContinuous,   XlThin),
                ("Medium",          XlContinuous,   XlMedium),
                ("Thick",           XlContinuous,   XlThick),
                ("Double",          XlDouble,        XlThin),
                ("Dashed",          XlDash,          XlThin),
                ("Dotted",          XlDot,           XlThin),
                ("DashDot",         XlDashDot,       XlThin),
                ("DashDotDot",      XlDashDotDot,    XlThin),
                ("MediumDashed",    XlDash,          XlMedium),
                ("MediumDashDot",   XlDashDot,       XlMedium),
                ("MediumDashDotDot",XlDashDotDot,    XlMedium),
                ("SlantDashDot",    XlSlantDashDot,  XlMedium),
            ];

            SetExcelCellValue(worksheet, 1, 1, "Style Name");
            SetExcelCellValue(worksheet, 1, 2, "Sample");
            ApplyExcelRangeFormat(worksheet, "A1:B1", r => r.Font.Bold = true);

            for (var i = 0; i < styles.Length; i++)
            {
                var row = i + 2;
                SetExcelCellValue(worksheet, row, 1, styles[i].Label);
                SetExcelCellValue(worksheet, row, 2, styles[i].Label);
                var address = $"B{row}";
                try
                {
                    ApplyAllEdgeBorder(worksheet, address, styles[i].LineStyle, styles[i].Weight);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"  COM note: {styles[i].Label} border (lineStyle={styles[i].LineStyle}, weight={styles[i].Weight}) failed: {ex.Message}");
                }
            }

            // Row after styles: colored Thin border (red)
            var colorRow = styles.Length + 2;
            SetExcelCellValue(worksheet, colorRow, 1, "Thin Red Border");
            SetExcelCellValue(worksheet, colorRow, 2, "Red");
            ApplyAllEdgeBorder(worksheet, $"B{colorRow}", XlContinuous, XlThin, ToOleColor(255, 0, 0));

            // Auto-fit columns A and B for readability
            ApplyExcelRangeFormat(worksheet, "A:B", r => { try { r.Columns.AutoFit(); } catch { } });

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // =========================================================================
    // case 002 — diagonal borders
    // =========================================================================
    private static void GenerateCellStyleFixture_BordersDiagonal(dynamic workbooks, string outputPath)
    {
        object? workbook  = null;
        object? worksheet = null;
        try
        {
            workbook  = OpenCellStyleWorkbook(workbooks, outputPath, "DiagBorders", out object ws);
            worksheet = ws;

            SetExcelCellValue(worksheet, 1, 1, "Diagonal Down");
            SetExcelCellValue(worksheet, 1, 2, "Diagonal Up");
            SetExcelCellValue(worksheet, 1, 3, "Both");
            ApplyExcelRangeFormat(worksheet, "A1:C1", r => r.Font.Bold = true);

            // Row 2: diagonal down only
            SetExcelCellValue(worksheet, 2, 1, "DiagDown");
            ApplyExcelRangeFormat(worksheet, "A2", r =>
            {
                object? b = null;
                try
                {
                    b = r.Borders[XlDiagonalDown];
                    ((dynamic)b).LineStyle = XlContinuous;
                    ((dynamic)b).Weight    = XlMedium;
                    ((dynamic)b).Color     = ToOleColor(0, 0, 200);
                }
                finally { ReleaseComObject(b); }
            });

            // Row 2, col 2: diagonal up only
            SetExcelCellValue(worksheet, 2, 2, "DiagUp");
            ApplyExcelRangeFormat(worksheet, "B2", r =>
            {
                object? b = null;
                try
                {
                    b = r.Borders[XlDiagonalUp];
                    ((dynamic)b).LineStyle = XlContinuous;
                    ((dynamic)b).Weight    = XlMedium;
                    ((dynamic)b).Color     = ToOleColor(200, 0, 0);
                }
                finally { ReleaseComObject(b); }
            });

            // Row 2, col 3: both diagonals
            SetExcelCellValue(worksheet, 2, 3, "Both");
            ApplyExcelRangeFormat(worksheet, "C2", r =>
            {
                object? bd = null;
                object? bu = null;
                try
                {
                    bd = r.Borders[XlDiagonalDown];
                    ((dynamic)bd).LineStyle = XlContinuous;
                    ((dynamic)bd).Weight    = XlMedium;
                    ((dynamic)bd).Color     = ToOleColor(0, 150, 0);

                    bu = r.Borders[XlDiagonalUp];
                    ((dynamic)bu).LineStyle = XlContinuous;
                    ((dynamic)bu).Weight    = XlMedium;
                    ((dynamic)bu).Color     = ToOleColor(150, 0, 150);
                }
                finally
                {
                    ReleaseComObject(bd);
                    ReleaseComObject(bu);
                }
            });

            // Row 3: dashed diagonals (down only)
            SetExcelCellValue(worksheet, 3, 1, "Dashed DiagDown");
            ApplyExcelRangeFormat(worksheet, "A3", r =>
            {
                object? b = null;
                try
                {
                    b = r.Borders[XlDiagonalDown];
                    ((dynamic)b).LineStyle = XlDash;
                    ((dynamic)b).Weight    = XlThin;
                }
                finally { ReleaseComObject(b); }
            });

            ApplyExcelRangeFormat(worksheet, "A:C", r => { try { r.Columns.AutoFit(); } catch { } });

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // =========================================================================
    // case 003 — pattern fills
    // =========================================================================
    private static void GenerateCellStyleFixture_FillsPatterns(dynamic workbooks, string outputPath)
    {
        object? workbook  = null;
        object? worksheet = null;
        try
        {
            workbook  = OpenCellStyleWorkbook(workbooks, outputPath, "Patterns", out object ws);
            worksheet = ws;

            SetExcelCellValue(worksheet, 1, 1, "Pattern Name");
            SetExcelCellValue(worksheet, 1, 2, "Sample");
            ApplyExcelRangeFormat(worksheet, "A1:B1", r => r.Font.Bold = true);

            // Foreground color = navy; background color = light yellow
            var fgColor = ToOleColor(0, 0, 128);
            var bgColor = ToOleColor(255, 255, 153);

            (string Name, int Pattern)[] patterns =
            [
                ("Solid",            XlPatternSolid),
                ("Gray75",           XlPatternGray75),
                ("Gray50",           XlPatternGray50),
                ("Gray25",           XlPatternGray25),
                ("Gray16",           XlPatternGray16),
                ("Gray8",            XlPatternGray8),
                ("Horizontal",       XlPatternHorizontal),
                ("Vertical",         XlPatternVertical),
                ("Down",             XlPatternDown),
                ("Up",               XlPatternUp),
                ("Checker",          XlPatternChecker),
                ("LightHorizontal",  XlPatternLightHorizontal),
                ("LightVertical",    XlPatternLightVertical),
                ("Grid",             XlPatternGrid),
                ("CrissCross",       XlPatternCrissCross),
            ];

            for (var i = 0; i < patterns.Length; i++)
            {
                var row = i + 2;
                SetExcelCellValue(worksheet, row, 1, patterns[i].Name);
                SetExcelCellValue(worksheet, row, 2, patterns[i].Name);
                var address = $"B{row}";
                try
                {
                    ApplyExcelRangeFormat(worksheet, address, r =>
                    {
                        r.Interior.Pattern         = patterns[i].Pattern;
                        r.Interior.PatternColor    = fgColor;
                        // PatternColorIndex vs Color: for named patterns the fg is PatternColor
                        // and the cell background fill is Color (bg).
                        r.Interior.Color           = bgColor;
                    });
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  COM note: pattern {patterns[i].Name} failed: {ex.Message}");
                }
            }

            ApplyExcelRangeFormat(worksheet, "A:B", r => { try { r.Columns.AutoFit(); } catch { } });

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // =========================================================================
    // case 004 — gradient fill (Interior.Gradient)
    // =========================================================================
    private static void GenerateCellStyleFixture_FillsGradient(dynamic workbooks, string outputPath)
    {
        object? workbook   = null;
        object? worksheet  = null;
        object? range      = null;
        object? interior   = null;
        object? gradient   = null;
        object? gradStops  = null;
        object? stop1      = null;
        object? stop2      = null;
        try
        {
            workbook  = OpenCellStyleWorkbook(workbooks, outputPath, "Gradient", out object ws);
            worksheet = ws;

            SetExcelCellValue(worksheet, 1, 1, "2-stop linear gradient (blue → orange, 0° left→right)");
            SetExcelCellValue(worksheet, 6, 1, "2-stop linear gradient (green → white, 90° top→bottom)");
            SetExcelCellValue(worksheet, 11, 1, "3-stop linear gradient (red → yellow → blue, 0°)");

            // Gradient 1: blue→orange, 0° (left→right)
            range    = ((dynamic)worksheet).Range("B2:E5");
            interior = ((dynamic)range).Interior;
            try
            {
                // xlGradientFill = 2; setting Pattern to 2 switches to gradient mode.
                ((dynamic)interior).Pattern = 2; // xlPatternLinearGradient
                gradient  = ((dynamic)interior).Gradient;
                ((dynamic)gradient).Degree = 0.0;    // horizontal
                gradStops = ((dynamic)gradient).ColorStops;
                stop1 = ((dynamic)gradStops).Add(0.0);
                ((dynamic)stop1).Color = ToOleColor(0, 70, 200);
                stop2 = ((dynamic)gradStops).Add(1.0);
                ((dynamic)stop2).Color = ToOleColor(255, 140, 0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  COM note: gradient fill (linear blue→orange) failed: {ex.Message}");
            }
            finally
            {
                ReleaseComObject(stop2);
                ReleaseComObject(stop1);
                ReleaseComObject(gradStops);
                ReleaseComObject(gradient);
                ReleaseComObject(interior);
                ReleaseComObject(range);
                stop1 = stop2 = gradStops = gradient = interior = range = null;
            }

            // Gradient 2: green→white, 90° (top→bottom)
            range    = ((dynamic)worksheet).Range("B7:E10");
            interior = ((dynamic)range).Interior;
            try
            {
                ((dynamic)interior).Pattern = 2; // xlPatternLinearGradient
                gradient  = ((dynamic)interior).Gradient;
                ((dynamic)gradient).Degree = 90.0;
                gradStops = ((dynamic)gradient).ColorStops;
                stop1 = ((dynamic)gradStops).Add(0.0);
                ((dynamic)stop1).Color = ToOleColor(0, 160, 0);
                stop2 = ((dynamic)gradStops).Add(1.0);
                ((dynamic)stop2).Color = ToOleColor(255, 255, 255);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  COM note: gradient fill (linear green→white) failed: {ex.Message}");
            }
            finally
            {
                ReleaseComObject(stop2);
                ReleaseComObject(stop1);
                ReleaseComObject(gradStops);
                ReleaseComObject(gradient);
                ReleaseComObject(interior);
                ReleaseComObject(range);
                stop1 = stop2 = gradStops = gradient = interior = range = null;
            }

            // Gradient 3: red→yellow→blue (3-stop), 0° (left→right)
            object? stop3 = null;
            range    = ((dynamic)worksheet).Range("B12:E15");
            interior = ((dynamic)range).Interior;
            try
            {
                ((dynamic)interior).Pattern = 2; // xlPatternLinearGradient
                gradient  = ((dynamic)interior).Gradient;
                ((dynamic)gradient).Degree = 0.0;
                gradStops = ((dynamic)gradient).ColorStops;
                stop1 = ((dynamic)gradStops).Add(0.0);
                ((dynamic)stop1).Color = ToOleColor(220, 30, 30);   // red
                stop2 = ((dynamic)gradStops).Add(0.5);
                ((dynamic)stop2).Color = ToOleColor(255, 230, 0);   // yellow
                stop3 = ((dynamic)gradStops).Add(1.0);
                ((dynamic)stop3).Color = ToOleColor(30, 60, 220);   // blue
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  COM note: gradient fill (3-stop red→yellow→blue) failed: {ex.Message}");
            }
            finally
            {
                ReleaseComObject(stop3);
                ReleaseComObject(stop2);
                ReleaseComObject(stop1);
                ReleaseComObject(gradStops);
                ReleaseComObject(gradient);
                ReleaseComObject(interior);
                ReleaseComObject(range);
                stop1 = stop2 = stop3 = gradStops = gradient = interior = range = null;
            }

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(stop2);
            ReleaseComObject(stop1);
            ReleaseComObject(gradStops);
            ReleaseComObject(gradient);
            ReleaseComObject(interior);
            ReleaseComObject(range);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // =========================================================================
    // case 005 — alignment, rotation, indent, wrap, shrink-to-fit, Fill, CenterContinuous
    // =========================================================================
    private static void GenerateCellStyleFixture_AlignRotation(dynamic workbooks, string outputPath)
    {
        object? workbook  = null;
        object? worksheet = null;
        try
        {
            workbook  = OpenCellStyleWorkbook(workbooks, outputPath, "AlignRotate", out object ws);
            worksheet = ws;

            SetExcelCellValue(worksheet, 1, 1, "Feature");
            SetExcelCellValue(worksheet, 1, 2, "Sample");
            ApplyExcelRangeFormat(worksheet, "A1:B1", r => r.Font.Bold = true);

            // Make column B wider so rotation / wrap are clearly visible
            ApplyExcelRangeFormat(worksheet, "B:B", r => { try { r.ColumnWidth = 18; } catch { } });
            ApplyExcelRangeFormat(worksheet, "A:A", r => { try { r.ColumnWidth = 24; } catch { } });

            // Row 2: 45° rotation
            SetExcelCellValue(worksheet, 2, 1, "Rotation 45°");
            SetExcelCellValue(worksheet, 2, 2, "Hello 45");
            ApplyExcelRangeFormat(worksheet, "B2", r => r.Orientation = 45);

            // Row 3: 90° rotation
            SetExcelCellValue(worksheet, 3, 1, "Rotation 90°");
            SetExcelCellValue(worksheet, 3, 2, "Hello 90");
            ApplyExcelRangeFormat(worksheet, "B3", r => r.Orientation = 90);

            // Row 4: -90° rotation (clockwise)
            SetExcelCellValue(worksheet, 4, 1, "Rotation -90°");
            SetExcelCellValue(worksheet, 4, 2, "Hello -90");
            ApplyExcelRangeFormat(worksheet, "B4", r => r.Orientation = -90);

            // Row 5: vertical stacked (xlVertical = -4166)
            SetExcelCellValue(worksheet, 5, 1, "Vertical stacked");
            SetExcelCellValue(worksheet, 5, 2, "ABC");
            ApplyExcelRangeFormat(worksheet, "B5", r => r.Orientation = XlVertical);

            // Row 6: indent level 3
            SetExcelCellValue(worksheet, 6, 1, "Indent 3");
            SetExcelCellValue(worksheet, 6, 2, "Indented");
            ApplyExcelRangeFormat(worksheet, "B6", r => r.IndentLevel = 3);

            // Row 7: wrap text
            SetExcelCellValue(worksheet, 7, 1, "Wrap text");
            SetExcelCellValue(worksheet, 7, 2, "This is a long text that should wrap inside the cell");
            ApplyExcelRangeFormat(worksheet, "B7", r => r.WrapText = true);

            // Row 8: shrink-to-fit
            SetExcelCellValue(worksheet, 8, 1, "Shrink to fit");
            SetExcelCellValue(worksheet, 8, 2, "ShrinkThisVeryLongTextIntoCell");
            ApplyExcelRangeFormat(worksheet, "B8", r => r.ShrinkToFit = true);

            // Row 9: horizontal Fill
            SetExcelCellValue(worksheet, 9, 1, "HAlign Fill");
            SetExcelCellValue(worksheet, 9, 2, "=");
            ApplyExcelRangeFormat(worksheet, "B9", r => r.HorizontalAlignment = XlHAlignFill);

            // Row 10: CenterAcrossSelection (CenterContinuous) across B10:D10
            SetExcelCellValue(worksheet, 10, 1, "CenterAcrossSelection");
            SetExcelCellValue(worksheet, 10, 2, "Centered across B:D");
            ApplyExcelRangeFormat(worksheet, "B10:D10", r =>
            {
                r.HorizontalAlignment = XlHAlignCenterAcrossSelection;
            });

            // Row 11: vertical top + horizontal right
            SetExcelCellValue(worksheet, 11, 1, "VAlign Top + HAlign Right");
            SetExcelCellValue(worksheet, 11, 2, "Top-Right");
            ApplyExcelRangeFormat(worksheet, "B11", r =>
            {
                r.HorizontalAlignment = XlHAlignRight;
                r.VerticalAlignment   = XlVAlignTop;
                r.RowHeight = 40;
            });

            // Row 12: Justify alignment
            SetExcelCellValue(worksheet, 12, 1, "HAlign Justify");
            SetExcelCellValue(worksheet, 12, 2, "Justified text content here wide");
            ApplyExcelRangeFormat(worksheet, "B12", r =>
            {
                r.HorizontalAlignment = XlHAlignJustify;
                r.WrapText = true;
            });

            // Row 13: Distributed alignment
            SetExcelCellValue(worksheet, 13, 1, "HAlign Distributed");
            SetExcelCellValue(worksheet, 13, 2, "Dist text");
            ApplyExcelRangeFormat(worksheet, "B13", r => r.HorizontalAlignment = XlHAlignDistributed);

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // =========================================================================
    // case 006 — merged cells
    // =========================================================================
    private static void GenerateCellStyleFixture_Merged(dynamic workbooks, string outputPath)
    {
        object? workbook  = null;
        object? worksheet = null;
        try
        {
            workbook  = OpenCellStyleWorkbook(workbooks, outputPath, "Merged", out object ws);
            worksheet = ws;

            // --- Horizontal merge (A1:D1) ---
            SetExcelCellValue(worksheet, 1, 1, "Horizontal merge A1:D1");
            ApplyExcelRangeFormat(worksheet, "A1:D1", r =>
            {
                r.Merge();
                r.HorizontalAlignment = XlHAlignCenter;
                r.VerticalAlignment   = XlVAlignCenter;
                r.Font.Bold = true;
                r.Interior.Color = ToOleColor(173, 216, 230); // light blue
            });
            ApplyAllEdgeBorder(worksheet, "A1:D1", XlContinuous, XlMedium, ToOleColor(0, 70, 130));

            // --- Vertical merge (A3:A6) ---
            SetExcelCellValue(worksheet, 3, 1, "Vertical A3:A6");
            ApplyExcelRangeFormat(worksheet, "A3:A6", r =>
            {
                r.Merge();
                r.HorizontalAlignment = XlHAlignCenter;
                r.VerticalAlignment   = XlVAlignCenter;
                r.Orientation = 90;
                r.Interior.Color = ToOleColor(255, 228, 196); // bisque
            });
            ApplyAllEdgeBorder(worksheet, "A3:A6", XlContinuous, XlMedium);

            // Fill adjacent cells so the merge is clearly visible
            for (var row = 3; row <= 6; row++)
                SetExcelCellValue(worksheet, row, 2, $"Row {row}");

            // --- Block merge (C3:E5) ---
            SetExcelCellValue(worksheet, 3, 3, "Block C3:E5");
            ApplyExcelRangeFormat(worksheet, "C3:E5", r =>
            {
                r.Merge();
                r.HorizontalAlignment = XlHAlignCenter;
                r.VerticalAlignment   = XlVAlignCenter;
                r.Interior.Color = ToOleColor(255, 255, 153); // light yellow
            });
            ApplyAllEdgeBorder(worksheet, "C3:E5", XlContinuous, XlThick, ToOleColor(180, 90, 0));

            // --- Merge with borders around the region ---
            SetExcelCellValue(worksheet, 8, 1, "Merge + all-edge border (B8:D9)");
            SetExcelCellValue(worksheet, 8, 2, "Bordered merge");
            ApplyExcelRangeFormat(worksheet, "B8:D9", r =>
            {
                r.Merge();
                r.HorizontalAlignment = XlHAlignCenter;
                r.VerticalAlignment   = XlVAlignCenter;
            });
            ApplyAllEdgeBorder(worksheet, "B8:D9", XlDash, XlMedium, ToOleColor(128, 0, 128));

            ApplyExcelRangeFormat(worksheet, "A:E", r => { try { r.Columns.AutoFit(); } catch { } });

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // =========================================================================
    // case 007 — font styles
    // =========================================================================
    private static void GenerateCellStyleFixture_Fonts(dynamic workbooks, string outputPath)
    {
        object? workbook  = null;
        object? worksheet = null;
        try
        {
            workbook  = OpenCellStyleWorkbook(workbooks, outputPath, "Fonts", out object ws);
            worksheet = ws;

            SetExcelCellValue(worksheet, 1, 1, "Feature");
            SetExcelCellValue(worksheet, 1, 2, "Sample");
            ApplyExcelRangeFormat(worksheet, "A1:B1", r => r.Font.Bold = true);

            // Row 2: Bold
            SetExcelCellValue(worksheet, 2, 1, "Bold");
            SetExcelCellValue(worksheet, 2, 2, "Bold text");
            ApplyExcelRangeFormat(worksheet, "B2", r => r.Font.Bold = true);

            // Row 3: Italic
            SetExcelCellValue(worksheet, 3, 1, "Italic");
            SetExcelCellValue(worksheet, 3, 2, "Italic text");
            ApplyExcelRangeFormat(worksheet, "B3", r => r.Font.Italic = true);

            // Row 4: Bold + Italic
            SetExcelCellValue(worksheet, 4, 1, "Bold + Italic");
            SetExcelCellValue(worksheet, 4, 2, "Bold italic");
            ApplyExcelRangeFormat(worksheet, "B4", r =>
            {
                r.Font.Bold   = true;
                r.Font.Italic = true;
            });

            // Row 5: Single underline
            SetExcelCellValue(worksheet, 5, 1, "Single underline");
            SetExcelCellValue(worksheet, 5, 2, "Underlined");
            ApplyExcelRangeFormat(worksheet, "B5", r => r.Font.Underline = XlUnderlineStyleSingle);

            // Row 6: Double underline
            SetExcelCellValue(worksheet, 6, 1, "Double underline");
            SetExcelCellValue(worksheet, 6, 2, "Dbl Underline");
            ApplyExcelRangeFormat(worksheet, "B6", r => r.Font.Underline = XlUnderlineStyleDouble);

            // Row 7: Strikethrough
            SetExcelCellValue(worksheet, 7, 1, "Strikethrough");
            SetExcelCellValue(worksheet, 7, 2, "Struck through");
            ApplyExcelRangeFormat(worksheet, "B7", r => r.Font.Strikethrough = true);

            // Row 8: Superscript — use formula text so it's visible; COM sets per-char via Characters
            SetExcelCellValue(worksheet, 8, 1, "Superscript (char)");
            SetExcelCellValue(worksheet, 8, 2, "X2");
            // Apply superscript only to the "2" character (index 2, length 1)
            try
            {
                ApplyExcelRangeFormat(worksheet, "B8", r =>
                {
                    object? chars = null;
                    object? font  = null;
                    try
                    {
                        chars = r.Characters(2, 1);
                        font  = ((dynamic)chars).Font;
                        ((dynamic)font).Superscript = true;
                    }
                    finally
                    {
                        ReleaseComObject(font);
                        ReleaseComObject(chars);
                    }
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  COM note: superscript character failed: {ex.Message}");
            }

            // Row 9: Subscript — cell-level (applies to all chars)
            SetExcelCellValue(worksheet, 9, 1, "Subscript (cell)");
            SetExcelCellValue(worksheet, 9, 2, "H2O");
            ApplyExcelRangeFormat(worksheet, "B9", r => r.Font.Subscript = true);

            // Row 10: Colored font (red)
            SetExcelCellValue(worksheet, 10, 1, "Red font");
            SetExcelCellValue(worksheet, 10, 2, "Red text");
            ApplyExcelRangeFormat(worksheet, "B10", r => r.Font.Color = ToOleColor(255, 0, 0));

            // Row 11: Font size 18
            SetExcelCellValue(worksheet, 11, 1, "Size 18");
            SetExcelCellValue(worksheet, 11, 2, "Large text");
            ApplyExcelRangeFormat(worksheet, "B11", r => r.Font.Size = 18);

            // Row 12: Font size 8
            SetExcelCellValue(worksheet, 12, 1, "Size 8");
            SetExcelCellValue(worksheet, 12, 2, "Small text");
            ApplyExcelRangeFormat(worksheet, "B12", r => r.Font.Size = 8);

            // Row 13: Different font name (Courier New)
            SetExcelCellValue(worksheet, 13, 1, "Courier New");
            SetExcelCellValue(worksheet, 13, 2, "Monospace text");
            ApplyExcelRangeFormat(worksheet, "B13", r => r.Font.Name = "Courier New");

            // Row 14: All combined — bold+italic+underline+red+size14
            SetExcelCellValue(worksheet, 14, 1, "Combined styles");
            SetExcelCellValue(worksheet, 14, 2, "Bold Italic Red U/L 14pt");
            ApplyExcelRangeFormat(worksheet, "B14", r =>
            {
                r.Font.Bold      = true;
                r.Font.Italic    = true;
                r.Font.Underline = XlUnderlineStyleSingle;
                r.Font.Color     = ToOleColor(180, 0, 0);
                r.Font.Size      = 14;
            });

            ApplyExcelRangeFormat(worksheet, "A:B", r => { try { r.Columns.AutoFit(); } catch { } });

            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // =========================================================================
    // Drawing-objects (shapes) corpus fixtures
    // =========================================================================

    // msoAutoShape type constants (MsoAutoShapeType enum values from Office PIA)
    private const int MsoShapeRectangle              = 1;    // msoBevelRectangle not needed; plain rect
    private const int MsoShapeRoundedRectangle       = 5;
    private const int MsoShapeOval                   = 9;
    private const int MsoShapeIsoscelesTriangle      = 7;
    private const int MsoShapeDiamond                = 4;
    private const int MsoShapeRightArrow             = 13;
    private const int MsoShapeLeftRightArrow         = 37;
    private const int MsoShapePentagon               = 56;   // msoShapeChevron=52; pentagon=56
    private const int MsoShapeChevron                = 52;
    private const int MsoShapeFlowchartProcess       = 109;
    private const int MsoShapeFlowchartDecision      = 110;
    private const int MsoShape5PointStar             = 92;
    private const int MsoShapeExplosion1             = 89;
    private const int MsoShapeRectangularCallout     = 61;
    private const int MsoShapeOvalCallout            = 107;

    // MsoLineDashStyle
    private const int MsoLineSolid               = 1;
    private const int MsoLineDash                = 4;
    private const int MsoLineDashDot             = 5;
    private const int MsoLineLongDash            = 7;

    // MsoTextOrientation (reuse existing MsoTextOrientationHorizontal = 1)

    /// <summary>Returns output paths for all nine shapes corpus fixtures.</summary>
    public static IReadOnlyList<string> GetExcelShapesCorpusFixturePaths(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        return
        [
            Path.Combine(outputDirectory, "Excel_native_shapes_basic_001.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_shapes_arrows_flow_002.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_shapes_stars_callouts_003.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_shapes_fill_outline_004.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_shapes_rotation_005.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_shapes_text_006.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_shapes_line_conn_007.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_shapes_picture_008.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_shapes_wordart_009.xlsx"),
        ];
    }

    /// <summary>Per-file dispatch for shapes corpus fixtures.</summary>
    private static void GenerateExcelNativeShapesCorpusFixture(dynamic workbooks, string outputPath, string fileName)
    {
        if (fileName.Contains("basic_001", StringComparison.OrdinalIgnoreCase))
            GenerateShapesFixture_Basic(workbooks, outputPath);
        else if (fileName.Contains("arrows_flow_002", StringComparison.OrdinalIgnoreCase))
            GenerateShapesFixture_ArrowsFlow(workbooks, outputPath);
        else if (fileName.Contains("stars_callouts_003", StringComparison.OrdinalIgnoreCase))
            GenerateShapesFixture_StarsCallouts(workbooks, outputPath);
        else if (fileName.Contains("fill_outline_004", StringComparison.OrdinalIgnoreCase))
            GenerateShapesFixture_FillOutline(workbooks, outputPath);
        else if (fileName.Contains("rotation_005", StringComparison.OrdinalIgnoreCase))
            GenerateShapesFixture_Rotation(workbooks, outputPath);
        else if (fileName.Contains("text_006", StringComparison.OrdinalIgnoreCase))
            GenerateShapesFixture_Text(workbooks, outputPath);
        else if (fileName.Contains("line_conn_007", StringComparison.OrdinalIgnoreCase))
            GenerateShapesFixture_LineConn(workbooks, outputPath);
        else if (fileName.Contains("picture_008", StringComparison.OrdinalIgnoreCase))
            GenerateShapesFixture_Picture(workbooks, outputPath);
        else if (fileName.Contains("wordart_009", StringComparison.OrdinalIgnoreCase))
            GenerateShapesFixture_WordArt(workbooks, outputPath);
        else
            throw new ArgumentException($"Unknown shapes corpus fixture: {fileName}");
    }

    // -------------------------------------------------------------------------
    // Shared helper: open a blank workbook for shapes fixtures
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes a spacer string to cell (row, col) so that Excel's UsedRange covers
    /// the entire shape region.  Without at least one cell with content at the
    /// bottom-right of where shapes are placed, GetUsedRange() collapses to A1
    /// and CopyPicture fails with 0x800A03EC.
    /// </summary>
    private static void AnchorShapeUsedRange(object worksheet, int row = 22, int col = 14)
    {
        // Write a zero-width-space so it registers as data without showing visually.
        SetExcelCellValue(worksheet, row, col, " ");
    }

    private static object OpenShapesWorkbook(dynamic workbooks, string outputPath, string sheetName,
        out object worksheet)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        if (File.Exists(outputPath)) File.Delete(outputPath);
        var workbook = workbooks.Add();
        worksheet = ((dynamic)workbook).Worksheets[1];
        try { ((dynamic)worksheet).Name = sheetName; } catch { /* best effort */ }
        return workbook;
    }

    /// <summary>
    /// Add an AutoShape to the worksheet via Shapes.AddShape.
    /// Returns the shape COM object (caller must ReleaseComObject).
    /// left/top/width/height are in points (1 pt = 1/72 inch).
    /// </summary>
    private static object AddAutoShape(object worksheet, int msoShapeType, float left, float top, float width, float height)
    {
        object? shapes = null;
        try
        {
            shapes = ((dynamic)worksheet).Shapes;
            return ((dynamic)shapes).AddShape(msoShapeType, left, top, width, height);
        }
        finally
        {
            ReleaseComObject(shapes);
        }
    }

    /// <summary>Set a solid RGB fill on a shape object (already obtained).</summary>
    private static void SetShapeSolidFill(object shape, byte r, byte g, byte b)
    {
        object? fill = null;
        try
        {
            fill = ((dynamic)shape).Fill;
            ((dynamic)fill).Solid();
            ((dynamic)fill).ForeColor.RGB = ToOleColor(r, g, b);
        }
        finally
        {
            ReleaseComObject(fill);
        }
    }

    /// <summary>Set outline (line) color and weight on a shape.</summary>
    private static void SetShapeOutline(object shape, byte r, byte g, byte b, float weight = 1.5f)
    {
        object? line = null;
        try
        {
            line = ((dynamic)shape).Line;
            ((dynamic)line).Visible = true;           // msoTrue = -1
            ((dynamic)line).ForeColor.RGB = ToOleColor(r, g, b);
            ((dynamic)line).Weight = weight;
        }
        finally
        {
            ReleaseComObject(line);
        }
    }

    /// <summary>Remove the outline from a shape.</summary>
    private static void SetShapeNoOutline(object shape)
    {
        object? line = null;
        try
        {
            line = ((dynamic)shape).Line;
            ((dynamic)line).Visible = false;          // msoFalse = 0
        }
        finally
        {
            ReleaseComObject(line);
        }
    }

    // =========================================================================
    // case 001 — basic shape geometry
    // =========================================================================
    private static void GenerateShapesFixture_Basic(dynamic workbooks, string outputPath)
    {
        object? workbook  = null;
        object? worksheet = null;
        object? shape     = null;
        try
        {
            workbook = OpenShapesWorkbook(workbooks, outputPath, "Basic", out object ws);
            worksheet = ws;

            // Label row so capture region has something in cells
            SetExcelCellValue(worksheet, 1, 1, "Basic Shapes");

            // Rectangle — col A area, row 3 onward, 110x60 pts each, spaced 15 pts apart
            float left = 10; float top = 36; float w = 110; float h = 60; float gap = 15;

            // Rectangle
            shape = AddAutoShape(worksheet, MsoShapeRectangle, left, top, w, h);
            ((dynamic)shape).Name = "Rect";
            SetShapeSolidFill(shape, 91, 155, 213);   // blue
            SetShapeOutline(shape, 47, 85, 151);
            ReleaseComObject(shape); shape = null;

            // Rounded rectangle
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeRoundedRectangle, left, top, w, h);
            ((dynamic)shape).Name = "RoundedRect";
            SetShapeSolidFill(shape, 255, 192, 0);    // gold
            SetShapeOutline(shape, 192, 144, 0);
            ReleaseComObject(shape); shape = null;

            // Ellipse
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeOval, left, top, w, h);
            ((dynamic)shape).Name = "Ellipse";
            SetShapeSolidFill(shape, 112, 173, 71);   // green
            SetShapeOutline(shape, 84, 130, 53);
            ReleaseComObject(shape); shape = null;

            // Triangle
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeIsoscelesTriangle, left, top, w, h);
            ((dynamic)shape).Name = "Triangle";
            SetShapeSolidFill(shape, 255, 102, 0);    // orange
            SetShapeOutline(shape, 200, 77, 0);
            ReleaseComObject(shape); shape = null;

            // Diamond
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeDiamond, left, top, w, h);
            ((dynamic)shape).Name = "Diamond";
            SetShapeSolidFill(shape, 155, 99, 178);   // purple
            SetShapeOutline(shape, 116, 74, 133);
            ReleaseComObject(shape); shape = null;

            AnchorShapeUsedRange(worksheet);
            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(shape);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // =========================================================================
    // case 002 — arrows and flowchart shapes
    // =========================================================================
    private static void GenerateShapesFixture_ArrowsFlow(dynamic workbooks, string outputPath)
    {
        object? workbook  = null;
        object? worksheet = null;
        object? shape     = null;
        try
        {
            workbook = OpenShapesWorkbook(workbooks, outputPath, "ArrowsFlow", out object ws);
            worksheet = ws;

            SetExcelCellValue(worksheet, 1, 1, "Arrows & Flowchart");

            float left = 10; float top = 36; float w = 100; float h = 55; float gap = 15;

            // Right arrow
            shape = AddAutoShape(worksheet, MsoShapeRightArrow, left, top, w, h);
            ((dynamic)shape).Name = "RightArrow";
            SetShapeSolidFill(shape, 91, 155, 213);
            SetShapeOutline(shape, 47, 85, 151);
            ReleaseComObject(shape); shape = null;

            // Left-right arrow
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeLeftRightArrow, left, top, w, h);
            ((dynamic)shape).Name = "LeftRightArrow";
            SetShapeSolidFill(shape, 255, 192, 0);
            SetShapeOutline(shape, 192, 144, 0);
            ReleaseComObject(shape); shape = null;

            // Chevron (pentagon-like)
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeChevron, left, top, w, h);
            ((dynamic)shape).Name = "Chevron";
            SetShapeSolidFill(shape, 112, 173, 71);
            SetShapeOutline(shape, 84, 130, 53);
            ReleaseComObject(shape); shape = null;

            // Flowchart process
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeFlowchartProcess, left, top, w, h);
            ((dynamic)shape).Name = "FlowProcess";
            SetShapeSolidFill(shape, 255, 102, 0);
            SetShapeOutline(shape, 200, 77, 0);
            ReleaseComObject(shape); shape = null;

            // Flowchart decision
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeFlowchartDecision, left, top, w, h);
            ((dynamic)shape).Name = "FlowDecision";
            SetShapeSolidFill(shape, 155, 99, 178);
            SetShapeOutline(shape, 116, 74, 133);
            ReleaseComObject(shape); shape = null;

            AnchorShapeUsedRange(worksheet);
            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(shape);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // =========================================================================
    // case 003 — stars and callouts
    // =========================================================================
    private static void GenerateShapesFixture_StarsCallouts(dynamic workbooks, string outputPath)
    {
        object? workbook  = null;
        object? worksheet = null;
        object? shape     = null;
        try
        {
            workbook = OpenShapesWorkbook(workbooks, outputPath, "StarsCallouts", out object ws);
            worksheet = ws;

            SetExcelCellValue(worksheet, 1, 1, "Stars & Callouts");

            float left = 10; float top = 36; float w = 100; float h = 80; float gap = 20;

            // 5-point star
            shape = AddAutoShape(worksheet, MsoShape5PointStar, left, top, w, h);
            ((dynamic)shape).Name = "Star5";
            SetShapeSolidFill(shape, 255, 192, 0);
            SetShapeOutline(shape, 192, 144, 0);
            ReleaseComObject(shape); shape = null;

            // Explosion
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeExplosion1, left, top, w, h);
            ((dynamic)shape).Name = "Explosion";
            SetShapeSolidFill(shape, 255, 75, 75);
            SetShapeOutline(shape, 192, 0, 0);
            ReleaseComObject(shape); shape = null;

            // Rectangular callout
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeRectangularCallout, left, top, w, h);
            ((dynamic)shape).Name = "RectCallout";
            SetShapeSolidFill(shape, 91, 155, 213);
            SetShapeOutline(shape, 47, 85, 151);
            ReleaseComObject(shape); shape = null;

            // Oval callout
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeOvalCallout, left, top, w, h);
            ((dynamic)shape).Name = "OvalCallout";
            SetShapeSolidFill(shape, 112, 173, 71);
            SetShapeOutline(shape, 84, 130, 53);
            ReleaseComObject(shape); shape = null;

            AnchorShapeUsedRange(worksheet);
            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(shape);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // =========================================================================
    // case 004 — fill and outline variations
    // =========================================================================
    private static void GenerateShapesFixture_FillOutline(dynamic workbooks, string outputPath)
    {
        object? workbook  = null;
        object? worksheet = null;
        object? shape     = null;
        try
        {
            workbook = OpenShapesWorkbook(workbooks, outputPath, "FillOutline", out object ws);
            worksheet = ws;

            SetExcelCellValue(worksheet, 1, 1, "Fill & Outline Variants");

            float left = 10; float top = 36; float w = 100; float h = 60; float gap = 18;

            // 1. Solid fill + medium outline
            shape = AddAutoShape(worksheet, MsoShapeRectangle, left, top, w, h);
            ((dynamic)shape).Name = "SolidFill";
            SetShapeSolidFill(shape, 91, 155, 213);
            SetShapeOutline(shape, 47, 85, 151, 2f);
            ReleaseComObject(shape); shape = null;

            // 2. Gradient fill (two-color linear)
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeRectangle, left, top, w, h);
            ((dynamic)shape).Name = "GradientFill";
            {
                object? fill = null;
                try
                {
                    fill = ((dynamic)shape).Fill;
                    // TwoColorGradient: msoGradientHorizontal=1, msoGradientVariant=1
                    ((dynamic)fill).TwoColorGradient(1 /*msoGradientHorizontal*/, 1);
                    ((dynamic)fill).ForeColor.RGB = ToOleColor(91, 155, 213);
                    ((dynamic)fill).BackColor.RGB = ToOleColor(255, 255, 255);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  COM note: gradient fill failed: {ex.Message}");
                    // Fall back to solid
                    try
                    {
                        object? f2 = ((dynamic)shape).Fill;
                        try { ((dynamic)f2).Solid(); ((dynamic)f2).ForeColor.RGB = ToOleColor(91, 155, 213); }
                        finally { ReleaseComObject(f2); }
                    }
                    catch { }
                }
                finally
                {
                    ReleaseComObject(fill);
                }
            }
            SetShapeOutline(shape, 47, 85, 151, 1.5f);
            ReleaseComObject(shape); shape = null;

            // 3. No fill (transparent) + outline
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeRectangle, left, top, w, h);
            ((dynamic)shape).Name = "NoFill";
            {
                object? fill = null;
                try
                {
                    fill = ((dynamic)shape).Fill;
                    ((dynamic)fill).Visible = false;  // msoFalse=0
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  COM note: no-fill failed: {ex.Message}");
                }
                finally
                {
                    ReleaseComObject(fill);
                }
            }
            SetShapeOutline(shape, 47, 85, 151, 2f);
            ReleaseComObject(shape); shape = null;

            // 4. Thick colored outline (red, 4pt)
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeRectangle, left, top, w, h);
            ((dynamic)shape).Name = "ThickOutline";
            SetShapeSolidFill(shape, 255, 255, 255);
            SetShapeOutline(shape, 255, 0, 0, 4f);
            ReleaseComObject(shape); shape = null;

            // 5. Dashed outline
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeRectangle, left, top, w, h);
            ((dynamic)shape).Name = "DashedOutline";
            SetShapeSolidFill(shape, 255, 255, 204);  // light yellow
            {
                object? line = null;
                try
                {
                    line = ((dynamic)shape).Line;
                    ((dynamic)line).Visible = true;
                    ((dynamic)line).ForeColor.RGB = ToOleColor(0, 0, 0);
                    ((dynamic)line).Weight = 1.5f;
                    try { ((dynamic)line).DashStyle = MsoLineDash; }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"  COM note: DashStyle failed: {ex.Message}");
                    }
                }
                finally
                {
                    ReleaseComObject(line);
                }
            }
            ReleaseComObject(shape); shape = null;

            // 6. No outline
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeRectangle, left, top, w, h);
            ((dynamic)shape).Name = "NoOutline";
            SetShapeSolidFill(shape, 112, 173, 71);
            SetShapeNoOutline(shape);
            ReleaseComObject(shape); shape = null;

            AnchorShapeUsedRange(worksheet);
            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(shape);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // =========================================================================
    // case 005 — rotation and flip
    // =========================================================================
    private static void GenerateShapesFixture_Rotation(dynamic workbooks, string outputPath)
    {
        object? workbook  = null;
        object? worksheet = null;
        object? shape     = null;
        try
        {
            workbook = OpenShapesWorkbook(workbooks, outputPath, "Rotation", out object ws);
            worksheet = ws;

            SetExcelCellValue(worksheet, 1, 1, "Rotation & Flip");

            float left = 20; float top = 50; float w = 100; float h = 60; float gap = 25;

            // 30 degrees
            shape = AddAutoShape(worksheet, MsoShapeRectangle, left, top, w, h);
            ((dynamic)shape).Name = "Rot30";
            SetShapeSolidFill(shape, 91, 155, 213);
            SetShapeOutline(shape, 47, 85, 151);
            try { ((dynamic)shape).Rotation = 30f; } catch (Exception ex) { Console.Error.WriteLine($"  COM note: Rotation=30 failed: {ex.Message}"); }
            ReleaseComObject(shape); shape = null;

            // 45 degrees
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeRectangle, left, top, w, h);
            ((dynamic)shape).Name = "Rot45";
            SetShapeSolidFill(shape, 255, 192, 0);
            SetShapeOutline(shape, 192, 144, 0);
            try { ((dynamic)shape).Rotation = 45f; } catch (Exception ex) { Console.Error.WriteLine($"  COM note: Rotation=45 failed: {ex.Message}"); }
            ReleaseComObject(shape); shape = null;

            // 90 degrees
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeRectangle, left, top, w, h);
            ((dynamic)shape).Name = "Rot90";
            SetShapeSolidFill(shape, 112, 173, 71);
            SetShapeOutline(shape, 84, 130, 53);
            try { ((dynamic)shape).Rotation = 90f; } catch (Exception ex) { Console.Error.WriteLine($"  COM note: Rotation=90 failed: {ex.Message}"); }
            ReleaseComObject(shape); shape = null;

            // Flip horizontal (right arrow so flip is visible)
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeRightArrow, left, top, w, h);
            ((dynamic)shape).Name = "FlipH";
            SetShapeSolidFill(shape, 255, 102, 0);
            SetShapeOutline(shape, 200, 77, 0);
            try { ((dynamic)shape).Flip(0 /* msoFlipHorizontal */); } catch (Exception ex) { Console.Error.WriteLine($"  COM note: FlipH failed: {ex.Message}"); }
            ReleaseComObject(shape); shape = null;

            // Flip vertical
            left += w + gap;
            shape = AddAutoShape(worksheet, MsoShapeIsoscelesTriangle, left, top, w, h);
            ((dynamic)shape).Name = "FlipV";
            SetShapeSolidFill(shape, 155, 99, 178);
            SetShapeOutline(shape, 116, 74, 133);
            try { ((dynamic)shape).Flip(1 /* msoFlipVertical */); } catch (Exception ex) { Console.Error.WriteLine($"  COM note: FlipV failed: {ex.Message}"); }
            ReleaseComObject(shape); shape = null;

            AnchorShapeUsedRange(worksheet);
            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(shape);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // =========================================================================
    // case 006 — shape text (rectangle, ellipse, textbox)
    // =========================================================================
    private static void GenerateShapesFixture_Text(dynamic workbooks, string outputPath)
    {
        object? workbook  = null;
        object? worksheet = null;
        object? shape     = null;
        try
        {
            workbook = OpenShapesWorkbook(workbooks, outputPath, "Text", out object ws);
            worksheet = ws;

            SetExcelCellValue(worksheet, 1, 1, "Shape Text");

            // Rectangle with text
            shape = AddAutoShape(worksheet, MsoShapeRectangle, 10, 36, 160, 70);
            ((dynamic)shape).Name = "RectText";
            SetShapeSolidFill(shape, 91, 155, 213);
            SetShapeOutline(shape, 47, 85, 151);
            try
            {
                object? tf2 = null;
                object? tr = null;
                try
                {
                    tf2 = ((dynamic)shape).TextFrame2;
                    tr  = ((dynamic)tf2).TextRange;
                    ((dynamic)tr).Text = "Hello Shape";
                    ((dynamic)tr).Font.Bold = true;
                    ((dynamic)tr).Font.Size = 14;
                    ((dynamic)tr).Font.Fill.ForeColor.RGB = ToOleColor(255, 255, 255);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  COM note: TextFrame2 on rect failed: {ex.Message}; trying TextFrame");
                    // Fallback to legacy TextFrame
                    try
                    {
                        object? tf = ((dynamic)shape).TextFrame;
                        object? chars = null;
                        try
                        {
                            chars = ((dynamic)tf).Characters();
                            ((dynamic)chars).Text = "Hello Shape";
                            ((dynamic)chars).Font.Bold = true;
                            ((dynamic)chars).Font.Size = 14;
                        }
                        finally
                        {
                            ReleaseComObject(chars);
                            ReleaseComObject(tf);
                        }
                    }
                    catch (Exception ex2)
                    {
                        Console.Error.WriteLine($"  COM note: TextFrame fallback also failed: {ex2.Message}");
                    }
                }
                finally
                {
                    ReleaseComObject(tr);
                    ReleaseComObject(tf2);
                }
            }
            catch { }
            ReleaseComObject(shape); shape = null;

            // Ellipse with text
            shape = AddAutoShape(worksheet, MsoShapeOval, 185, 36, 160, 70);
            ((dynamic)shape).Name = "EllipseText";
            SetShapeSolidFill(shape, 112, 173, 71);
            SetShapeOutline(shape, 84, 130, 53);
            try
            {
                object? tf2 = null;
                object? tr = null;
                try
                {
                    tf2 = ((dynamic)shape).TextFrame2;
                    tr  = ((dynamic)tf2).TextRange;
                    ((dynamic)tr).Text = "Ellipse";
                    ((dynamic)tr).Font.Bold = false;
                    ((dynamic)tr).Font.Size = 12;
                    ((dynamic)tr).Font.Fill.ForeColor.RGB = ToOleColor(255, 255, 255);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  COM note: TextFrame2 on ellipse failed: {ex.Message}");
                    try
                    {
                        object? tf = ((dynamic)shape).TextFrame;
                        object? chars = null;
                        try
                        {
                            chars = ((dynamic)tf).Characters();
                            ((dynamic)chars).Text = "Ellipse";
                        }
                        finally
                        {
                            ReleaseComObject(chars);
                            ReleaseComObject(tf);
                        }
                    }
                    catch { }
                }
                finally
                {
                    ReleaseComObject(tr);
                    ReleaseComObject(tf2);
                }
            }
            catch { }
            ReleaseComObject(shape); shape = null;

            // Textbox with text
            {
                object? shapes = null;
                object? tb = null;
                try
                {
                    shapes = ((dynamic)worksheet).Shapes;
                    tb = ((dynamic)shapes).AddTextbox(MsoTextOrientationHorizontal, 360, 36, 160, 70);
                    ((dynamic)tb).Name = "TextBox";
                    object? tf = null;
                    object? chars = null;
                    try
                    {
                        tf = ((dynamic)tb).TextFrame;
                        chars = ((dynamic)tf).Characters();
                        ((dynamic)chars).Text = "Text Box content";
                        ((dynamic)chars).Font.Size = 11;
                        ((dynamic)chars).Font.Bold = false;
                        ((dynamic)chars).Font.Color = ToOleColor(0, 0, 0);
                    }
                    finally
                    {
                        ReleaseComObject(chars);
                        ReleaseComObject(tf);
                    }
                }
                finally
                {
                    ReleaseComObject(tb);
                    ReleaseComObject(shapes);
                }
            }

            AnchorShapeUsedRange(worksheet);
            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(shape);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // =========================================================================
    // case 007 — lines and connectors
    // =========================================================================
    private static void GenerateShapesFixture_LineConn(dynamic workbooks, string outputPath)
    {
        object? workbook  = null;
        object? worksheet = null;
        try
        {
            workbook = OpenShapesWorkbook(workbooks, outputPath, "LinesConn", out object ws);
            worksheet = ws;

            SetExcelCellValue(worksheet, 1, 1, "Lines & Connectors");

            object? shapes = null;
            object? line1  = null;
            object? line2  = null;
            object? conn1  = null;
            try
            {
                shapes = ((dynamic)worksheet).Shapes;

                // Straight line (AddLine: x1,y1,x2,y2 in points)
                line1 = ((dynamic)shapes).AddLine(10, 50, 200, 50);
                ((dynamic)line1).Name = "StraightLine";
                {
                    object? ln = null;
                    try
                    {
                        ln = ((dynamic)line1).Line;
                        ((dynamic)ln).ForeColor.RGB = ToOleColor(0, 112, 192);
                        ((dynamic)ln).Weight = 2.5f;
                        // Arrowhead at end
                        try { ((dynamic)ln).EndArrowheadStyle = 2; /* msoArrowheadOpen */ } catch { }
                    }
                    finally { ReleaseComObject(ln); }
                }

                // Second straight line — dashed, thicker
                line2 = ((dynamic)shapes).AddLine(10, 90, 200, 90);
                ((dynamic)line2).Name = "DashedLine";
                {
                    object? ln = null;
                    try
                    {
                        ln = ((dynamic)line2).Line;
                        ((dynamic)ln).ForeColor.RGB = ToOleColor(255, 0, 0);
                        ((dynamic)ln).Weight = 2f;
                        try { ((dynamic)ln).DashStyle = MsoLineDash; } catch { }
                        try { ((dynamic)ln).BeginArrowheadStyle = 2; ((dynamic)ln).EndArrowheadStyle = 2; } catch { }
                    }
                    finally { ReleaseComObject(ln); }
                }

                // Elbow connector
                try
                {
                    conn1 = ((dynamic)shapes).AddConnector(2 /*msoConnectorElbow*/, 250, 36, 400, 100);
                    ((dynamic)conn1).Name = "ElbowConnector";
                    {
                        object? ln = null;
                        try
                        {
                            ln = ((dynamic)conn1).Line;
                            ((dynamic)ln).ForeColor.RGB = ToOleColor(112, 173, 71);
                            ((dynamic)ln).Weight = 2f;
                            try { ((dynamic)ln).EndArrowheadStyle = 2; } catch { }
                        }
                        finally { ReleaseComObject(ln); }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  COM note: AddConnector (elbow) failed: {ex.Message}");
                }
            }
            finally
            {
                ReleaseComObject(conn1);
                ReleaseComObject(line2);
                ReleaseComObject(line1);
                ReleaseComObject(shapes);
            }

            AnchorShapeUsedRange(worksheet);
            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    // =========================================================================
    // case 008 — picture insertion
    // =========================================================================
    private static void GenerateShapesFixture_Picture(dynamic workbooks, string outputPath)
    {
        object? workbook  = null;
        object? worksheet = null;
        object? picture   = null;
        string? tempPng   = null;
        try
        {
            workbook = OpenShapesWorkbook(workbooks, outputPath, "Picture", out object ws);
            worksheet = ws;

            SetExcelCellValue(worksheet, 1, 1, "Picture");

            // Write a minimal 10x10 PNG to a temp file
            tempPng = Path.Combine(Path.GetTempPath(), $"freex_smoke_shape_pic_{Guid.NewGuid():N}.png");
            WriteTiny10x10Png(tempPng);

            object? shapes = null;
            try
            {
                shapes = ((dynamic)worksheet).Shapes;
                // AddPicture(filename, linkToFile, saveWithDocument, left, top, width, height)
                picture = ((dynamic)shapes).AddPicture(
                    tempPng,
                    false,   // LinkToFile = msoFalse
                    true,    // SaveWithDocument = msoTrue
                    10f,     // left
                    36f,     // top
                    120f,    // width
                    80f);    // height
                ((dynamic)picture).Name = "EmbeddedPicture";
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  COM note: AddPicture failed: {ex.Message}");
            }
            finally
            {
                ReleaseComObject(picture); picture = null;
                ReleaseComObject(shapes);
            }

            AnchorShapeUsedRange(worksheet);
            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            ReleaseComObject(picture);
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
            if (tempPng is not null && File.Exists(tempPng))
                try { File.Delete(tempPng); } catch { }
        }
        Console.WriteLine($"Generated: {outputPath}");
    }

    /// <summary>
    /// Write a minimal valid 10×10 24-bit PNG to <paramref name="path"/>.
    /// Uses raw bytes so we have no external dependency.
    /// </summary>
    private static void WriteTiny10x10Png(string path)
    {
        // Minimal 10x10 solid orange PNG (hand-crafted, valid for all PNG readers).
        // Generated from Python: 10x10 RGB (255,102,0) PNG, zlib-compressed IDAT.
        // Lengths and CRCs are pre-computed.
        byte[] pngBytes =
        [
            // PNG signature
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            // IHDR chunk: 13 bytes — width=10, height=10, bit_depth=8, color_type=2 (RGB), ...
            0x00, 0x00, 0x00, 0x0D,   // length = 13
            0x49, 0x48, 0x44, 0x52,   // "IHDR"
            0x00, 0x00, 0x00, 0x0A,   // width = 10
            0x00, 0x00, 0x00, 0x0A,   // height = 10
            0x08,                     // bit depth = 8
            0x02,                     // color type = 2 (RGB)
            0x00,                     // compression = 0 (deflate)
            0x00,                     // filter = 0
            0x00,                     // interlace = 0
            0x8D, 0x5B, 0x4F, 0x5B,   // CRC32 of IHDR
            // IDAT chunk
            0x00, 0x00, 0x00, 0x25,   // length = 37
            0x49, 0x44, 0x41, 0x54,   // "IDAT"
            // zlib deflate: 10 rows, each: filter-byte=0, 10 pixels RGB(255,102,0)
            0x78, 0x9C,               // zlib header (deflate, default compression)
            0x62, 0xF8, 0xCF, 0xC0,
            0x00, 0x00, 0x00, 0xFF,
            0x00, 0xFE, 0xFF, 0xFF,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0xFF, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x03, 0xFC,
            0x00, 0xBD, 0xFE, 0xFB, 0x05,   // Adler-32
            // CRC32 of IDAT  (may be wrong for the above — use the safe approach below)
            0x00, 0x00, 0x00, 0x00,
            // IEND
            0x00, 0x00, 0x00, 0x00,
            0x49, 0x45, 0x4E, 0x44,
            0xAE, 0x42, 0x60, 0x82,
        ];
        // The embedded zlib/CRC bytes above are placeholder; build a proper PNG programmatically
        // using System.IO.Compression so it is always valid, regardless of platform endianness.
        using var ms = new System.IO.MemoryStream();
        WritePngSignature(ms);
        WritePngIhdr(ms, 10, 10);
        WritePngIdat(ms, 10, 10, 255, 102, 0);
        WritePngIend(ms);
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static void WritePngSignature(System.IO.Stream s)
    {
        s.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    }

    private static void WritePngIhdr(System.IO.Stream s, int width, int height)
    {
        var data = new byte[13];
        // width
        data[0] = (byte)(width >> 24); data[1] = (byte)(width >> 16);
        data[2] = (byte)(width >> 8);  data[3] = (byte)width;
        // height
        data[4] = (byte)(height >> 24); data[5] = (byte)(height >> 16);
        data[6] = (byte)(height >> 8);  data[7] = (byte)height;
        data[8]  = 8;  // bit depth
        data[9]  = 2;  // color type RGB
        data[10] = 0;  // compression
        data[11] = 0;  // filter
        data[12] = 0;  // interlace
        WritePngChunk(s, "IHDR"u8.ToArray(), data);
    }

    private static void WritePngIdat(System.IO.Stream s, int width, int height, byte r, byte g, byte b)
    {
        // Raw image data: for each row, a filter byte (0 = None) then RGB pixels
        var raw = new byte[height * (1 + width * 3)];
        for (var row = 0; row < height; row++)
        {
            var offset = row * (1 + width * 3);
            raw[offset] = 0; // filter type None
            for (var col = 0; col < width; col++)
            {
                raw[offset + 1 + col * 3]     = r;
                raw[offset + 1 + col * 3 + 1] = g;
                raw[offset + 1 + col * 3 + 2] = b;
            }
        }

        using var compressed = new System.IO.MemoryStream();
        using (var deflate = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(raw, 0, raw.Length);

        WritePngChunk(s, "IDAT"u8.ToArray(), compressed.ToArray());
    }

    private static void WritePngIend(System.IO.Stream s) =>
        WritePngChunk(s, "IEND"u8.ToArray(), []);

    private static void WritePngChunk(System.IO.Stream s, byte[] type, byte[] data)
    {
        var length = data.Length;
        s.Write([(byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length]);
        s.Write(type);
        s.Write(data);
        // CRC32 over type + data
        var crc = Crc32(type, data);
        s.Write([(byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc]);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var table = BuildCrc32Table();
        uint crc = 0xFFFFFFFFu;
        foreach (var b in type) crc = (crc >> 8) ^ table[(crc ^ b) & 0xFF];
        foreach (var b in data) crc = (crc >> 8) ^ table[(crc ^ b) & 0xFF];
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    // =========================================================================
    // case 009 — WordArt
    // =========================================================================
    private static void GenerateShapesFixture_WordArt(dynamic workbooks, string outputPath)
    {
        object? workbook  = null;
        object? worksheet = null;
        try
        {
            workbook = OpenShapesWorkbook(workbooks, outputPath, "WordArt", out object ws);
            worksheet = ws;

            SetExcelCellValue(worksheet, 1, 1, "WordArt");

            object? shapes = null;
            object? wa     = null;
            try
            {
                shapes = ((dynamic)worksheet).Shapes;
                // AddTextEffect(PresetTextEffect, text, fontName, fontSize, fontBold, fontItalic, left, top)
                // msoTextEffect1 = 1
                try
                {
                    wa = ((dynamic)shapes).AddTextEffect(
                        1,              // msoTextEffect1
                        "FreeX",        // text
                        "Arial Black",  // fontName
                        36f,            // fontSize
                        false,          // fontBold (msoFalse)
                        false,          // fontItalic
                        10f,            // left
                        36f);           // top
                    ((dynamic)wa).Name = "WordArt1";
                    Console.WriteLine("  COM note: WordArt AddTextEffect succeeded.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  COM note: AddTextEffect (WordArt) failed: {ex.Message}. Generating placeholder rectangle instead.");
                    // Fall back: a plain rectangle as placeholder so the fixture is still valid
                    try
                    {
                        var fallback = ((dynamic)shapes).AddShape(MsoShapeRectangle, 10f, 36f, 200f, 60f);
                        try
                        {
                            ((dynamic)fallback).Name = "WordArtPlaceholder";
                            object? fill = null;
                            try { fill = ((dynamic)fallback).Fill; ((dynamic)fill).Solid(); ((dynamic)fill).ForeColor.RGB = ToOleColor(200, 200, 200); }
                            finally { ReleaseComObject(fill); }
                            object? tf2 = null; object? tr = null;
                            try
                            {
                                tf2 = ((dynamic)fallback).TextFrame2;
                                tr  = ((dynamic)tf2).TextRange;
                                ((dynamic)tr).Text = "WordArt N/A";
                            }
                            catch
                            {
                                object? tf = null; object? chars = null;
                                try
                                {
                                    tf = ((dynamic)fallback).TextFrame;
                                    chars = ((dynamic)tf).Characters();
                                    ((dynamic)chars).Text = "WordArt N/A";
                                }
                                finally { ReleaseComObject(chars); ReleaseComObject(tf); }
                            }
                            finally { ReleaseComObject(tr); ReleaseComObject(tf2); }
                        }
                        finally { ReleaseComObject(fallback); }
                    }
                    catch (Exception ex2)
                    {
                        Console.Error.WriteLine($"  COM note: placeholder fallback also failed: {ex2.Message}");
                    }
                }
            }
            finally
            {
                ReleaseComObject(wa);
                ReleaseComObject(shapes);
            }

            AnchorShapeUsedRange(worksheet);
            SaveExcelWorkbook(workbook, outputPath);
            workbook = null;
        }
        finally
        {
            SafeCloseWorkbook(workbook);
            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
        Console.WriteLine($"Generated: {outputPath}");
    }
}
