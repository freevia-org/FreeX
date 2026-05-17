using System.IO.Compression;
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
        "generated-objects-001",
        "generated-charts-001",
        "generated-protection-page-setup-001"
    };

    public static bool CanCreate(string id) => SupportedIds.Contains(id);

    public static bool CanCreateKnownGapPackage(string id) => id switch
    {
        "generated-color-scales-001" => true,
        "generated-data-bars-001" => true,
        "generated-text-boxes-shapes-001" => true,
        "generated-images-sparklines-001" => true,
        "generated-threaded-comments-001" => true,
        "generated-track-changes-001" => true,
        "generated-unsupported-chart-001" => true,
        "generated-vba-macros-001" => true,
        "generated-pivots-001" => true,
        "generated-power-query-001" => true,
        "generated-data-model-001" => true,
        "generated-linked-data-types-001" => true,
        "generated-slicers-001" => true,
        "generated-timelines-001" => true,
        "generated-external-links-001" => true,
        "generated-embedded-objects-001" => true,
        "generated-custom-xml-001" => true,
        _ => false
    };

    public static Workbook Create(string id) => id switch
    {
        "generated-grid-basic-001" => CreateGridBasic(),
        "generated-formulas-001" => CreateFormulas(),
        "generated-cross-sheet-001" => CreateCrossSheet(),
        "generated-formatting-001" => CreateFormatting(),
        "generated-structure-001" => CreateStructure(),
        "generated-validation-001" => CreateValidation(),
        "generated-conditional-formatting-001" => CreateConditionalFormatting(),
        "generated-objects-001" => CreateObjects(),
        "generated-charts-001" => CreateCharts(),
        "generated-protection-page-setup-001" => CreateProtectionAndPageSetup(),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "No generated XLSX corpus fixture exists for this id.")
    };

    public static MemoryStream CreateKnownGapPackage(string id) => id switch
    {
        "generated-color-scales-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <conditionalFormatting sqref="A1:A5">
                <cfRule type="colorScale" priority="1">
                  <colorScale/>
                </cfRule>
              </conditionalFormatting>
            </worksheet>
            """)),
        "generated-data-bars-001" => CreatePackage(("xl/worksheets/sheet1.xml", """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <conditionalFormatting sqref="A1:A5">
                <cfRule type="dataBar" priority="1">
                  <dataBar/>
                </cfRule>
              </conditionalFormatting>
            </worksheet>
            """)),
        "generated-text-boxes-shapes-001" => CreatePackage(("xl/drawings/drawing1.xml", """
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing">
              <xdr:twoCellAnchor>
                <xdr:sp/>
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """)),
        "generated-images-sparklines-001" => CreatePackage(
            ("xl/drawings/drawing1.xml", """
                <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing">
                  <xdr:twoCellAnchor>
                    <xdr:pic/>
                  </xdr:twoCellAnchor>
                </xdr:wsDr>
                """),
            ("xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                           xmlns:x14="http://schemas.microsoft.com/office/spreadsheetml/2009/9/main">
                  <extLst>
                    <ext>
                      <x14:sparklineGroups/>
                    </ext>
                  </extLst>
                </worksheet>
                """)),
        "generated-threaded-comments-001" => CreatePackage(
            ("xl/threadedComments/threadedComment1.xml", "<threadedComments/>"),
            ("xl/persons/person.xml", "<persons/>")),
        "generated-track-changes-001" => CreatePackage(
            ("xl/revisionHeaders/revisionHeader1.xml", "<revisionHeader/>"),
            ("xl/revisions/revisionLog1.xml", "<revisionLog/>")),
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
        "generated-external-links-001" => CreatePackage(("xl/externalLinks/externalLink1.xml", "<externalLink/>")),
        "generated-embedded-objects-001" => CreatePackage(("xl/embeddings/oleObject1.bin", "Freexcel generated OLE placeholder")),
        "generated-custom-xml-001" => CreatePackage(("customXml/item1.xml", "<freexcelGeneratedCustomXml/>")),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "No generated known-gap XLSX package fixture exists for this id.")
    };

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
