using System.Globalization;
using System.Reflection;
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
    private const int XlTabularRow = 1;
    private const int XlOutlineRow = 2;
    private const int XlRepeatLabels = 2;
    private const int XlCaptionBeginsWith = 17;
    private const int XlValueIsGreaterThan = 9;
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
            Path.Combine(outputDirectory, "Excel_native_pivot_filters_sorts_002.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_layout_options_002.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_date_grouping_003.xlsx"),
            Path.Combine(outputDirectory, "Excel_native_pivot_calculated_field_item_003.xlsx"),
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
            else if (fileName.Contains("filters_sorts", StringComparison.OrdinalIgnoreCase))
                AddNativePivotFiltersSorts(workbook, dataSheet);
            else if (fileName.Contains("layout_options", StringComparison.OrdinalIgnoreCase))
                AddNativePivotLayoutOptions(workbook, dataSheet);
            else if (fileName.Contains("date_grouping", StringComparison.OrdinalIgnoreCase))
                AddNativePivotDateGrouping(workbook, dataSheet);
            else if (fileName.Contains("calculated_field_item", StringComparison.OrdinalIgnoreCase))
                AddNativePivotCalculatedFieldItem(workbook, dataSheet);
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
}
