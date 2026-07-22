using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

internal static partial class XlsxCorpusFixtureFactory
{
    private static Workbook CreateGridBasic()
    {
        var workbook = NewWorkbook("generated-grid-basic-001");
        var sheet = workbook.AddSheet("Grid");
        var dateTimeStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "m/d/yy h:mm" });
        Set(sheet, "A1", new TextValue("Text"));
        Set(sheet, "B1", new NumberValue(123.45));
        Set(sheet, "C1", new BoolValue(true));
        Set(sheet, "D1", DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)), dateTimeStyle);
        Set(sheet, "E1", ErrorValue.NA);
        Set(sheet, "A3", new TextValue("Sparse corner"));
        Set(sheet, "XFD10", new NumberValue(16384));
        return workbook;
    }

    private static Workbook CreateFormulas()
    {
        var workbook = NewWorkbook("generated-formulas-001");
        var sheet = workbook.AddSheet("Formulas");
        Set(sheet, "A1", new NumberValue(10));
        Set(sheet, "A2", new NumberValue(20));
        Set(sheet, "A3", new NumberValue(30));
        Formula(sheet, "B1", "SUM(A1:A3)");
        Formula(sheet, "B2", "AVERAGE(A1:A3)");
        Formula(sheet, "B3", "IF(B1>50,\"high\",\"low\")");
        Formula(sheet, "B4", "TEXT(DATE(2026,5,17),\"yyyy-mm-dd\")");
        Formula(sheet, "B5", "A1/A2");
        return workbook;
    }

    private static Workbook CreateCrossSheet()
    {
        var workbook = NewWorkbook("generated-cross-sheet-001");
        var input = workbook.AddSheet("Inputs");
        var summary = workbook.AddSheet("Summary");
        Set(input, "A1", new TextValue("North"));
        Set(input, "B1", new NumberValue(100));
        Set(input, "A2", new TextValue("South"));
        Set(input, "B2", new NumberValue(125));
        workbook.DefineNamedRange("SalesValues", Range(input, "B1", "B2"));
        Formula(summary, "A1", "SUM(Inputs!B1:B2)");
        Formula(summary, "A2", "SUM(SalesValues)");
        Formula(summary, "A3", "Inputs!A1");
        return workbook;
    }

    private static Workbook CreateFormatting()
    {
        var workbook = NewWorkbook("generated-formatting-001");
        var sheet = workbook.AddSheet("Formatting");
        var headerStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FontColor = CellColor.White,
            FillColor = new CellColor(31, 78, 121),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BorderBottom = new CellBorder(BorderStyle.Thick, new CellColor(90, 90, 90))
        });
        var currencyStyle = workbook.RegisterStyle(new CellStyle
        {
            NumberFormat = "$#,##0.00",
            HorizontalAlignment = HorizontalAlignment.Right
        });

        Set(sheet, "A1", new TextValue("Item"), headerStyle);
        Set(sheet, "B1", new TextValue("Amount"), headerStyle);
        Set(sheet, "A2", new TextValue("Revenue"));
        Set(sheet, "B2", new NumberValue(1234.5), currencyStyle);
        Set(sheet, "A4", new TextValue("Wrapped text sample"));
        sheet.GetCell(4, 1)!.StyleId = workbook.RegisterStyle(new CellStyle { WrapText = true, FontName = "Aptos", FontSize = 12 });
        return workbook;
    }

    private static Workbook CreateStructure()
    {
        var workbook = NewWorkbook("generated-structure-001");
        var sheet = workbook.AddSheet("Structure");
        Set(sheet, "A1", new TextValue("Merged heading"));
        Set(sheet, "A3", new TextValue("Visible"));
        Set(sheet, "C3", new TextValue("Hidden markers"));
        sheet.AddMergedRegion(Range(sheet, "A1", "C1"));
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 1;
        sheet.ColumnWidths[1] = 18;
        sheet.ColumnWidths[3] = 22;
        sheet.RowHeights[1] = 28;
        sheet.HiddenRows.Add(5);
        sheet.HiddenCols.Add(4);
        sheet.RowOutlineLevels[6] = 1;
        sheet.ColOutlineLevels[5] = 1;
        return workbook;
    }

    private static Workbook CreateValidation()
    {
        var workbook = NewWorkbook("generated-validation-001");
        var sheet = workbook.AddSheet("Validation");
        Set(sheet, "A1", new TextValue("Choice"));
        Set(sheet, "B1", new TextValue("Quantity"));
        Set(sheet, "A2", new TextValue("Apple"));
        Set(sheet, "B2", new NumberValue(5));
        var listValidation = new DataValidation
        {
            AppliesTo = Range(sheet, "A2", "A10"),
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            ErrorTitle = "Invalid choice",
            ErrorMessage = "Choose a listed item.",
            PromptTitle = "Pick a fruit",
            PromptMessage = "Select Apple, Banana, or Cherry."
        };
        listValidation.AdditionalRanges.Add(Range(sheet, "C2", "C10"));
        sheet.DataValidations.Add(listValidation);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, "B2", "B10"),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10"
        });
        workbook.DefineNamedRange("ValidChoices", Range(sheet, "A2", "A10"));
        return workbook;
    }

    private static Workbook CreateConditionalFormatting()
    {
        var workbook = NewWorkbook("generated-conditional-formatting-001");
        var sheet = workbook.AddSheet("Conditional Formatting");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 10));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "A1", "A5"),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "30",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(198, 239, 206), FontColor = new CellColor(0, 97, 0) }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "A1", "A5"),
            Priority = 2,
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>25",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 235, 156), FontColor = new CellColor(156, 87, 0) }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "B1", "B5"),
            Priority = 3,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 3,
            AboveAverage = true
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "C1", "C5"),
            Priority = 4,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "review",
            FormulaText = "NOT(ISERROR(SEARCH(\"review\",C1)))"
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "D1", "D5"),
            Priority = 5,
            RuleType = CfRuleType.DuplicateValues
        });
        return workbook;
    }

    private static Workbook CreateColorScales()
    {
        var workbook = NewWorkbook("generated-color-scales-001");
        var sheet = workbook.AddSheet("Color Scales");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 10));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "A1", "A5"),
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Number,
            MinThresholdValue = "0",
            MidThresholdType = CfThresholdType.Percentile,
            MidThresholdValue = "50",
            MaxThresholdType = CfThresholdType.Number,
            MaxThresholdValue = "100"
        });
        return workbook;
    }

    private static Workbook CreateDataBars()
    {
        var workbook = NewWorkbook("generated-data-bars-001");
        var sheet = workbook.AddSheet("Data Bars");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 10));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, "A1", "A5"),
            RuleType = CfRuleType.DataBar,
            DataBarMinThresholdType = CfThresholdType.Number,
            DataBarMinThresholdValue = "0",
            DataBarMaxThresholdType = CfThresholdType.Number,
            DataBarMaxThresholdValue = "100",
            DataBarShowValue = false,
            DataBarMinLength = 5,
            DataBarMaxLength = 95,
            DataBarGradient = false,
            DataBarBorder = true,
            DataBarAxisPosition = "middle",
            DataBarAxisColor = new RgbColor(90, 90, 90),
            DataBarNegativeFillColor = new RgbColor(220, 80, 80),
            DataBarNegativeBorderColor = new RgbColor(160, 40, 40)
        });
        return workbook;
    }

    private static Workbook CreateIconSets()
    {
        var workbook = NewWorkbook("generated-icon-sets-001");
        var sheet = workbook.AddSheet("Icon Sets");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 20));

        var rule = new ConditionalFormat
        {
            AppliesTo = Range(sheet, "A1", "A5"),
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "5Arrows",
            IconSetShowValue = false,
            IconSetReverse = true
        };
        rule.IconSetThresholds.AddRange(
        [
            new CfThresholdModel(CfThresholdType.Percent, "0"),
            new CfThresholdModel(CfThresholdType.Percent, "20"),
            new CfThresholdModel(CfThresholdType.Percent, "40"),
            new CfThresholdModel(CfThresholdType.Percent, "60"),
            new CfThresholdModel(CfThresholdType.Percent, "80")
        ]);
        sheet.ConditionalFormats.Add(rule);
        return workbook;
    }

    private static Workbook CreateImagesAndSparklines()
    {
        var workbook = NewWorkbook("generated-images-sparklines-001");
        var sheet = workbook.AddSheet("Images Sparklines");
        Set(sheet, "A1", new NumberValue(1));
        Set(sheet, "B1", new NumberValue(2));
        Set(sheet, "C1", new NumberValue(3));
        sheet.BackgroundImage = new WorksheetBackgroundImage(MinimalPngBytes(), "image/png", "corpus-background.png");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Corpus Image 1",
            Anchor = Addr(sheet, "E2"),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 120,
            Height = 80,
            CropLeft = 0.05,
            CropTop = 0.10,
            CropRight = 0.05,
            CropBottom = 0.10,
            Title = "Corpus image title",
            AltText = "Corpus image"
        });
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = Range(sheet, "A1", "C1"),
            Location = Addr(sheet, "D1"),
            Kind = SparklineKind.Line
        });
        return workbook;
    }

    private static Workbook CreateTextBoxesAndShapes()
    {
        var workbook = NewWorkbook("generated-text-boxes-shapes-001");
        var sheet = workbook.AddSheet("Text Shapes");
        Set(sheet, "A1", new TextValue("Drawing objects"));
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Name = "Corpus Text Box 1",
            Anchor = Addr(sheet, "B2"),
            Text = "Corpus note",
            Width = 200,
            Height = 90,
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25),
            Title = "Corpus text box title",
            AltText = "Corpus text box"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Corpus Ellipse 1",
            Anchor = Addr(sheet, "D5"),
            Kind = DrawingShapeKind.Ellipse,
            Width = 140,
            Height = 90,
            FillColor = new CellColor(221, 235, 247),
            GradientFillEndColor = new CellColor(189, 215, 238),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.5),
            HasShadowEffect = true,
            Title = "Corpus ellipse title",
            AltText = "Corpus ellipse"
        });
        return workbook;
    }

    private static Workbook CreateCommentsAndHyperlinks()
    {
        var workbook = NewWorkbook("generated-comments-hyperlinks-002");
        var sheet = workbook.AddSheet("Links Notes");
        var hyperlinkStyle = RegisterHyperlinkStyle(workbook);
        Set(sheet, "A1", new TextValue("Documentation"));
        Set(sheet, "A2", new TextValue("Release notes"));
        Set(sheet, "B1", new TextValue("Review"));
        Set(sheet, "B2", new TextValue("Follow-up"));
        sheet.Hyperlinks[Addr(sheet, "A1")] = "https://example.com/freex/docs";
        sheet.GetCell(Addr(sheet, "A1"))!.StyleId = hyperlinkStyle;
        sheet.HyperlinkMetadata[Addr(sheet, "A1")] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open the FreeX documentation");
        sheet.Hyperlinks[Addr(sheet, "A2")] = "mailto:review@example.com";
        sheet.GetCell(Addr(sheet, "A2"))!.StyleId = hyperlinkStyle;
        sheet.HyperlinkMetadata[Addr(sheet, "A2")] = new HyperlinkMetadata(
            HyperlinkTargetKind.EmailAddress,
            "Send a workbook review note");
        sheet.Hyperlinks[Addr(sheet, "B2")] = "Links Notes!A1";
        sheet.GetCell(Addr(sheet, "B2"))!.StyleId = hyperlinkStyle;
        sheet.HyperlinkMetadata[Addr(sheet, "B2")] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump to the documentation link",
            "Links Notes!A1");
        sheet.Comments[Addr(sheet, "B1")] = "Check workbook fidelity notes.";
        sheet.Comments[Addr(sheet, "B2")] = "Confirm links survived round-trip.";
        return workbook;
    }

    private static Workbook CreateMergedFreeze()
    {
        var workbook = NewWorkbook("generated-merged-freeze-002");
        var sheet = workbook.AddSheet("Merged Freeze");
        Set(sheet, "A1", new TextValue("Regional summary"));
        Set(sheet, "A3", new TextValue("North"));
        Set(sheet, "B3", new NumberValue(120));
        Set(sheet, "A4", new TextValue("South"));
        Set(sheet, "B4", new NumberValue(145));
        sheet.AddMergedRegion(Range(sheet, "A1", "D1"));
        sheet.AddMergedRegion(Range(sheet, "C3", "D4"));
        sheet.FrozenRows = 2;
        sheet.FrozenCols = 1;
        sheet.HiddenRows.Add(8);
        sheet.HiddenCols.Add(6);
        sheet.ColumnWidths[1] = 20;
        sheet.RowHeights[1] = 30;
        return workbook;
    }

    private static Workbook CreatePrintTitlesAndBreaks()
    {
        var workbook = NewWorkbook("generated-print-titles-breaks-001");
        var sheet = workbook.AddSheet("Print Setup");
        Set(sheet, "A1", new TextValue("Region"));
        Set(sheet, "B1", new TextValue("Amount"));
        Set(sheet, "A2", new TextValue("North"));
        Set(sheet, "B2", new NumberValue(100));
        Set(sheet, "A25", new TextValue("South"));
        Set(sheet, "B25", new NumberValue(125));
        sheet.PrintArea = Range(sheet, "A1", "D40");
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, 1);
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        sheet.PageHeader = new WorksheetHeaderFooter("FreeX", "Print setup", "Corpus");
        sheet.PageFooter = new WorksheetHeaderFooter("", "Page &P of &N", "");
        sheet.RowPageBreaks.Add(20);
        sheet.ColumnPageBreaks.Add(4);
        return workbook;
    }

    private static Workbook CreateNamedRangesAndFormulas()
    {
        var workbook = NewWorkbook("generated-named-ranges-formulas-002");
        var inputs = workbook.AddSheet("Inputs");
        var summary = workbook.AddSheet("Summary");
        Set(inputs, "A1", new TextValue("North"));
        Set(inputs, "B1", new NumberValue(100));
        Set(inputs, "A2", new TextValue("South"));
        Set(inputs, "B2", new NumberValue(125));
        Set(inputs, "A3", new TextValue("West"));
        Set(inputs, "B3", new NumberValue(90));
        workbook.DefineNamedRange("RevenueValues", Range(inputs, "B1", "B3"));
        workbook.DefineNamedRange("RegionLabels", Range(inputs, "A1", "A3"));
        Formula(summary, "A1", "SUM(RevenueValues)");
        Formula(summary, "A2", "AVERAGE(Inputs!B1:B3)");
        Formula(summary, "A3", "INDEX(RegionLabels,2)");
        return workbook;
    }

    private static Workbook CreateValidationCustom()
    {
        var workbook = NewWorkbook("generated-validation-custom-002");
        var sheet = workbook.AddSheet("Validation Custom");
        Set(sheet, "A1", new TextValue("Allowed"));
        Set(sheet, "A2", new TextValue("Open"));
        Set(sheet, "A3", new TextValue("Closed"));
        Set(sheet, "B1", new TextValue("Status"));
        Set(sheet, "C1", new TextValue("Ratio"));
        workbook.DefineNamedRange("StatusChoices", Range(sheet, "A2", "A3"));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, "B2", "B20"),
            Type = DvType.List,
            // Named-range List source: the in-memory model convention (matching a real range/name
            // reference formula elsewhere, e.g. R27_DataValidationListSourceTests) is a leading '=',
            // which DataValidationService.ListSources gates range/name resolution on. Real Excel
            // itself never stores the '=' in the saved <formula1> element, and the R36 IO-mapper fix
            // re-adds it on load -- so the model here must also carry it for the round-trip to match.
            Formula1 = "=StatusChoices"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, "C2", "C20"),
            Type = DvType.Decimal,
            Operator = DvOperator.Between,
            Formula1 = "0",
            Formula2 = "1"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, "D2", "D20"),
            Type = DvType.Custom,
            Formula1 = "LEN(D2)<=12"
        });
        return workbook;
    }

    private static Workbook CreateStyleOnlyCells()
    {
        var workbook = NewWorkbook("generated-style-only-cells-002");
        var sheet = workbook.AddSheet("Style Only");
        var warningStyle = workbook.RegisterStyle(new CellStyle
        {
            FillColor = new CellColor(255, 242, 204),
            FontColor = new CellColor(156, 87, 0),
            BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(191, 143, 0))
        });
        var percentStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00%" });
        sheet.SetStyleOnly(4, 4, warningStyle);
        sheet.SetStyleOnly(5, 4, warningStyle);
        Set(sheet, "A1", new TextValue("Completion"));
        Set(sheet, "B1", new NumberValue(0.875), percentStyle);
        Set(sheet, "A2", new TextValue("Empty styled cells at D4:D5"));
        return workbook;
    }

    private static Workbook CreateChartsCombo()
    {
        var workbook = NewWorkbook("generated-charts-combo-002");
        var sheet = workbook.AddSheet("Chart Mix");
        Set(sheet, "A1", new TextValue("Quarter"));
        Set(sheet, "B1", new TextValue("Revenue"));
        Set(sheet, "C1", new TextValue("Cost"));
        Set(sheet, "A2", new TextValue("Q1"));
        Set(sheet, "A3", new TextValue("Q2"));
        Set(sheet, "A4", new TextValue("Q3"));
        Set(sheet, "A5", new TextValue("Q4"));
        Set(sheet, "B2", new NumberValue(120));
        Set(sheet, "B3", new NumberValue(135));
        Set(sheet, "B4", new NumberValue(150));
        Set(sheet, "B5", new NumberValue(170));
        Set(sheet, "C2", new NumberValue(80));
        Set(sheet, "C3", new NumberValue(92));
        Set(sheet, "C4", new NumberValue(98));
        Set(sheet, "C5", new NumberValue(110));
        sheet.Charts.Add(new ChartModel { Type = ChartType.Line, DataRange = Range(sheet, "A1", "C5"), Title = "Trend", ShowLegend = true });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = Range(sheet, "A1", "C5"),
            Title = "Bar View",
            ShowLegend = true,
            BarGapWidth = 75,
            BarOverlap = -20,
            VaryColorsByPoint = true
        });
        sheet.Charts.Add(new ChartModel { Type = ChartType.Area, DataRange = Range(sheet, "A1", "C5"), Title = "Area View", ShowLegend = true });
        return workbook;
    }

    private static Workbook CreatePivotsWithFilters()
    {
        var workbook = NewWorkbook("generated-pivots-filters-002");
        workbook.NumberFormatCatalog[165] = "#,##0.0 \"kg\"";
        var sheet = workbook.AddSheet("Pivot Filters");
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
        Set(sheet, "A7", new TextValue("Region"));
        Set(sheet, "B7", new TextValue("Sum of Amount"));
        Set(sheet, "A8", new TextValue("North"));
        Set(sheet, "B8", new NumberValue(180));
        Set(sheet, "A9", new TextValue("Grand Total"));
        Set(sheet, "B9", new NumberValue(180));

        var cache = new PivotCacheModel
        {
            CacheId = 2,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:C4",
            PackagePart = "xl/pivotCache/pivotCacheDefinition2.xml",
            RefreshOnLoad = true,
            PreserveSourceSortFilter = false,
            RecordCount = 3,
            CreatedVersion = 8,
            MinRefreshableVersion = 4,
            RefreshedVersion = 8,
            RefreshedBy = "FreeX Corpus",
            RefreshedDateIso = "2026-05-24T12:34:56Z"
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["North", "South"]));
        cache.Fields.Add(new PivotCacheFieldModel("Category", ContainsString: true, SharedItems: ["Hardware", "Software", "Services"]));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", 165, ContainsNumber: true, MinValue: 80, MaxValue: 125));
        workbook.PivotCaches.Add(cache);

        var style = new PivotTableStyleModel { Name = "FreeXCorpusFilteredPivotStyle", AppliesToPivotTables = true };
        style.Elements.Add(new PivotTableStyleElementModel("wholeTable"));
        style.Elements.Add(new PivotTableStyleElementModel("headerRow"));
        workbook.PivotTableStyles.Add(style);

        var pivot = new PivotTableModel
        {
            Name = "PivotTableFiltered",
            CacheId = 2,
            SourceRange = Range(sheet, "A1", "C4"),
            TargetRange = Range(sheet, "A7", "B9"),
            PackagePart = "xl/pivotTables/pivotTable2.xml",
            StyleName = style.Name,
            ShowRowStripes = true,
            RepeatItemLabels = false,
            EnableDrill = false,
            AsteriskTotals = true,
            MultipleFieldFilters = false,
            EnableFieldDialog = false,
            EnableFieldProperties = false,
            EnableDataValueEditing = true,
            ApplyNumberFormats = false,
            ApplyBorderFormats = false,
            ApplyFontFormats = false,
            ApplyPatternFormats = false,
            DataCaption = "Corpus Values",
            GrandTotalCaption = "Corpus Grand Total",
            MissingCaption = "(corpus missing)",
            ErrorCaption = "(corpus error)"
        };
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "Hardware", SelectedItems: ["Hardware"]));
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["North"]));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum", 165, null, PivotShowValuesAs.None, null, null, "#,##0.0 \"kg\""));
        sheet.PivotTables.Add(pivot);
        return workbook;
    }

    private static Workbook CreateStructuredTableTotals()
    {
        var workbook = NewWorkbook("generated-structured-table-totals-002");
        var sheet = workbook.AddSheet("Table Totals");
        Set(sheet, "A1", new TextValue("Item"));
        Set(sheet, "B1", new TextValue("Amount"));
        Set(sheet, "A2", new TextValue("A"));
        Set(sheet, "B2", new NumberValue(10));
        Set(sheet, "A3", new TextValue("B"));
        Set(sheet, "B3", new NumberValue(20));
        Set(sheet, "A4", new TextValue("Total"));
        Set(sheet, "B4", new NumberValue(30));

        var table = new StructuredTableModel
        {
            Id = 2,
            Name = "SalesTotals",
            DisplayName = "SalesTotals",
            Range = Range(sheet, "A1", "B4"),
            HasAutoFilter = true,
            TotalsRowShown = true,
            StyleName = "TableStyleMedium9",
            ShowRowStripes = true,
            ShowFirstColumn = true,
            PackagePart = "xl/tables/table2.xml"
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Item", TotalsRowLabel: "Total"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount", TotalsRowFunction: "sum"));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["A", "B"]));
        sheet.StructuredTables.Add(table);
        return workbook;
    }

    private static Workbook CreateImagesAndSparklinesVariant()
    {
        var workbook = NewWorkbook("generated-images-sparklines-002");
        var sheet = workbook.AddSheet("Visual Data");
        Set(sheet, "A1", new NumberValue(5));
        Set(sheet, "B1", new NumberValue(7));
        Set(sheet, "C1", new NumberValue(9));
        Set(sheet, "A2", new NumberValue(3));
        Set(sheet, "B2", new NumberValue(4));
        Set(sheet, "C2", new NumberValue(8));
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Additional Corpus Image 1",
            Anchor = Addr(sheet, "F2"),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 80,
            Height = 80,
            Title = "Additional corpus image title",
            AltText = "Additional corpus image"
        });
        sheet.Sparklines.Add(new SparklineModel { DataRange = Range(sheet, "A1", "C1"), Location = Addr(sheet, "D1"), Kind = SparklineKind.Line });
        sheet.Sparklines.Add(new SparklineModel { DataRange = Range(sheet, "A2", "C2"), Location = Addr(sheet, "D2"), Kind = SparklineKind.Column });
        return workbook;
    }

    private static Workbook CreateObjects()
    {
        var workbook = NewWorkbook("generated-objects-001");
        var sheet = workbook.AddSheet("Objects");
        var hyperlinkStyle = RegisterHyperlinkStyle(workbook);
        Set(sheet, "A1", new TextValue("Documentation"));
        Set(sheet, "B1", new TextValue("Review note"));
        sheet.Hyperlinks[Addr(sheet, "A1")] = "https://example.com/freex";
        sheet.GetCell(Addr(sheet, "A1"))!.StyleId = hyperlinkStyle;
        sheet.Comments[Addr(sheet, "B1")] = "Round-trip comment fixture";
        return workbook;
    }

    private static Workbook CreateCharts()
    {
        var workbook = NewWorkbook("generated-charts-001");
        var sheet = workbook.AddSheet("Charts");
        Set(sheet, "A1", new TextValue("Month"));
        Set(sheet, "B1", new TextValue("Sales"));
        Set(sheet, "C1", new TextValue("Margin"));
        Set(sheet, "D1", new TextValue("Open"));
        Set(sheet, "E1", new TextValue("High"));
        Set(sheet, "F1", new TextValue("Low"));
        Set(sheet, "G1", new TextValue("Close"));
        Set(sheet, "I1", new TextValue("Date"));
        Set(sheet, "J1", new TextValue("Volume"));
        Set(sheet, "K1", new TextValue("Open"));
        Set(sheet, "L1", new TextValue("High"));
        Set(sheet, "M1", new TextValue("Low"));
        Set(sheet, "N1", new TextValue("Close"));
        Set(sheet, "A2", new TextValue("Jan"));
        Set(sheet, "A3", new TextValue("Feb"));
        Set(sheet, "A4", new TextValue("Mar"));
        Set(sheet, "I2", new TextValue("2026-01-02"));
        Set(sheet, "I3", new TextValue("2026-01-05"));
        Set(sheet, "I4", new TextValue("2026-01-06"));
        Set(sheet, "B2", new NumberValue(100));
        Set(sheet, "B3", new NumberValue(120));
        Set(sheet, "B4", new NumberValue(140));
        Set(sheet, "C2", new NumberValue(0.2));
        Set(sheet, "C3", new NumberValue(0.25));
        Set(sheet, "C4", new NumberValue(0.3));
        Set(sheet, "D2", new NumberValue(101));
        Set(sheet, "D3", new NumberValue(121));
        Set(sheet, "D4", new NumberValue(139));
        Set(sheet, "E2", new NumberValue(108));
        Set(sheet, "E3", new NumberValue(128));
        Set(sheet, "E4", new NumberValue(145));
        Set(sheet, "F2", new NumberValue(98));
        Set(sheet, "F3", new NumberValue(118));
        Set(sheet, "F4", new NumberValue(135));
        Set(sheet, "G2", new NumberValue(106));
        Set(sheet, "G3", new NumberValue(126));
        Set(sheet, "G4", new NumberValue(142));
        Set(sheet, "J2", new NumberValue(1000));
        Set(sheet, "J3", new NumberValue(1200));
        Set(sheet, "J4", new NumberValue(1400));
        Set(sheet, "K2", new NumberValue(101));
        Set(sheet, "K3", new NumberValue(121));
        Set(sheet, "K4", new NumberValue(139));
        Set(sheet, "L2", new NumberValue(108));
        Set(sheet, "L3", new NumberValue(128));
        Set(sheet, "L4", new NumberValue(145));
        Set(sheet, "M2", new NumberValue(98));
        Set(sheet, "M3", new NumberValue(118));
        Set(sheet, "M4", new NumberValue(135));
        Set(sheet, "N2", new NumberValue(106));
        Set(sheet, "N3", new NumberValue(126));
        Set(sheet, "N4", new NumberValue(142));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = Range(sheet, "A1", "C4"),
            Title = "Sales by Month",
            XAxisTitle = "Month",
            YAxisTitle = "Sales",
            ChartTitleTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.2),
            ChartTitleFontSize = 18,
            AxisTitleTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25),
            AxisTitleFontSize = 12.5,
            ChartAreaFillColor = new CellColor(250, 250, 250),
            PlotAreaFillColor = new CellColor(242, 242, 242),
            PlotAreaBorderColor = new CellColor(191, 191, 191),
            PlotAreaBorderThickness = 1.25,
            ChartStyleId = 42,
            RoundedCorners = true,
            BlankDisplayMode = ChartBlankDisplayMode.Zero,
            ShowDataInHiddenRowsAndColumns = true,
            ShowLegend = true,
            ShowDataLabels = true,
            DataLabelPosition = ChartDataLabelPosition.OutsideEnd,
            DataLabelSeparator = ChartDataLabelSeparator.Semicolon,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Currency,
            ShowDataLabelCallouts = true,
            DataLabelFillColor = new CellColor(255, 255, 225),
            DataLabelBorderColor = new CellColor(128, 128, 128),
            DataLabelTextColor = new CellColor(30, 30, 30),
            DataLabelBorderThickness = 1.5,
            DataLabelFontSize = 13,
            DataLabelAngle = -35,
            ShowLinearTrendline = true,
            TrendlineType = ChartTrendlineType.Polynomial,
            TrendlineOrder = 3,
            ShowTrendlineEquation = true,
            ShowTrendlineRSquared = true,
            TrendlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent6),
            TrendlineThickness = 2.25,
            TrendlineDashStyle = ChartLineDashStyle.Dot,
            ShowErrorBars = true,
            ErrorBarKind = ChartErrorBarKind.Percentage,
            ErrorBarDirection = ChartErrorBarDirection.Plus,
            ErrorBarValue = 12.5,
            ErrorBarEndCaps = false,
            ErrorBarThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
            ErrorBarThickness = 2,
            ErrorBarDashStyle = ChartLineDashStyle.Dash,
            LegendPosition = ChartLegendPosition.Bottom,
            LegendTextColor = new CellColor(64, 64, 64),
            LegendFillColor = new CellColor(255, 255, 255),
            LegendBorderColor = new CellColor(166, 166, 166),
            LegendBorderThickness = 1,
            LegendFontSize = 10.5,
            ShowXAxisMajorGridlines = true,
            XAxisMajorGridlineColor = new CellColor(217, 217, 217),
            XAxisGridlineThickness = 0.75,
            XAxisMajorTickStyle = ChartAxisTickStyle.Cross,
            XAxisMinorTickStyle = ChartAxisTickStyle.Inside,
            XAxisLineColor = new CellColor(128, 128, 128),
            XAxisLineThickness = 1,
            XAxisLabelTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1),
            XAxisLabelFontSize = 10,
            XAxisLabelAngle = -45,
            YAxisMinimum = 0,
            YAxisMaximum = 200,
            YAxisMajorUnit = 50,
            YAxisMinorUnit = 25,
            YAxisNumberFormat = ChartDataLabelNumberFormat.Currency,
            ShowYAxisMajorGridlines = true,
            YAxisMajorGridlineColor = new CellColor(217, 217, 217),
            YAxisGridlineThickness = 0.75,
            YAxisMajorTickStyle = ChartAxisTickStyle.Outside,
            YAxisMinorTickStyle = ChartAxisTickStyle.Inside,
            YAxisLineColor = new CellColor(128, 128, 128),
            YAxisLineThickness = 1,
            YAxisLabelTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Light2, 0.1),
            YAxisLabelFontSize = 12,
            YAxisLabelAngle = 90,
            Uses1904DateSystem = true,
            Language = "en-US",
            ShowDataLabelsOverMaximum = true,
            AutoTitleDeleted = true,
            ColorMapOverride = new ChartColorMapOverrideModel
            {
                UseMasterColorMapping = false,
                OverrideMappings = { ["accent1"] = "accent2" }
            },
            ExternalData = new ChartExternalDataModel
            {
                RelationshipId = "rIdExternalData1",
                RelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package",
                Target = "../externalLinks/externalLink1.xml",
                TargetMode = "External",
                AutoUpdate = true
            },
            PlotAreaLayout = new ChartManualLayoutModel
            {
                LayoutTarget = "inner",
                XMode = "factor",
                YMode = "factor",
                WidthMode = "factor",
                HeightMode = "factor",
                X = 0.1,
                Y = 0.2,
                Width = 0.8,
                Height = 0.6
            },
            LegendLayout = new ChartManualLayoutModel
            {
                LayoutTarget = "inner",
                X = 0.72,
                Y = 0.1,
                Height = 0.7
            },
            DataTable = new ChartDataTableModel
            {
                ShowHorizontalBorder = true,
                ShowVerticalBorder = true,
                ShowOutline = true,
                ShowLegendKeys = true
            },
            SeriesFormats = [new ChartSeriesFormat(0, FillColor: new CellColor(68, 114, 196), Smooth: true, InvertIfNegative: true)]
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.StackedColumn,
            DataRange = Range(sheet, "A1", "C4"),
            Title = "Stacked With Series Lines",
            ChartTitleFontSize = 14,
            LegendPosition = ChartLegendPosition.Bottom,
            XAxisMajorTickStyle = ChartAxisTickStyle.None,
            ShowYAxisMajorGridlines = true,
            YAxisGridlineThickness = 0.75,
            YAxisMajorTickStyle = ChartAxisTickStyle.None,
            ShowSeriesLines = true,
            SeriesLineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent5),
            SeriesLineThickness = 1.5,
            SeriesLineDashStyle = ChartLineDashStyle.Dot
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Radar,
            DataRange = Range(sheet, "A1", "C4"),
            Title = "Radar View",
            ShowLegend = true
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.VolumeOpenHighLowClose,
            DataRange = Range(sheet, "I1", "N4"),
            Title = "Stock View",
            ShowLegend = true,
            ShowHighLowLines = true,
            HighLowLineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4),
            HighLowLineThickness = 2,
            HighLowLineDashStyle = ChartLineDashStyle.Dash,
            ShowUpDownBars = true,
            UpDownBarGapWidth = 180,
            UpBarFillColor = new CellColor(112, 173, 71),
            UpBarBorderColor = new CellColor(84, 130, 53),
            UpBarBorderThickness = 1,
            DownBarFillColor = new CellColor(192, 0, 0),
            DownBarBorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
            DownBarBorderThickness = 2
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Surface,
            DataRange = Range(sheet, "A1", "C4"),
            Title = "Surface View",
            ShowLegend = true
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.ThreeDSurface,
            DataRange = Range(sheet, "A1", "C4"),
            Title = "3D Surface View",
            ShowLegend = true,
            ThreeDView = new Chart3DViewModel
            {
                RotationX = 20,
                HeightPercent = 80,
                RotationY = 30,
                DepthPercent = 150,
                RightAngleAxes = false,
                Perspective = 30
            },
            FloorFormat = new ChartSurfaceFormatModel
            {
                FillColor = new CellColor(217, 234, 211),
                BorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent6),
                BorderThickness = 1
            },
            SideWallFormat = new ChartSurfaceFormatModel
            {
                FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
                BorderColor = new CellColor(192, 0, 0),
                BorderThickness = 2
            },
            BackWallFormat = new ChartSurfaceFormatModel
            {
                FillColor = new CellColor(217, 225, 242),
                BorderColor = new CellColor(68, 114, 196),
                BorderThickness = 3
            }
        });
        return workbook;
    }

    private static Workbook CreateChartsClassicExtended()
    {
        var workbook = NewWorkbook("generated-charts-classic-extended-004");
        var sheet = workbook.AddSheet("Classic Extended");
        Set(sheet, "A1", new TextValue("Quarter"));
        Set(sheet, "B1", new TextValue("North"));
        Set(sheet, "C1", new TextValue("South"));
        Set(sheet, "D1", new TextValue("East"));
        Set(sheet, "A2", new TextValue("Q1"));
        Set(sheet, "A3", new TextValue("Q2"));
        Set(sheet, "A4", new TextValue("Q3"));
        Set(sheet, "A5", new TextValue("Q4"));
        Set(sheet, "B2", new NumberValue(18));
        Set(sheet, "B3", new NumberValue(24));
        Set(sheet, "B4", new NumberValue(21));
        Set(sheet, "B5", new NumberValue(30));
        Set(sheet, "C2", new NumberValue(12));
        Set(sheet, "C3", new NumberValue(17));
        Set(sheet, "C4", new NumberValue(22));
        Set(sheet, "C5", new NumberValue(25));
        Set(sheet, "D2", new NumberValue(10));
        Set(sheet, "D3", new NumberValue(14));
        Set(sheet, "D4", new NumberValue(18));
        Set(sheet, "D5", new NumberValue(20));

        var dataRange = Range(sheet, "A1", "D5");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.PercentStackedColumn,
            DataRange = dataRange,
            Title = "Percent Stacked Column",
            ShowLegend = true,
            ChartTitleFontSize = 14,
            LegendPosition = ChartLegendPosition.Bottom,
            XAxisMajorTickStyle = ChartAxisTickStyle.None,
            YAxisNumberFormat = ChartDataLabelNumberFormat.Percent,
            ShowYAxisMajorGridlines = true,
            YAxisGridlineThickness = 0.75,
            YAxisMajorTickStyle = ChartAxisTickStyle.None
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.StackedBar,
            DataRange = dataRange,
            Title = "Stacked Bar",
            ShowLegend = true,
            ShowSeriesLines = true,
            ChartTitleFontSize = 14,
            LegendPosition = ChartLegendPosition.Bottom,
            ShowXAxisMajorGridlines = true,
            XAxisGridlineThickness = 0.75,
            XAxisMajorTickStyle = ChartAxisTickStyle.None
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.PercentStackedBar,
            DataRange = dataRange,
            Title = "Percent Stacked Bar",
            ShowLegend = true,
            ShowSeriesLines = true,
            ChartTitleFontSize = 14,
            LegendPosition = ChartLegendPosition.Bottom,
            XAxisNumberFormat = ChartDataLabelNumberFormat.Percent,
            ShowXAxisMajorGridlines = true,
            XAxisGridlineThickness = 0.75,
            XAxisMajorTickStyle = ChartAxisTickStyle.None
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.StackedArea,
            DataRange = dataRange,
            Title = "Stacked Area",
            ShowLegend = true
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.PercentStackedArea,
            DataRange = dataRange,
            Title = "Percent Stacked Area",
            ShowLegend = true
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.ThreeDColumn,
            DataRange = dataRange,
            Title = "3D Column",
            ShowLegend = true,
            ThreeDView = new Chart3DViewModel { RotationX = 15, RotationY = 20, HeightPercent = 90, DepthPercent = 120, RightAngleAxes = true }
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.ThreeDBar,
            DataRange = dataRange,
            Title = "3D Bar",
            ShowLegend = true,
            ThreeDView = new Chart3DViewModel { RotationX = 10, RotationY = 25, HeightPercent = 80, DepthPercent = 150, RightAngleAxes = true }
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.ThreeDArea,
            DataRange = dataRange,
            Title = "3D Area",
            ShowLegend = true,
            ThreeDView = new Chart3DViewModel { RotationX = 20, RotationY = 30, HeightPercent = 70, DepthPercent = 130, RightAngleAxes = false, Perspective = 25 }
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.ThreeDLine,
            DataRange = dataRange,
            Title = "3D Line",
            ShowLegend = true,
            ShowDropLines = true,
            ThreeDView = new Chart3DViewModel { RotationX = 18, RotationY = 25, HeightPercent = 80, DepthPercent = 120, RightAngleAxes = true }
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.ThreeDPie,
            DataRange = Range(sheet, "A1", "B5"),
            Title = "3D Pie",
            ShowLegend = true,
            ExplodedSliceIndex = 1,
            ExplodedSliceDistance = 0.08,
            ThreeDView = new Chart3DViewModel { RotationX = 30, RotationY = 0, RightAngleAxes = false }
        });
        return workbook;
    }

    private static Workbook CreateChartsChartEx()
    {
        var workbook = NewWorkbook("generated-charts-chartex-004");
        var sheet = workbook.AddSheet("ChartEx");
        Set(sheet, "A1", new TextValue("Category"));
        Set(sheet, "B1", new TextValue("Value"));
        Set(sheet, "C1", new TextValue("Group"));
        Set(sheet, "A2", new TextValue("Opening"));
        Set(sheet, "A3", new TextValue("Sales"));
        Set(sheet, "A4", new TextValue("Returns"));
        Set(sheet, "A5", new TextValue("Costs"));
        Set(sheet, "A6", new TextValue("Closing"));
        Set(sheet, "B2", new NumberValue(120));
        Set(sheet, "B3", new NumberValue(45));
        Set(sheet, "B4", new NumberValue(-18));
        Set(sheet, "B5", new NumberValue(-32));
        Set(sheet, "B6", new NumberValue(115));
        Set(sheet, "C2", new TextValue("Base"));
        Set(sheet, "C3", new TextValue("Movement"));
        Set(sheet, "C4", new TextValue("Movement"));
        Set(sheet, "C5", new TextValue("Movement"));
        Set(sheet, "C6", new TextValue("Base"));

        Set(sheet, "E1", new TextValue("Value"));
        double[] histogramValues = [4, 7, 9, 11, 12, 16, 18, 19, 23, 27, 32, 38, 41, 47];
        for (var index = 0; index < histogramValues.Length; index++)
            Set(sheet, $"E{index + 2}", new NumberValue(histogramValues[index]));

        Set(sheet, "G1", new TextValue("Sample"));
        Set(sheet, "H1", new TextValue("North"));
        Set(sheet, "I1", new TextValue("South"));
        double[] north = [10, 13, 15, 16, 18, 21, 23, 24];
        double[] south = [8, 11, 14, 16, 19, 20, 25, 29];
        for (var index = 0; index < north.Length; index++)
        {
            var row = index + 2;
            Set(sheet, $"G{row}", new TextValue((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Set(sheet, $"H{row}", new NumberValue(north[index]));
            Set(sheet, $"I{row}", new NumberValue(south[index]));
        }

        var singleSeries = Range(sheet, "A1", "B6");
        sheet.Charts.Add(new ChartModel { Type = ChartType.Treemap, DataRange = singleSeries, Title = "Treemap", ShowLegend = false, LegendPosition = ChartLegendPosition.None });
        sheet.Charts.Add(new ChartModel { Type = ChartType.Sunburst, DataRange = Range(sheet, "A1", "C6"), Title = "Sunburst", ShowLegend = false, LegendPosition = ChartLegendPosition.None });
        sheet.Charts.Add(new ChartModel { Type = ChartType.Histogram, DataRange = Range(sheet, "E1", "E15"), Title = "Histogram", FirstColIsCategories = false, ShowLegend = false, LegendPosition = ChartLegendPosition.None });
        sheet.Charts.Add(new ChartModel { Type = ChartType.Pareto, DataRange = singleSeries, Title = "Pareto", ShowLegend = false, LegendPosition = ChartLegendPosition.None });
        sheet.Charts.Add(new ChartModel { Type = ChartType.BoxAndWhisker, DataRange = Range(sheet, "G1", "I9"), Title = "Box and Whisker", ShowLegend = true });
        sheet.Charts.Add(new ChartModel { Type = ChartType.Waterfall, DataRange = singleSeries, Title = "Waterfall", ShowLegend = false, LegendPosition = ChartLegendPosition.None, ShowSeriesLines = false, WaterfallTotalPointIndices = [0, 4] });
        sheet.Charts.Add(new ChartModel { Type = ChartType.Funnel, DataRange = singleSeries, Title = "Funnel", ShowLegend = false, LegendPosition = ChartLegendPosition.None });
        return workbook;
    }

    private static Workbook CreateStructuredTables()
    {
        var workbook = NewWorkbook("generated-structured-tables-001");
        var sheet = workbook.AddSheet("Tables");
        Set(sheet, "A1", new TextValue("Category"));
        Set(sheet, "B1", new TextValue("Amount"));
        Set(sheet, "A2", new TextValue("A"));
        Set(sheet, "B2", new NumberValue(10));
        Set(sheet, "A3", new TextValue("B"));
        Set(sheet, "B3", new NumberValue(20));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, "A1", "B3"),
            HasAutoFilter = true,
            TotalsRowShown = false,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true,
            NativeSortStateXml = """<sortState xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" ref="A2:B3"><sortCondition ref="B2:B3" descending="1" /></sortState>""",
            PackagePart = "xl/tables/table1.xml"
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Category"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(
            0,
            [],
            IncludeBlank: false,
            CustomFilters:
            [
                new StructuredTableCustomFilterModel("greaterThan", "15")
            ],
            CustomFiltersAnd: false,
            NativeCustomFiltersAttributes: null,
            NativeFilterXmls:
            [
            ]));
        sheet.FilterHiddenRows.Add(2);
        sheet.FilterHiddenRows.Add(3);
        sheet.StructuredTables.Add(table);
        return workbook;
    }

    private static Workbook CreatePivots()
    {
        var workbook = NewWorkbook("generated-pivots-001");
        var sheet = workbook.AddSheet("Pivot Data");
        Set(sheet, "A1", new TextValue("Category"));
        Set(sheet, "B1", new TextValue("Amount"));
        Set(sheet, "A2", new TextValue("A"));
        Set(sheet, "B2", new NumberValue(10));
        Set(sheet, "A3", new TextValue("B"));
        Set(sheet, "B3", new NumberValue(20));
        Set(sheet, "A5", new TextValue("Category"));
        Set(sheet, "B5", new TextValue("Sum of Amount"));
        Set(sheet, "A6", new TextValue("A"));
        Set(sheet, "B6", new NumberValue(10));
        Set(sheet, "A7", new TextValue("B"));
        Set(sheet, "B7", new NumberValue(20));
        Set(sheet, "A8", new TextValue("Grand Total"));
        Set(sheet, "B8", new NumberValue(30));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
            CreatedVersion = 8,
            MinRefreshableVersion = 4
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", 4));
        workbook.PivotCaches.Add(cache);
        var style = new PivotTableStyleModel
        {
            Name = "FreeXCorpusPivotStyle",
            AppliesToPivotTables = true,
            AppliesToTables = false
        };
        style.Elements.Add(new PivotTableStyleElementModel("wholeTable"));
        style.Elements.Add(new PivotTableStyleElementModel("firstRowStripe", Size: 1));
        workbook.PivotTableStyles.Add(style);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "A5", "B8"),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
            StyleName = "FreeXCorpusPivotStyle",
            ShowRowStripes = true,
            ShowContextualTooltips = false,
            ShowPropertiesInTooltips = false,
            ShowClassicLayout = true,
            MergeAndCenterLabels = true,
            AltTextTitle = "Corpus pivot",
            AltTextDescription = "Generated PivotTable parity fixture",
            // dataCaption is a required OOXML attribute; FreeX writes the Excel default "Values" when unset,
            // so the fixture states it explicitly to keep the save/load round-trip identity-stable.
            DataCaption = "Values"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 4));
        sheet.PivotTables.Add(pivot);
        return workbook;
    }

    private static Workbook CreateProtectionAndPageSetup()
    {
        var workbook = NewWorkbook("generated-protection-page-setup-001");
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        workbook.FullCalculationOnLoad = true;
        workbook.ForceFullCalculation = true;
        workbook.IterativeCalculation = true;
        workbook.MaxCalculationIterations = 25;
        workbook.MaxCalculationChange = 0.005;
        workbook.Theme = WorkbookTheme.Office
            .WithName("FreeX Corpus Theme")
            .WithFonts("Aptos Display", "Aptos")
            .WithEffects("FreeXEffects")
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(12, 34, 56))
            .WithColor(WorkbookThemeColorSlot.Hyperlink, new CellColor(1, 99, 193));
        var sheet = workbook.AddSheet("Print");
        Set(sheet, "A1", new TextValue("Protected print fixture"));
        Set(sheet, "A2", new NumberValue(42));
        sheet.DefaultColumnWidth = 11;
        sheet.DefaultRowHeight = 22;
        sheet.ColumnWidths[1] = 18;
        sheet.RowHeights[2] = 28;
        sheet.TabColor = new CellColor(0, 176, 80);
        sheet.CodeName = "PrintSheet";
        sheet.FullCalculationOnLoad = true;
        sheet.PhoneticProperties = new WorksheetPhoneticProperties("1", "fullwidthKatakana", "center");
        sheet.CustomProperties.Add(new WorksheetCustomProperty("FreeXCorpusSheet", 7));
        sheet.IsProtected = true;
        sheet.ProtectionPassword = "fixture";
        sheet.AllowEditRanges.Add(Range(sheet, "A2", "B5"));
        workbook.IsStructureProtected = true;
        workbook.StructureProtectionPassword = "structure";
        sheet.PrintArea = Range(sheet, "A1", "C20");
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, 1);
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        sheet.PageHeader = new WorksheetHeaderFooter("FreeX &[Picture]", "Corpus", "2026");
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(
            new WorksheetHeaderFooterPicture(MinimalPngBytes(), "image/png", "header-logo.png", 96, 32),
            null,
            null);
        sheet.PageFooter = new WorksheetHeaderFooter("", "Page &P", "");
        sheet.ViewMode = WorksheetViewMode.PageBreakPreview;
        sheet.ViewTopRow = 4;
        sheet.ViewLeftCol = 2;
        sheet.ActiveRow = 6;
        sheet.ActiveCol = 3;
        workbook.WatchedCells.Add(Addr(sheet, "A2"));
        workbook.Scenarios.Add(new WorkbookScenario(
            "Print Forecast",
            [
                new ScenarioCellValue(Addr(sheet, "A2"), new NumberValue(84)),
                new ScenarioCellValue(Addr(sheet, "B2"), new TextValue("Scenario"))
            ]));
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Print Review",
            [
                new WorksheetCustomViewState(
                    sheet.Name,
                    WorksheetViewMode.PageLayout,
                    sheet.FrozenRows,
                    sheet.FrozenCols,
                    sheet.SplitRow,
                    sheet.SplitColumn,
                    sheet.ShowGridlines,
                    sheet.ShowHeadings,
                    sheet.ShowRulers,
                    125,
                    sheet.ShowFormulas)
            ],
            IncludePrintSettings: true,
            IncludeHiddenRowsColumnsAndFilterSettings: true));
        var hidden = workbook.AddSheet("Hidden Meta");
        Set(hidden, "A1", new TextValue("Very hidden metadata fixture"));
        hidden.IsHidden = true;
        hidden.IsVeryHidden = true;
        hidden.CodeName = "HiddenMeta";
        hidden.TabColor = new CellColor(255, 192, 0);
        return workbook;
    }

    private static Workbook CreateNamedRangesDeep()
    {
        var workbook = NewWorkbook("generated-named-ranges-deep-003");
        var data = workbook.AddSheet("Data");
        var calc = workbook.AddSheet("Calc");
        Set(data, "A1", new TextValue("North"));
        Set(data, "B1", new NumberValue(100));
        Set(data, "A2", new TextValue("South"));
        Set(data, "B2", new NumberValue(125));
        Set(data, "A3", new TextValue("West"));
        Set(data, "B3", new NumberValue(90));
        Set(data, "A4", new TextValue("East"));
        Set(data, "B4", new NumberValue(110));
        workbook.DefineNamedRange("AllRegions", Range(data, "A1", "A4"));
        workbook.DefineNamedRange("AllSales", Range(data, "B1", "B4"));
        workbook.DefineNamedRange("NorthSales", Range(data, "B1", "B1"));
        workbook.DefineNamedRange("SouthSales", Range(data, "B2", "B2"));
        workbook.DefineNamedRange("WestSales", Range(data, "B3", "B3"));
        workbook.DefineNamedRange("EastSales", Range(data, "B4", "B4"));
        Formula(calc, "A1", "SUM(AllSales)");
        Formula(calc, "A2", "AVERAGE(AllSales)");
        Formula(calc, "A3", "MAX(AllSales)");
        Formula(calc, "A4", "MIN(AllSales)");
        Formula(calc, "B1", "NorthSales+SouthSales");
        Formula(calc, "B2", "WestSales+EastSales");
        Formula(calc, "B3", "SUM(Data!B1:B4)");
        Formula(calc, "B4", "INDEX(AllRegions,2)");
        return workbook;
    }

    private static Workbook CreateCfMultiRules()
    {
        var workbook = NewWorkbook("generated-cf-multi-rules-003");
        var sheet = workbook.AddSheet("CF Rules");
        for (uint row = 1; row <= 10; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 10));

        sheet.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = Range(sheet, "A1", "A10"), Priority = 1, RuleType = CfRuleType.CellValue, Operator = CfOperator.GreaterThan, Value1 = "80", FormatIfTrue = new CellStyle { FillColor = new CellColor(198, 239, 206) } });
        sheet.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = Range(sheet, "A1", "A10"), Priority = 2, RuleType = CfRuleType.CellValue, Operator = CfOperator.LessThan, Value1 = "30", FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 199, 206) } });
        sheet.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = Range(sheet, "A1", "A10"), Priority = 3, RuleType = CfRuleType.Formula, FormulaText = "MOD(ROW(A1),2)=0", FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 242, 204) } });
        sheet.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = Range(sheet, "A1", "A10"), Priority = 4, RuleType = CfRuleType.Top10, TopBottomRank = 3, AboveAverage = true });
        sheet.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = Range(sheet, "A1", "A10"), Priority = 5, RuleType = CfRuleType.DuplicateValues });
        sheet.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = Range(sheet, "A1", "A10"), Priority = 6, RuleType = CfRuleType.ColorScale, UseThreeColorScale = false, MinThresholdType = CfThresholdType.Min, MaxThresholdType = CfThresholdType.Max });
        sheet.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = Range(sheet, "A1", "A10"), Priority = 7, RuleType = CfRuleType.DataBar, DataBarMinThresholdType = CfThresholdType.AutoMin, DataBarMaxThresholdType = CfThresholdType.AutoMax });
        var iconRule = new ConditionalFormat { AppliesTo = Range(sheet, "A1", "A10"), Priority = 8, RuleType = CfRuleType.IconSet, IconSetStyle = "3TrafficLights1" };
        iconRule.IconSetThresholds.AddRange([new CfThresholdModel(CfThresholdType.Number, "0"), new CfThresholdModel(CfThresholdType.Percent, "33"), new CfThresholdModel(CfThresholdType.Percent, "67")]);
        sheet.ConditionalFormats.Add(iconRule);
        return workbook;
    }

    private static Workbook CreateChartsMultiSeries()
    {
        var workbook = NewWorkbook("generated-charts-multi-series-003");
        var sheet = workbook.AddSheet("Multi Series");
        Set(sheet, "A1", new TextValue("Month")); Set(sheet, "B1", new TextValue("S1")); Set(sheet, "C1", new TextValue("S2")); Set(sheet, "D1", new TextValue("S3")); Set(sheet, "E1", new TextValue("S4"));
        for (int r = 2; r <= 5; r++)
        {
            Set(sheet, $"A{r}", new TextValue($"Q{r - 1}"));
            Set(sheet, $"B{r}", new NumberValue(100 + r * 10));
            Set(sheet, $"C{r}", new NumberValue(80 + r * 8));
            Set(sheet, $"D{r}", new NumberValue(60 + r * 6));
            Set(sheet, $"E{r}", new NumberValue(40 + r * 4));
        }
        sheet.Charts.Add(new ChartModel { Type = ChartType.Column, DataRange = Range(sheet, "A1", "E5"), Title = "4-Series Column", ShowLegend = true });
        sheet.Charts.Add(new ChartModel { Type = ChartType.Line, DataRange = Range(sheet, "A1", "E5"), Title = "4-Series Line", ShowLegend = true });
        return workbook;
    }

    private static Workbook CreateDvEdgeCases()
    {
        var workbook = NewWorkbook("generated-dv-edge-cases-003");
        var sheet = workbook.AddSheet("DV Edge");
        Set(sheet, "A1", new TextValue("Date")); Set(sheet, "B1", new TextValue("Time")); Set(sheet, "C1", new TextValue("TextLen")); Set(sheet, "D1", new TextValue("Dec")); Set(sheet, "E1", new TextValue("List"));
        workbook.DefineNamedRange("DvChoices", Range(sheet, "G1", "G3"));
        Set(sheet, "G1", new TextValue("Alpha")); Set(sheet, "G2", new TextValue("Beta")); Set(sheet, "G3", new TextValue("Gamma"));
        sheet.DataValidations.Add(new DataValidation { AppliesTo = Range(sheet, "A2", "A20"), Type = DvType.Date, Operator = DvOperator.GreaterThanOrEqual, Formula1 = "DATE(2026,1,1)", ErrorTitle = "Invalid date", ErrorMessage = "Date must be on or after 2026-01-01." });
        sheet.DataValidations.Add(new DataValidation { AppliesTo = Range(sheet, "B2", "B20"), Type = DvType.Time, Operator = DvOperator.Between, Formula1 = "TIME(8,0,0)", Formula2 = "TIME(18,0,0)" });
        sheet.DataValidations.Add(new DataValidation { AppliesTo = Range(sheet, "C2", "C20"), Type = DvType.TextLength, Operator = DvOperator.LessThanOrEqual, Formula1 = "50" });
        var listDv = new DataValidation { AppliesTo = Range(sheet, "D2", "D20"), Type = DvType.Decimal, Operator = DvOperator.Between, Formula1 = "0", Formula2 = "1" };
        listDv.AdditionalRanges.Add(Range(sheet, "F2", "F20"));
        sheet.DataValidations.Add(listDv);
        // Named-range List source: model convention is a leading '=' (see R27_DataValidationListSourceTests
        // and the R36 IO-mapper fix, which re-adds it on load to match real Excel's unprefixed on-disk form).
        sheet.DataValidations.Add(new DataValidation { AppliesTo = Range(sheet, "E2", "E20"), Type = DvType.List, Formula1 = "=DvChoices", PromptTitle = "Pick item", PromptMessage = "Choose Alpha, Beta or Gamma." });
        return workbook;
    }

    private static Workbook CreateMergedCellsFormulas()
    {
        // This fixture exercises merged regions alongside formulas that reference
        // data on other sheets. Merged regions in XLSX only retain the top-left cell
        // value after a round-trip, so the fixture only sets values in the top-left
        // cell of each merge region.
        var workbook = NewWorkbook("generated-merged-cells-formulas-003");
        var sales = workbook.AddSheet("Sales");
        var summary = workbook.AddSheet("Summary");
        var budget = workbook.AddSheet("Budget");
        // Sales sheet: A1 header row, data in rows 2-5.
        // Regions A2:A3 and A4:A5 are merged; only A2/A4 hold the region label.
        Set(sales, "A1", new TextValue("Region")); Set(sales, "B1", new TextValue("Product")); Set(sales, "C1", new TextValue("Amount"));
        Set(sales, "A2", new TextValue("North")); Set(sales, "B2", new TextValue("A")); Set(sales, "C2", new NumberValue(100));
        /* A3 is the covered cell of A2:A3 merge – leave empty */  Set(sales, "B3", new TextValue("B")); Set(sales, "C3", new NumberValue(120));
        Set(sales, "A4", new TextValue("South")); Set(sales, "B4", new TextValue("A")); Set(sales, "C4", new NumberValue(90));
        /* A5 is the covered cell of A4:A5 merge – leave empty */  Set(sales, "B5", new TextValue("B")); Set(sales, "C5", new NumberValue(85));
        sales.AddMergedRegion(Range(sales, "A2", "A3"));
        sales.AddMergedRegion(Range(sales, "A4", "A5"));
        // Budget sheet: A1 is the top-left of A1:C1 merge – leave B1/C1 empty.
        Set(budget, "A1", new TextValue("Budget Q1-Q2 Summary"));
        Set(budget, "A2", new NumberValue(400)); Set(budget, "A3", new NumberValue(450));
        budget.AddMergedRegion(Range(budget, "A1", "C1"));
        // Summary sheet: formulas that reference Sales and Budget.
        // C1:D2 is merged; only C1 is set (top-left of the region).
        Formula(summary, "A1", "SUMIFS(Sales!C2:C5,Sales!A2:A5,\"North\")");
        Formula(summary, "A2", "SUMIFS(Sales!C2:C5,Sales!A2:A5,\"South\")");
        Formula(summary, "A3", "COUNTIFS(Sales!A2:A5,\"North\",Sales!B2:B5,\"A\")");
        Formula(summary, "A4", "SUM(Sales!C2:C5)");
        Formula(summary, "B1", "Budget!A2");
        Formula(summary, "B2", "Budget!A3");
        summary.AddMergedRegion(Range(summary, "C1", "D2"));
        // Add a frozen header row on Sales and a hidden row on Budget to satisfy the structure tag assertions.
        sales.FrozenRows = 1;
        budget.HiddenRows.Add(3);
        return workbook;
    }

    private static Workbook CreateCrossSheetRefsAdvanced()
    {
        var workbook = NewWorkbook("generated-cross-sheet-refs-advanced-003");
        var lookup = workbook.AddSheet("Lookup");
        var data = workbook.AddSheet("Data");
        var report = workbook.AddSheet("Report");
        Set(lookup, "A1", new TextValue("Code")); Set(lookup, "B1", new TextValue("Name")); Set(lookup, "C1", new TextValue("Rate"));
        Set(lookup, "A2", new TextValue("X")); Set(lookup, "B2", new TextValue("Xray")); Set(lookup, "C2", new NumberValue(0.1));
        Set(lookup, "A3", new TextValue("Y")); Set(lookup, "B3", new TextValue("Yankee")); Set(lookup, "C3", new NumberValue(0.15));
        Set(lookup, "A4", new TextValue("Z")); Set(lookup, "B4", new TextValue("Zulu")); Set(lookup, "C4", new NumberValue(0.2));
        workbook.DefineNamedRange("CodeTable", Range(lookup, "A1", "C4"));
        Set(data, "A1", new TextValue("X")); Set(data, "B1", new NumberValue(500));
        Set(data, "A2", new TextValue("Y")); Set(data, "B2", new NumberValue(300));
        Set(data, "A3", new TextValue("Z")); Set(data, "B3", new NumberValue(200));
        Formula(report, "A1", "VLOOKUP(Data!A1,CodeTable,2,FALSE)");
        Formula(report, "A2", "VLOOKUP(Data!A2,CodeTable,2,FALSE)");
        Formula(report, "B1", "INDEX(Lookup!C2:C4,MATCH(Data!A1,Lookup!A2:A4,0))");
        Formula(report, "B2", "INDIRECT(\"Data!B\"&ROW())");
        Formula(report, "C1", "SUM(Data!B1:B3)");
        Formula(report, "C2", "SUMIF(Data!A1:A3,\"X\",Data!B1:B3)");
        return workbook;
    }

    private static Workbook CreateArrayFormulas()
    {
        var workbook = NewWorkbook("generated-array-formulas-003");
        var sheet = workbook.AddSheet("Arrays");
        for (uint r = 1; r <= 5; r++) { Set(sheet, $"A{r}", new NumberValue(r * 10)); Set(sheet, $"B{r}", new NumberValue(r * 5)); }
        Formula(sheet, "D1", "SUM(A1:A5*B1:B5)");
        Formula(sheet, "D2", "SUM(IF(A1:A5>20,A1:A5,0))");
        Formula(sheet, "D3", "SUM((A1:A5>20)*A1:A5)");
        Formula(sheet, "D4", "SUM(A1:A5)/COUNT(A1:A5)");
        Formula(sheet, "E1", "SUM(A1:A5*B1:B5)/SUM(A1:A5)");
        return workbook;
    }

    private static Workbook CreateChartScatterBubble()
    {
        var workbook = NewWorkbook("generated-chart-scatter-bubble-003");
        var sheet = workbook.AddSheet("Scatter Bubble");
        Set(sheet, "A1", new TextValue("X")); Set(sheet, "B1", new TextValue("Y")); Set(sheet, "C1", new TextValue("Size"));
        Set(sheet, "A2", new NumberValue(1)); Set(sheet, "B2", new NumberValue(3)); Set(sheet, "C2", new NumberValue(10));
        Set(sheet, "A3", new NumberValue(2)); Set(sheet, "B3", new NumberValue(5)); Set(sheet, "C3", new NumberValue(20));
        Set(sheet, "A4", new NumberValue(4)); Set(sheet, "B4", new NumberValue(2)); Set(sheet, "C4", new NumberValue(15));
        Set(sheet, "A5", new NumberValue(3)); Set(sheet, "B5", new NumberValue(7)); Set(sheet, "C5", new NumberValue(25));
        sheet.Charts.Add(new ChartModel { Type = ChartType.Scatter, DataRange = Range(sheet, "A1", "B5"), Title = "Scatter XY", ShowLegend = true });
        sheet.Charts.Add(new ChartModel { Type = ChartType.Bubble, DataRange = Range(sheet, "A1", "C5"), Title = "Bubble Chart", ShowLegend = true, BubbleScale = 80, ShowNegativeBubbles = false, BubbleSizeRepresents = ChartBubbleSizeRepresents.Area });
        return workbook;
    }

    private static Workbook CreatePivotCalculatedFields()
    {
        var workbook = NewWorkbook("generated-pivot-calculated-fields-003");
        var sheet = workbook.AddSheet("Pivot Calc");
        Set(sheet, "A1", new TextValue("Region")); Set(sheet, "B1", new TextValue("Sales")); Set(sheet, "C1", new TextValue("Cost"));
        Set(sheet, "A2", new TextValue("North")); Set(sheet, "B2", new NumberValue(200)); Set(sheet, "C2", new NumberValue(150));
        Set(sheet, "A3", new TextValue("South")); Set(sheet, "B3", new NumberValue(180)); Set(sheet, "C3", new NumberValue(130));
        Set(sheet, "A4", new TextValue("West")); Set(sheet, "B4", new NumberValue(160)); Set(sheet, "C4", new NumberValue(110));
        Set(sheet, "A6", new TextValue("Region")); Set(sheet, "B6", new TextValue("Sum of Sales")); Set(sheet, "C6", new TextValue("Sum of Margin"));
        Set(sheet, "A7", new TextValue("North")); Set(sheet, "B7", new NumberValue(200)); Set(sheet, "C7", new NumberValue(50));
        Set(sheet, "A8", new TextValue("South")); Set(sheet, "B8", new NumberValue(180)); Set(sheet, "C8", new NumberValue(50));
        Set(sheet, "A9", new TextValue("West")); Set(sheet, "B9", new NumberValue(160)); Set(sheet, "C9", new NumberValue(50));
        Set(sheet, "A10", new TextValue("Grand Total")); Set(sheet, "B10", new NumberValue(540)); Set(sheet, "C10", new NumberValue(150));
        var cache = new PivotCacheModel
        {
            CacheId = 3, SourceType = PivotCacheSourceType.WorksheetRange, SourceSheetName = sheet.Name,
            SourceReference = "A1:C4", PackagePart = "xl/pivotCache/pivotCacheDefinition3.xml",
            RecordCount = 3, CreatedVersion = 8, MinRefreshableVersion = 4
        };
        cache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["North", "South", "West"]));
        cache.Fields.Add(new PivotCacheFieldModel("Sales", ContainsNumber: true, MinValue: 160, MaxValue: 200));
        cache.Fields.Add(new PivotCacheFieldModel("Cost", ContainsNumber: true, MinValue: 110, MaxValue: 150));
        cache.Fields.Add(new PivotCacheFieldModel("Margin", Formula: "Sales-Cost", IsDatabaseField: false));
        workbook.PivotCaches.Add(cache);
        var pivot = new PivotTableModel
        {
            Name = "PivotCalcField", CacheId = 3, SourceRange = Range(sheet, "A1", "C4"),
            TargetRange = Range(sheet, "A6", "C10"), PackagePart = "xl/pivotTables/pivotTable3.xml",
            // dataCaption is a required OOXML attribute; FreeX writes the Excel default "Values" when unset.
            DataCaption = "Values"
        };
        pivot.CalculatedFields.Add(new PivotCalculatedFieldModel("Margin", "Sales-Cost"));
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Sales", "sum", 4));
        pivot.DataFields.Add(new PivotDataFieldModel(-1, "Sum of Margin", "sum", 4, "Margin"));
        sheet.PivotTables.Add(pivot);
        return workbook;
    }

    private static Workbook CreateTableStructuredRefs()
    {
        var workbook = NewWorkbook("generated-table-structured-refs-003");
        var sheet = workbook.AddSheet("Table Refs");
        Set(sheet, "A1", new TextValue("Product")); Set(sheet, "B1", new TextValue("Price")); Set(sheet, "C1", new TextValue("Qty")); Set(sheet, "D1", new TextValue("Revenue"));
        Set(sheet, "A2", new TextValue("Alpha")); Set(sheet, "B2", new NumberValue(10)); Set(sheet, "C2", new NumberValue(50)); Set(sheet, "D2", new NumberValue(500));
        Set(sheet, "A3", new TextValue("Beta")); Set(sheet, "B3", new NumberValue(15)); Set(sheet, "C3", new NumberValue(30)); Set(sheet, "D3", new NumberValue(450));
        Set(sheet, "A4", new TextValue("Gamma")); Set(sheet, "B4", new NumberValue(20)); Set(sheet, "C4", new NumberValue(20)); Set(sheet, "D4", new NumberValue(400));
        Set(sheet, "A5", new TextValue("Total")); Set(sheet, "B5", new NumberValue(45)); Set(sheet, "C5", new NumberValue(100)); Set(sheet, "D5", new NumberValue(1350));
        var table = new StructuredTableModel { Id = 3, Name = "SalesRef", DisplayName = "SalesRef", Range = Range(sheet, "A1", "D5"), HasAutoFilter = true, TotalsRowShown = true, StyleName = "TableStyleMedium6", PackagePart = "xl/tables/table3.xml" };
        table.Columns.Add(new StructuredTableColumnModel(1, "Product", TotalsRowLabel: "Total"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Price", TotalsRowFunction: "sum"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Qty", TotalsRowFunction: "sum"));
        table.Columns.Add(new StructuredTableColumnModel(4, "Revenue", TotalsRowFunction: "sum"));
        sheet.StructuredTables.Add(table);
        Formula(sheet, "F1", "SUM(SalesRef[Revenue])");
        Formula(sheet, "F2", "AVERAGE(SalesRef[Price])");
        Formula(sheet, "F3", "MAX(SalesRef[Qty])");
        return workbook;
    }

    private static Workbook CreateFormattingAdvanced()
    {
        var workbook = NewWorkbook("generated-formatting-advanced-003");
        var sheet = workbook.AddSheet("Adv Format");
        var accountStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)", HorizontalAlignment = HorizontalAlignment.Right });
        var pctStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00%", HorizontalAlignment = HorizontalAlignment.Right });
        var sciStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00E+00" });
        var fracStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "# ??/??" });
        var dateStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "[$-0409]mmm d yyyy" });
        var rotStyle = workbook.RegisterStyle(new CellStyle { TextRotation = 45, Bold = true, FontColor = new CellColor(0, 70, 127) });
        var indStyle = workbook.RegisterStyle(new CellStyle { IndentLevel = 3, Italic = true });
        var diagStyle = workbook.RegisterStyle(new CellStyle { BorderLeft = new CellBorder(BorderStyle.Double, new CellColor(255, 0, 0)), BorderRight = new CellBorder(BorderStyle.Double, new CellColor(0, 0, 255)) });
        Set(sheet, "A1", new NumberValue(1234.5), accountStyle);
        Set(sheet, "A2", new NumberValue(0.123), pctStyle);
        Set(sheet, "A3", new NumberValue(1234567), sciStyle);
        Set(sheet, "A4", new NumberValue(1.5), fracStyle);
        Set(sheet, "A5", DateTimeValue.FromDateTime(new DateTime(2026, 5, 28)), dateStyle);
        Set(sheet, "B1", new TextValue("Rotated"), rotStyle);
        Set(sheet, "B2", new TextValue("Indented"), indStyle);
        Set(sheet, "B3", new TextValue("Diagonal"), diagStyle);
        var themeAccent = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(180, 198, 231), FontColor = new CellColor(0, 0, 0) });
        Set(sheet, "C1", new TextValue("Theme fill"), themeAccent);
        return workbook;
    }

    private static Workbook CreateSparklinesAdvanced()
    {
        var workbook = NewWorkbook("generated-sparklines-advanced-003");
        var sheet = workbook.AddSheet("Sparklines");
        var data = new[] { new[] { 5, -2, 8, 1, 6 }, new[] { 3, 7, -1, 4, 9 }, new[] { 1, -3, 2, -1, 4 } };
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 5; c++)
                sheet.SetCell(new CellAddress(sheet.Id, (uint)(r + 1), (uint)(c + 1)), new NumberValue(data[r][c]));
        sheet.Sparklines.Add(new SparklineModel { DataRange = Range(sheet, "A1", "E1"), Location = Addr(sheet, "F1"), Kind = SparklineKind.Line });
        sheet.Sparklines.Add(new SparklineModel { DataRange = Range(sheet, "A2", "E2"), Location = Addr(sheet, "F2"), Kind = SparklineKind.Column });
        sheet.Sparklines.Add(new SparklineModel { DataRange = Range(sheet, "A3", "E3"), Location = Addr(sheet, "F3"), Kind = SparklineKind.WinLoss });
        return workbook;
    }

    private static Workbook CreateProtectionAdvanced()
    {
        var workbook = NewWorkbook("generated-protection-advanced-003");
        var sheet = workbook.AddSheet("Protected");
        Set(sheet, "A1", new TextValue("Protected area"));
        Set(sheet, "A2", new NumberValue(100));
        Set(sheet, "B2", new NumberValue(200));
        var lockedStyle = workbook.RegisterStyle(new CellStyle { Locked = true, FillColor = new CellColor(220, 230, 241) });
        var unlockedStyle = workbook.RegisterStyle(new CellStyle { Locked = false, FillColor = new CellColor(235, 241, 222) });
        sheet.GetCell(Addr(sheet, "A1"))!.StyleId = lockedStyle;
        Set(sheet, "C2", new TextValue("Editable"), unlockedStyle);
        Set(sheet, "C3", new TextValue("Also editable"), unlockedStyle);
        sheet.IsProtected = true;
        sheet.ProtectionPassword = "corpus123";
        sheet.AllowEditRanges.Add(Range(sheet, "C2", "D10"));
        sheet.AllowEditRanges.Add(Range(sheet, "E2", "F10"));
        workbook.IsStructureProtected = true;
        workbook.StructureProtectionPassword = "structure123";
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        Set(hidden, "A1", new TextValue("Hidden sheet content"));
        return workbook;
    }

    private static Workbook CreateCfWithDxfStyles()
    {
        var workbook = NewWorkbook("generated-cf-with-dxf-styles-003");
        var sheet = workbook.AddSheet("CF Styles");
        for (uint r = 1; r <= 5; r++) Set(sheet, $"A{r}", new NumberValue(r * 20));
        sheet.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = Range(sheet, "A1", "A5"), Priority = 1, RuleType = CfRuleType.CellValue, Operator = CfOperator.GreaterThan, Value1 = "60", FormatIfTrue = new CellStyle { Bold = true, Italic = false, FontColor = new CellColor(0, 97, 0), FillColor = new CellColor(198, 239, 206), BorderBottom = new CellBorder(BorderStyle.Medium, new CellColor(0, 97, 0)) } });
        sheet.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = Range(sheet, "A1", "A5"), Priority = 2, RuleType = CfRuleType.CellValue, Operator = CfOperator.LessThan, Value1 = "40", FormatIfTrue = new CellStyle { Bold = false, Italic = true, FontColor = new CellColor(156, 0, 6), FillColor = new CellColor(255, 199, 206), BorderTop = new CellBorder(BorderStyle.Thin, new CellColor(156, 0, 6)), BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(156, 0, 6)) } });
        sheet.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = Range(sheet, "A1", "A5"), Priority = 3, RuleType = CfRuleType.Formula, FormulaText = "A1=60", FormatIfTrue = new CellStyle { Bold = true, Underline = true, FillColor = new CellColor(255, 235, 156) } });
        return workbook;
    }

    private static Workbook CreateFormulasCached()
    {
        var workbook = NewWorkbook("generated-formulas-cached-003");
        var sheet = workbook.AddSheet("Cached");
        Set(sheet, "A1", new TextValue("Item")); Set(sheet, "B1", new NumberValue(10)); Set(sheet, "C1", new TextValue("Alpha"));
        Set(sheet, "A2", new TextValue("Item")); Set(sheet, "B2", new NumberValue(20)); Set(sheet, "C2", new TextValue("Beta"));
        Set(sheet, "A3", new TextValue("Other")); Set(sheet, "B3", new NumberValue(30)); Set(sheet, "C3", new TextValue("Alpha"));
        Set(sheet, "A4", new TextValue("Item")); Set(sheet, "B4", new NumberValue(15)); Set(sheet, "C4", new TextValue("Gamma"));
        Set(sheet, "A5", new TextValue("Other")); Set(sheet, "B5", new NumberValue(25)); Set(sheet, "C5", new TextValue("Beta"));
        var lookupTable = workbook.AddSheet("LookupTable");
        Set(lookupTable, "A1", new NumberValue(10)); Set(lookupTable, "B1", new TextValue("Ten"));
        Set(lookupTable, "A2", new NumberValue(15)); Set(lookupTable, "B2", new TextValue("Fifteen"));
        Set(lookupTable, "A3", new NumberValue(20)); Set(lookupTable, "B3", new TextValue("Twenty"));
        Set(lookupTable, "A4", new NumberValue(25)); Set(lookupTable, "B4", new TextValue("TwentyFive"));
        Set(lookupTable, "A5", new NumberValue(30)); Set(lookupTable, "B5", new TextValue("Thirty"));
        Formula(sheet, "E1", "SUMIFS(B1:B5,A1:A5,\"Item\",C1:C5,\"Alpha\")");
        Formula(sheet, "E2", "SUMIFS(B1:B5,A1:A5,\"Item\")");
        Formula(sheet, "E3", "COUNTIFS(A1:A5,\"Item\",C1:C5,\"Alpha\")");
        Formula(sheet, "E4", "VLOOKUP(20,LookupTable!A1:B5,2,FALSE)");
        Formula(sheet, "E5", "VLOOKUP(18,LookupTable!A1:B5,2,TRUE)");
        return workbook;
    }

    private static Workbook CreateChartPieDoughnut()
    {
        var workbook = NewWorkbook("generated-chart-pie-doughnut-003");
        var sheet = workbook.AddSheet("Pie Charts");
        Set(sheet, "A1", new TextValue("Category")); Set(sheet, "B1", new TextValue("Value"));
        Set(sheet, "A2", new TextValue("Alpha")); Set(sheet, "B2", new NumberValue(30));
        Set(sheet, "A3", new TextValue("Beta")); Set(sheet, "B3", new NumberValue(45));
        Set(sheet, "A4", new TextValue("Gamma")); Set(sheet, "B4", new NumberValue(25));
        Set(sheet, "A5", new TextValue("Delta")); Set(sheet, "B5", new NumberValue(20));
        sheet.Charts.Add(new ChartModel { Type = ChartType.Pie, DataRange = Range(sheet, "A1", "B5"), Title = "Pie Chart", ShowLegend = true, ShowDataLabels = true, DataLabelPosition = ChartDataLabelPosition.OutsideEnd });
        sheet.Charts.Add(new ChartModel { Type = ChartType.Doughnut, DataRange = Range(sheet, "A1", "B5"), Title = "Doughnut Chart", ShowLegend = true });
        return workbook;
    }

    private static Workbook CreateHyperlinksAdvanced()
    {
        var workbook = NewWorkbook("generated-hyperlinks-advanced-003");
        var sheet1 = workbook.AddSheet("Links1");
        var sheet2 = workbook.AddSheet("Links2");
        var hStyle = RegisterHyperlinkStyle(workbook);
        Set(sheet1, "A1", new TextValue("Web link"), hStyle);
        sheet1.Hyperlinks[Addr(sheet1, "A1")] = "https://example.com/corpus";
        sheet1.HyperlinkMetadata[Addr(sheet1, "A1")] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage, "Open example.com corpus page");
        Set(sheet1, "A2", new TextValue("Email link"), hStyle);
        sheet1.Hyperlinks[Addr(sheet1, "A2")] = "mailto:test@example.com";
        sheet1.HyperlinkMetadata[Addr(sheet1, "A2")] = new HyperlinkMetadata(HyperlinkTargetKind.EmailAddress, "Send test email");
        Set(sheet1, "A3", new TextValue("Sheet link"), hStyle);
        sheet1.Hyperlinks[Addr(sheet1, "A3")] = "Links2!A1";
        sheet1.HyperlinkMetadata[Addr(sheet1, "A3")] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument, "Jump to Links2", "Links2!A1");
        Set(sheet1, "A4", new TextValue("Self link"), hStyle);
        sheet1.Hyperlinks[Addr(sheet1, "A4")] = "Links1!A1";
        sheet1.HyperlinkMetadata[Addr(sheet1, "A4")] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument, "Jump to top", "Links1!A1");
        Set(sheet2, "A1", new TextValue("Back link"), hStyle);
        sheet2.Hyperlinks[Addr(sheet2, "A1")] = "Links1!A1";
        sheet2.HyperlinkMetadata[Addr(sheet2, "A1")] = new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument, "Return to Links1", "Links1!A1");
        Set(sheet2, "A2", new TextValue("Another web"), hStyle);
        sheet2.Hyperlinks[Addr(sheet2, "A2")] = "https://openxmlformats.org/";
        sheet2.HyperlinkMetadata[Addr(sheet2, "A2")] = new HyperlinkMetadata(HyperlinkTargetKind.ExistingFileOrWebPage, "Open OOXML spec");
        return workbook;
    }

    private static Workbook CreateCommentsAdvanced()
    {
        var workbook = NewWorkbook("generated-comments-advanced-003");
        var sheet1 = workbook.AddSheet("Notes1");
        var sheet2 = workbook.AddSheet("Notes2");
        Set(sheet1, "A1", new TextValue("Comment target 1"));
        Set(sheet1, "B1", new TextValue("Comment target 2"));
        Set(sheet1, "C1", new TextValue("Comment target 3"));
        sheet1.Comments[Addr(sheet1, "A1")] = "First note on sheet one.";
        sheet1.Comments[Addr(sheet1, "B1")] = "Second note: check formula.";
        sheet1.Comments[Addr(sheet1, "C1")] = "Third note: review later.";
        Set(sheet2, "A1", new TextValue("Sheet two item"));
        Set(sheet2, "B2", new TextValue("Another item"));
        sheet2.Comments[Addr(sheet2, "A1")] = "Cross-sheet note one.";
        sheet2.Comments[Addr(sheet2, "B2")] = "Cross-sheet note two.";
        return workbook;
    }

    private static Workbook CreateTableAutoFilter()
    {
        var workbook = NewWorkbook("generated-table-autofilter-003");
        var sheet = workbook.AddSheet("AutoFilter");
        Set(sheet, "A1", new TextValue("Region")); Set(sheet, "B1", new TextValue("Category")); Set(sheet, "C1", new TextValue("Amount"));
        Set(sheet, "A2", new TextValue("North")); Set(sheet, "B2", new TextValue("Food")); Set(sheet, "C2", new NumberValue(100));
        Set(sheet, "A3", new TextValue("South")); Set(sheet, "B3", new TextValue("Tech")); Set(sheet, "C3", new NumberValue(200));
        Set(sheet, "A4", new TextValue("North")); Set(sheet, "B4", new TextValue("Tech")); Set(sheet, "C4", new NumberValue(150));
        Set(sheet, "A5", new TextValue("West")); Set(sheet, "B5", new TextValue("Food")); Set(sheet, "C5", new NumberValue(120));
        var table = new StructuredTableModel { Id = 4, Name = "FilterTable", DisplayName = "FilterTable", Range = Range(sheet, "A1", "C5"), HasAutoFilter = true, TotalsRowShown = false, StyleName = "TableStyleLight9", PackagePart = "xl/tables/table4.xml" };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Category"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Amount"));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["North", "South"]));
        sheet.FilterHiddenRows.Add(4);
        sheet.FilterHiddenRows.Add(5);
        sheet.StructuredTables.Add(table);
        return workbook;
    }

    private static Workbook CreateMultipleSheets()
    {
        var workbook = NewWorkbook("generated-multiple-sheets-003");
        var s1 = workbook.AddSheet("Sheet1");
        var s2 = workbook.AddSheet("Sheet2");
        var s3 = workbook.AddSheet("Summary");
        var s4 = workbook.AddSheet("Hidden");
        var s5 = workbook.AddSheet("Archive");
        Set(s1, "A1", new TextValue("Alpha")); Set(s1, "B1", new NumberValue(10)); Set(s1, "B2", new NumberValue(20));
        s1.TabColor = new CellColor(0, 112, 192); s1.FrozenRows = 1;
        Set(s2, "A1", new TextValue("Beta")); Set(s2, "B1", new NumberValue(30)); Set(s2, "B2", new NumberValue(40));
        s2.TabColor = new CellColor(255, 192, 0); s2.FrozenCols = 1;
        Formula(s3, "A1", "SUM(Sheet1!B1:B2)"); Formula(s3, "A2", "SUM(Sheet2!B1:B2)"); Formula(s3, "A3", "Sheet1!B1+Sheet2!B1");
        s3.TabColor = new CellColor(0, 176, 80);
        Set(s4, "A1", new TextValue("Hidden content")); s4.IsHidden = true;
        Set(s5, "A1", new TextValue("Archive")); Set(s5, "A2", new NumberValue(99));
        s5.TabColor = new CellColor(128, 128, 128);
        return workbook;
    }

    private static Workbook CreateNumberFormats()
    {
        var workbook = NewWorkbook("generated-number-formats-003");
        var sheet = workbook.AddSheet("Formats");
        var styles = new[]
        {
            workbook.RegisterStyle(new CellStyle { NumberFormat = "#,##0.00" }),
            workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00%" }),
            workbook.RegisterStyle(new CellStyle { NumberFormat = "0.00E+00" }),
            workbook.RegisterStyle(new CellStyle { NumberFormat = "yyyy-mm-dd" }),
            workbook.RegisterStyle(new CellStyle { NumberFormat = "m/d/yyyy" }),
            workbook.RegisterStyle(new CellStyle { NumberFormat = "_($* #,##0_);_($* (#,##0);_($* \"-\"_);_(@_)" }),
            workbook.RegisterStyle(new CellStyle { NumberFormat = "# ??/??" }),
            workbook.RegisterStyle(new CellStyle { NumberFormat = "0.0\\%;" }),
            workbook.RegisterStyle(new CellStyle { NumberFormat = "[>=1000000]#.0,,\"M\";[>=1000]#.0,\"K\";#.0" }),
            workbook.RegisterStyle(new CellStyle { NumberFormat = "@" })
        };
        // Both date format slots (index 3 and 4) use DateTimeValue with date formats ClosedXML recognizes.
        var dateSerial = DateTimeValue.FromDateTime(new DateTime(2026, 5, 28));
        var values = new ScalarValue[] { new NumberValue(1234.56), new NumberValue(0.1234), new NumberValue(1234567), dateSerial, dateSerial, new NumberValue(9876.54), new NumberValue(1.5), new NumberValue(0.75), new NumberValue(1500000), new TextValue("Text value") };
        for (int i = 0; i < 10; i++)
        {
            uint r = (uint)(i + 1);
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), values[i]);
            sheet.GetCell(new CellAddress(sheet.Id, r, 1))!.StyleId = styles[i];
        }
        return workbook;
    }

    private static Workbook CreateValidationMessages()
    {
        var workbook = NewWorkbook("generated-validation-messages-003");
        var sheet = workbook.AddSheet("DV Messages");
        Set(sheet, "A1", new TextValue("Name")); Set(sheet, "B1", new TextValue("Score")); Set(sheet, "C1", new TextValue("Status"));
        sheet.DataValidations.Add(new DataValidation { AppliesTo = Range(sheet, "A2", "A20"), Type = DvType.TextLength, Operator = DvOperator.LessThanOrEqual, Formula1 = "30", ShowInputMessage = true, PromptTitle = "Name Limit", PromptMessage = "Names must be 30 characters or fewer.", ShowErrorMessage = true, AlertStyle = DvAlertStyle.Stop, ErrorTitle = "Name Too Long", ErrorMessage = "Please enter a name with 30 characters or fewer." });
        sheet.DataValidations.Add(new DataValidation { AppliesTo = Range(sheet, "B2", "B20"), Type = DvType.WholeNumber, Operator = DvOperator.Between, Formula1 = "0", Formula2 = "100", ShowInputMessage = true, PromptTitle = "Score Range", PromptMessage = "Enter a score between 0 and 100.", ShowErrorMessage = true, AlertStyle = DvAlertStyle.Warning, ErrorTitle = "Score Out of Range", ErrorMessage = "Score should be between 0 and 100." });
        sheet.DataValidations.Add(new DataValidation { AppliesTo = Range(sheet, "C2", "C20"), Type = DvType.List, Formula1 = "Pass,Fail,Pending", ShowInputMessage = true, PromptTitle = "Select Status", PromptMessage = "Choose Pass, Fail, or Pending.", ShowErrorMessage = true, AlertStyle = DvAlertStyle.Information, ErrorTitle = "Unknown Status", ErrorMessage = "Please select a recognized status value." });
        return workbook;
    }

    private static Workbook CreateRowColumnGroups()
    {
        var workbook = NewWorkbook("generated-row-column-groups-003");
        var sheet = workbook.AddSheet("Groups");
        for (uint r = 1; r <= 10; r++) { Set(sheet, $"A{r}", new TextValue($"Item {r}")); Set(sheet, $"B{r}", new NumberValue(r * 10)); }
        sheet.RowOutlineLevels[2] = 1; sheet.RowOutlineLevels[3] = 1; sheet.RowOutlineLevels[4] = 1;
        sheet.RowOutlineLevels[6] = 2; sheet.RowOutlineLevels[7] = 2;
        sheet.ColOutlineLevels[3] = 1; sheet.ColOutlineLevels[4] = 1;
        sheet.HiddenRows.Add(6); sheet.HiddenRows.Add(7);
        Formula(sheet, "C1", "SUBTOTAL(9,B2:B4)");
        Formula(sheet, "C5", "SUBTOTAL(9,B6:B7)");
        Formula(sheet, "C10", "SUM(B1:B9)");
        return workbook;
    }

    private static Workbook CreateMixedChartTypes()
    {
        var workbook = NewWorkbook("generated-mixed-chart-types-003");
        var sheet = workbook.AddSheet("Mix Charts");
        Set(sheet, "A1", new TextValue("Month")); Set(sheet, "B1", new TextValue("Val"));
        for (int r = 2; r <= 5; r++) { Set(sheet, $"A{r}", new TextValue($"M{r - 1}")); Set(sheet, $"B{r}", new NumberValue(r * 25)); Set(sheet, $"C{r}", new NumberValue(r * 15)); }
        sheet.Charts.Add(new ChartModel { Type = ChartType.Line, DataRange = Range(sheet, "A1", "B5"), Title = "Line" });
        sheet.Charts.Add(new ChartModel { Type = ChartType.Area, DataRange = Range(sheet, "A1", "B5"), Title = "Area" });
        sheet.Charts.Add(new ChartModel { Type = ChartType.Bar, DataRange = Range(sheet, "A1", "B5"), Title = "Bar" });
        sheet.Charts.Add(new ChartModel { Type = ChartType.Scatter, DataRange = Range(sheet, "B1", "C5"), Title = "Scatter" });
        return workbook;
    }

    private static Workbook CreateLargeNamedRanges()
    {
        var workbook = NewWorkbook("generated-large-named-ranges-003");
        var sheet = workbook.AddSheet("Ranges");
        for (uint r = 1; r <= 10; r++) { Set(sheet, $"A{r}", new NumberValue(r * 10)); Set(sheet, $"B{r}", new NumberValue(r * 5)); }
        workbook.DefineNamedRange("Col_A", Range(sheet, "A1", "A10"));
        workbook.DefineNamedRange("Col_B", Range(sheet, "B1", "B10"));
        workbook.DefineNamedRange("Header_Row", Range(sheet, "A1", "B1"));
        workbook.DefineNamedRange("Mid_Row", Range(sheet, "A5", "B5"));
        workbook.DefineNamedRange("Last_Row", Range(sheet, "A10", "B10"));
        workbook.DefineNamedRange("TopFive", Range(sheet, "A1", "B5"));
        workbook.DefineNamedRange("BottomFive", Range(sheet, "A6", "B10"));
        workbook.DefineNamedRange("AllData", Range(sheet, "A1", "B10"));
        workbook.DefineNamedRange("Odd_Rows", Range(sheet, "A1", "A1"));
        workbook.DefineNamedRange("Even_Rows", Range(sheet, "A2", "A2"));
        var calc = workbook.AddSheet("Calc");
        Formula(calc, "A1", "SUM(Col_A)");
        Formula(calc, "A2", "AVERAGE(Col_B)");
        Formula(calc, "A3", "SUM(TopFive)");
        Formula(calc, "A4", "SUM(BottomFive)");
        Formula(calc, "A5", "INDEX(AllData,3,1)");
        Formula(calc, "A6", "VLOOKUP(50,Col_A,1,TRUE)");
        Formula(calc, "A7", "SUM(Header_Row)");
        Formula(calc, "A8", "SUM(Mid_Row)");
        Formula(calc, "A9", "SUM(Last_Row)");
        Formula(calc, "A10", "COUNT(AllData)");
        return workbook;
    }

}
