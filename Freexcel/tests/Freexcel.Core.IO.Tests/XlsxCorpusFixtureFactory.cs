using System.IO.Compression;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

internal static class XlsxCorpusFixtureFactory
{
    private static readonly HashSet<string> SupportedIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "generated-grid-basic-001",
        "generated-formulas-001",
        "generated-cross-sheet-001",
        "generated-formatting-001",
        "generated-structure-001",
        "generated-validation-001",
        "generated-conditional-formatting-001",
        "generated-color-scales-001",
        "generated-data-bars-001",
        "generated-text-boxes-shapes-001",
        "generated-images-sparklines-001",
        "generated-objects-001",
        "generated-charts-001",
        "generated-pivots-001",
        "generated-structured-tables-001",
        "generated-protection-page-setup-001"
    };

    public static bool CanCreate(string id) => SupportedIds.Contains(id);

    public static bool CanCreateKnownGapPackage(string id) => id switch
    {
        "generated-threaded-comments-001" => true,
        "generated-track-changes-001" => true,
        "generated-form-controls-001" => true,
        "generated-digital-signatures-001" => true,
        "generated-custom-ribbon-ui-001" => true,
        "generated-office-addins-001" => true,
        "generated-live-web-queries-001" => true,
        "generated-sensitivity-labels-001" => true,
        "generated-smartart-diagrams-001" => true,
        "generated-printer-settings-001" => true,
        "generated-unsupported-sheet-types-001" => true,
        "generated-unsupported-chart-001" => true,
        "generated-vba-macros-001" => true,
        "generated-power-query-001" => true,
        "generated-data-model-001" => true,
        "generated-linked-data-types-001" => true,
        "generated-slicers-001" => true,
        "generated-timelines-001" => true,
        "generated-embedded-objects-001" => true,
        "generated-custom-xml-001" => true,
        _ => false
    };

    public static bool CanCreateKnownGapRetentionPackage(string id) => CanCreateKnownGapPackage(id);

    public static Workbook Create(string id) => id switch
    {
        "generated-grid-basic-001" => CreateGridBasic(),
        "generated-formulas-001" => CreateFormulas(),
        "generated-cross-sheet-001" => CreateCrossSheet(),
        "generated-formatting-001" => CreateFormatting(),
        "generated-structure-001" => CreateStructure(),
        "generated-validation-001" => CreateValidation(),
        "generated-conditional-formatting-001" => CreateConditionalFormatting(),
        "generated-color-scales-001" => CreateColorScales(),
        "generated-data-bars-001" => CreateDataBars(),
        "generated-text-boxes-shapes-001" => CreateTextBoxesAndShapes(),
        "generated-images-sparklines-001" => CreateImagesAndSparklines(),
        "generated-objects-001" => CreateObjects(),
        "generated-charts-001" => CreateCharts(),
        "generated-pivots-001" => CreatePivots(),
        "generated-structured-tables-001" => CreateStructuredTables(),
        "generated-protection-page-setup-001" => CreateProtectionAndPageSetup(),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "No generated XLSX corpus fixture exists for this id.")
    };

    public static MemoryStream CreateKnownGapPackage(string id) => id switch
    {
        "generated-text-boxes-shapes-001" => CreatePackage(("xl/drawings/drawing1.xml", """
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing">
              <xdr:twoCellAnchor>
                <xdr:sp/>
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """)),
        "generated-threaded-comments-001" => CreatePackage(
            ("xl/threadedComments/threadedComment1.xml", "<threadedComments/>"),
            ("xl/persons/person.xml", "<persons/>")),
        "generated-track-changes-001" => CreatePackage(
            ("xl/revisionHeaders/revisionHeader1.xml", "<revisionHeader/>"),
            ("xl/revisions/revisionLog1.xml", "<revisionLog/>")),
        "generated-form-controls-001" => CreatePackage(
            ("xl/activeX/activeX1.xml", "<activeX/>"),
            ("xl/activeX/activeX1.bin", "Freexcel generated ActiveX placeholder"),
            ("xl/ctrlProps/ctrlProp1.xml", "<controlProperties/>")),
        "generated-digital-signatures-001" => CreatePackage(
            ("_xmlsignatures/origin.sigs", "Freexcel generated signature origin placeholder"),
            ("_xmlsignatures/sig1.xml", "<Signature/>")),
        "generated-custom-ribbon-ui-001" => CreatePackage(("customUI/customUI.xml", """
            <customUI xmlns="http://schemas.microsoft.com/office/2006/01/customui">
              <ribbon/>
            </customUI>
            """)),
        "generated-office-addins-001" => CreatePackage(
            ("xl/webextensions/taskpanes.xml", "<taskpanes/>"),
            ("xl/webextensions/webextension1.xml", "<webextension/>")),
        "generated-live-web-queries-001" => CreatePackage(("xl/webPublishItems.xml", "<webPublishItems/>")),
        "generated-sensitivity-labels-001" => CreatePackage(("docProps/custom.xml", """
            <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties"
                        xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
              <property name="MSIP_Label_01234567-89ab-cdef-0123-456789abcdef_Enabled">
                <vt:lpwstr>true</vt:lpwstr>
              </property>
            </Properties>
            """)),
        "generated-smartart-diagrams-001" => CreatePackage(
            ("xl/diagrams/data1.xml", "<dgm:dataModel/>"),
            ("xl/diagrams/layout1.xml", "<dgm:layoutDef/>"),
            ("xl/diagrams/quickStyle1.xml", "<dgm:styleDef/>")),
        "generated-printer-settings-001" => CreatePackage(("xl/printerSettings/printerSettings1.bin", "Freexcel generated printer settings placeholder")),
        "generated-unsupported-sheet-types-001" => CreatePackage(
            ("xl/chartsheets/sheet1.xml", "<chartsheet/>"),
            ("xl/dialogSheets/sheet2.xml", "<dialogsheet/>"),
            ("xl/macroSheets/sheet3.xml", "<macrosheet/>")),
        "generated-unsupported-chart-001" => CreatePackage(("xl/charts/chart1.xml", """
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:radarChart/>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """)),
        "generated-vba-macros-001" => CreatePackage(("xl/vbaProject.bin", "Freexcel generated macro placeholder")),
        "generated-pivots-001" => CreatePackage(
            ("xl/pivotTables/pivotTable1.xml", "<pivotTableDefinition/>"),
            ("xl/pivotCache/pivotCacheDefinition1.xml", "<pivotCacheDefinition/>")),
        "generated-power-query-001" => CreatePackage(
            ("xl/connections.xml", "<connections/>"),
            ("xl/queries/query1.xml", "<query/>"),
            ("xl/queryTables/queryTable1.xml", "<queryTable/>")),
        "generated-data-model-001" => CreatePackage(
            ("xl/model/item.data", "Freexcel generated data model placeholder"),
            ("xl/model/item.xml", "<dataModel/>")),
        "generated-linked-data-types-001" => CreatePackage(
            ("xl/richData/rdrichvalue.xml", "<rvData/>"),
            ("xl/richData/rdRichValueTypes.xml", "<rvTypes/>"),
            ("xl/richData/richValueRel.xml", "<richValueRels/>")),
        "generated-slicers-001" => CreatePackage(
            ("xl/slicers/slicer1.xml", "<slicer/>"),
            ("xl/slicerCaches/slicerCache1.xml", "<slicerCacheDefinition/>")),
        "generated-timelines-001" => CreatePackage(
            ("xl/timelines/timeline1.xml", "<timeline/>"),
            ("xl/timelineCaches/timelineCache1.xml", "<timelineCacheDefinition/>")),
        "generated-embedded-objects-001" => CreatePackage(("xl/embeddings/oleObject1.bin", "Freexcel generated OLE placeholder")),
        "generated-custom-xml-001" => CreatePackage(("customXml/item1.xml", "<freexcelGeneratedCustomXml/>")),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "No generated known-gap XLSX package fixture exists for this id.")
    };

    public static MemoryStream CreateKnownGapRetentionPackage(string id)
    {
        using var knownGapPackage = CreateKnownGapPackage(id);
        var workbook = NewWorkbook($"retention-{id}");
        var sheet = workbook.AddSheet("Sheet1");
        Set(sheet, "A1", new TextValue(id));
        Set(sheet, "B1", new NumberValue(1));

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using (var sourceArchive = new ZipArchive(knownGapPackage, ZipArchiveMode.Read, leaveOpen: true))
        using (var targetArchive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            foreach (var sourceEntry in sourceArchive.Entries)
            {
                targetArchive.GetEntry(sourceEntry.FullName)?.Delete();
                var targetEntry = targetArchive.CreateEntry(sourceEntry.FullName);
                using var sourceStream = sourceEntry.Open();
                using var targetStream = targetEntry.Open();
                sourceStream.CopyTo(targetStream);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static Workbook CreateGridBasic()
    {
        var workbook = NewWorkbook("generated-grid-basic-001");
        var sheet = workbook.AddSheet("Grid");
        Set(sheet, "A1", new TextValue("Text"));
        Set(sheet, "B1", new NumberValue(123.45));
        Set(sheet, "C1", new BoolValue(true));
        Set(sheet, "D1", DateTimeValue.FromDateTime(new DateTime(2026, 5, 17, 9, 30, 0)));
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
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, "A2", "A10"),
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            ErrorTitle = "Invalid choice",
            ErrorMessage = "Choose a listed item."
        });
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
            DataBarShowValue = false
        });
        return workbook;
    }

    private static Workbook CreateImagesAndSparklines()
    {
        var workbook = NewWorkbook("generated-images-sparklines-001");
        var sheet = workbook.AddSheet("Images Sparklines");
        Set(sheet, "A1", new NumberValue(1));
        Set(sheet, "B1", new NumberValue(2));
        Set(sheet, "C1", new NumberValue(3));
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = Addr(sheet, "E2"),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 120,
            Height = 80,
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
            Anchor = Addr(sheet, "B2"),
            Text = "Corpus note",
            Width = 200,
            Height = 90,
            FillColor = new CellColor(255, 242, 204),
            OutlineColor = new CellColor(112, 48, 160),
            AltText = "Corpus text box"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = Addr(sheet, "D5"),
            Kind = DrawingShapeKind.Ellipse,
            Width = 140,
            Height = 90,
            FillColor = new CellColor(221, 235, 247),
            OutlineColor = new CellColor(31, 78, 121),
            AltText = "Corpus ellipse"
        });
        return workbook;
    }

    private static Workbook CreateObjects()
    {
        var workbook = NewWorkbook("generated-objects-001");
        var sheet = workbook.AddSheet("Objects");
        Set(sheet, "A1", new TextValue("Documentation"));
        Set(sheet, "B1", new TextValue("Review note"));
        sheet.Hyperlinks[Addr(sheet, "A1")] = "https://example.com/freexcel";
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
        Set(sheet, "A2", new TextValue("Jan"));
        Set(sheet, "A3", new TextValue("Feb"));
        Set(sheet, "A4", new TextValue("Mar"));
        Set(sheet, "B2", new NumberValue(100));
        Set(sheet, "B3", new NumberValue(120));
        Set(sheet, "B4", new NumberValue(140));
        Set(sheet, "C2", new NumberValue(0.2));
        Set(sheet, "C3", new NumberValue(0.25));
        Set(sheet, "C4", new NumberValue(0.3));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = Range(sheet, "A1", "C4"),
            Title = "Sales by Month",
            XAxisTitle = "Month",
            YAxisTitle = "Sales",
            ShowLegend = true,
            ShowDataLabels = true,
            LegendPosition = ChartLegendPosition.Bottom,
            SeriesFormats = [new ChartSeriesFormat(0, FillColor: new CellColor(68, 114, 196))]
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
            DataRange = Range(sheet, "A1", "C4"),
            Title = "Stock View",
            ShowLegend = true
        });
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
            PackagePart = "xl/tables/table1.xml"
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Category"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
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
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml"
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", 4));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "A5", "B8"),
            PackagePart = "xl/pivotTables/pivotTable1.xml"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 4));
        sheet.PivotTables.Add(pivot);
        return workbook;
    }

    private static Workbook CreateProtectionAndPageSetup()
    {
        var workbook = NewWorkbook("generated-protection-page-setup-001");
        var sheet = workbook.AddSheet("Print");
        Set(sheet, "A1", new TextValue("Protected print fixture"));
        Set(sheet, "A2", new NumberValue(42));
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
        sheet.PageHeader = new WorksheetHeaderFooter("Freexcel", "Corpus", "2026");
        sheet.PageFooter = new WorksheetHeaderFooter("", "Page &P", "");
        return workbook;
    }

    private static Workbook NewWorkbook(string name) => new(name);

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static void Set(Sheet sheet, string a1, ScalarValue value) =>
        sheet.SetCell(Addr(sheet, a1), value);

    private static void Set(Sheet sheet, string a1, ScalarValue value, StyleId styleId)
    {
        var address = Addr(sheet, a1);
        sheet.SetCell(address, value);
        sheet.GetCell(address)!.StyleId = styleId;
    }

    private static void Formula(Sheet sheet, string a1, string formula) =>
        sheet.SetFormula(Addr(sheet, a1), formula);

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

    private static MemoryStream CreatePackage(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryName, content) in entries)
            {
                var entry = archive.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
