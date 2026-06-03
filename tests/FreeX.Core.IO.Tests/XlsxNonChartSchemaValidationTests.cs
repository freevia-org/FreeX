using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Widens the Open XML SDK schema-validation gate (see <see cref="XlsxSchemaValidationTests"/>) beyond
/// charts to non-chart feature workbooks: data validation, conditional formatting (color scale / data
/// bar / icon set), structured tables, named ranges, merged cells, comments, and freeze/split panes.
/// These guard that schema-invalid OOXML for those features cannot regress silently (the same class of
/// bug as the chart bodyPr / grouping / dataId fixes), since Microsoft Excel rejects schema-invalid
/// packages outright.
/// </summary>
public sealed class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void DataValidation_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("DataValidation");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "100",
            ShowInputMessage = true,
            PromptTitle = "Enter a number",
            PromptMessage = "Between 1 and 100",
            ShowErrorMessage = true,
            ErrorTitle = "Invalid",
            ErrorMessage = "Out of range",
            AlertStyle = DvAlertStyle.Stop,
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, 2, 2, 5, 2),
            Type = DvType.List,
            Formula1 = "\"Red,Green,Blue\"",
            ShowDropdown = true,
        });

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void DataValidation_UnquotedInlineList_QuotesFormulaForExcelOpenability()
    {
        var workbook = new Workbook("DataValidationInlineList");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Type = DvType.List,
            Formula1 = "Red,Green,Blue",
            ShowDropdown = true,
        });

        var worksheetXml = WorksheetXml(workbook);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        worksheetXml.Root!
            .Element(worksheetNs + "dataValidations")!
            .Element(worksheetNs + "dataValidation")!
            .Element(worksheetNs + "formula1")!
            .Value
            .Should()
            .Be("\"Red,Green,Blue\"");
        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void ConditionalFormat_ColorScale_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("ColorScaleCf");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 2),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
        });

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void ConditionalFormat_DataBar_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("DataBarCf");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarShowValue = true,
        });

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void ConditionalFormat_DataBar_WithX14Extensions_ProducesSchemaValidWorkbook()
    {
        // DataBar border / axis / negative-fill colors require the x14 (2009) dataBar extension, which is
        // emitted as a worksheet <extLst>. extLst is the final CT_Worksheet child, so it must stay last.
        var workbook = new Workbook("DataBarX14Cf");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Priority = 1,
            RuleType = CfRuleType.DataBar,
            DataBarShowValue = true,
            DataBarGradient = false,
            DataBarBorder = true,
            DataBarAxisPosition = "middle",
            DataBarAxisColor = new RgbColor(0, 0, 0),
            DataBarNegativeFillColor = new RgbColor(255, 0, 0),
            DataBarNegativeBorderColor = new RgbColor(255, 0, 0),
        });

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void BackgroundImage_WithSparklines_KeepsPictureBeforeWorksheetExtensions()
    {
        var workbook = new Workbook("BackgroundSparklineOrder");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));
        sheet.BackgroundImage = new WorksheetBackgroundImage(MinimalPngBytes(), "image/png", "background.png");
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = Range(sheet, 1, 1, 1, 3),
            Location = new CellAddress(sheet.Id, 1, 4),
            Kind = SparklineKind.Line
        });

        var worksheetXml = WorksheetXml(workbook);
        var childNames = worksheetXml.Root!.Elements().Select(element => element.Name.LocalName).ToList();
        childNames.IndexOf("picture").Should().BeLessThan(childNames.IndexOf("extLst"));
        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void ConditionalFormat_IconSet_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("IconSetCf");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
            IconSetThresholds =
            {
                new CfThresholdModel(CfThresholdType.Percent, "0"),
                new CfThresholdModel(CfThresholdType.Percent, "33"),
                new CfThresholdModel(CfThresholdType.Percent, "67"),
            },
        });

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void ConditionalFormat_CellValue_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("CellValueCf");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "10",
            FormatIfTrue = new CellStyle { Bold = true },
        });

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void ConditionalFormat_DifferentialStyleWithFontNumberAndFill_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("DifferentialStyleOrderCf");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Priority = 1,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 2,
            FormatIfTrue = new CellStyle
            {
                Bold = true,
                Italic = true,
                Underline = true,
                Strikethrough = true,
                FontColor = new CellColor(255, 255, 255),
                FontName = "Arial",
                FontSize = 12,
                FillColor = new CellColor(31, 78, 121),
                NumberFormat = "yyyy-mm-dd"
            }
        });

        using var stream = Save(workbook);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var dxf = LoadPackageXml(archive.GetEntry("xl/styles.xml")!)
            .Root!
            .Element(workbookNs + "dxfs")!
            .Element(workbookNs + "dxf")!;

        dxf.Elements().Select(element => element.Name.LocalName).Should().ContainInOrder("font", "numFmt", "fill");
        dxf.Element(workbookNs + "font")!
            .Elements()
            .Select(element => element.Name.LocalName)
            .Should()
            .ContainInOrder("b", "i", "strike", "u", "sz", "color", "name");

        stream.Position = 0;
        SchemaErrors(stream).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookSave_DropsExcelRevisionUidsAndKeepsIndexedDifferentialStylesSchemaValid()
    {
        var source = Save(CreateExcelAuthoredSchemaRegressionWorkbook());
        AddExcelRevisionUidMetadata(source);
        ReplaceDifferentialStylesWithExcelIndexedStyles(source);

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        SchemaErrors(saved).Should().BeEmpty();

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        workbookXml.Descendants().SelectMany(element => element.Attributes()).Where(IsOfficeRevisionAttribute).Should().BeEmpty();
        worksheetXml.Descendants().SelectMany(element => element.Attributes()).Where(IsOfficeRevisionAttribute).Should().BeEmpty();

        var dxf = LoadPackageXml(archive.GetEntry("xl/styles.xml")!)
            .Root!
            .Element(workbookNs + "dxfs")!
            .Elements(workbookNs + "dxf")
            .First();
        dxf.Element(workbookNs + "numFmt").Should().BeNull();
        dxf.Element(workbookNs + "font")!.Element(workbookNs + "b").Should().BeNull();
    }

    [Fact]
    public void LoadedWorkbookSave_DropsMalformedSourceOnlyRelationshipParts()
    {
        var workbook = new Workbook("MalformedRelationships");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var source = Save(workbook);
        AddMalformedSourceOnlyRelationshipPart(source);

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("customXml/item99.xml").Should().NotBeNull();
        archive.GetEntry("customXml/_rels/item99.xml.rels").Should().BeNull();
        SchemaErrors(saved).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookSave_RemovesSourceNativeCustomViewsForExcelCompatibility()
    {
        var workbook = new Workbook("CustomViewSchemaRepair");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var source = Save(workbook);
        AddLowercaseNativeCustomViews(source);

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        LoadPackageXml(archive.GetEntry("xl/workbook.xml")!)
            .Root!
            .Element(workbookNs + "customWorkbookViews")
            .Should()
            .BeNull();
        LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!)
            .Root!
            .Element(workbookNs + "customSheetViews")
            .Should()
            .BeNull();
        SchemaErrors(saved).Should().BeEmpty();
    }

    [Fact]
    public void StyleFonts_WithUnderlineStrikeAndBaselineVertAlign_ProduceSchemaValidWorkbook()
    {
        var workbook = new Workbook("RegularFontOrder");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            Value = new TextValue("styled"),
            StyleId = workbook.RegisterStyle(new CellStyle
            {
                Italic = true,
                Underline = true,
                Strikethrough = true,
                Subscript = true,
                FontSize = 9,
                FontColor = new CellColor(255, 0, 0),
                FontName = "Arial"
            })
        });

        using var stream = Save(workbook);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var styledFont = LoadPackageXml(archive.GetEntry("xl/styles.xml")!)
            .Root!
            .Element(workbookNs + "fonts")!
            .Elements(workbookNs + "font")
            .Last();

        styledFont.Elements().Select(element => element.Name.LocalName).Should().ContainInOrder(
            "i",
            "strike",
            "u",
            "vertAlign",
            "sz",
            "color",
            "name",
            "family");

        stream.Position = 0;
        SchemaErrors(stream).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookSave_SanitizesRichSharedStringFontFamiliesForSchemaValidity()
    {
        var workbook = new Workbook("RichSharedStringFonts");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Rich font"));

        var source = Save(workbook);
        AddCssFontFamilyRichSharedString(source);

        source.Position = 0;
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rFont = LoadPackageXml(archive.GetEntry("xl/sharedStrings.xml")!)
            .Root!
            .Elements(workbookNs + "si")
            .Single(element => element.Value == "Rich font")
            .Element(workbookNs + "r")!
            .Element(workbookNs + "rPr")!
            .Element(workbookNs + "rFont")!;
        rFont.Attribute("val")!.Value.Should().Be("Google Sans");

        saved.Position = 0;
        SchemaErrors(saved).Should().BeEmpty();
    }

    [Fact]
    public void StructuredTable_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("StructuredTable");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(2));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, 1, 1, 3, 2),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Name"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Value"));
        sheet.StructuredTables.Add(table);

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void NamedRanges_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("NamedRanges");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        workbook.DefineNamedRange("MyRange", Range(sheet, 2, 1, 5, 1));
        workbook.DefineNamedRange("SingleCell", Range(sheet, 1, 1, 1, 1));

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void MergedCells_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("MergedCells");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Merged Header"));
        SeedNumericGrid(sheet);
        sheet.AddMergedRegion(Range(sheet, 1, 1, 1, 3));
        sheet.AddMergedRegion(Range(sheet, 2, 4, 4, 4));

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void Comments_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("Comments");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "First comment";
        sheet.Comments[new CellAddress(sheet.Id, 2, 2)] = "Second comment";

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void FreezePanes_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("FreezePanes");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 1;

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void SplitPanes_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("SplitPanes");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.SplitRow = 3;
        sheet.SplitColumn = 2;
        sheet.ViewTopRow = 1;
        sheet.ViewLeftCol = 1;

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void ManualPageBreaks_UseExcelCompatibleSpanBounds()
    {
        var workbook = new Workbook("ManualPageBreaks");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.RowPageBreaks.Add(20);
        sheet.ColumnPageBreaks.Add(4);

        var worksheetXml = WorksheetXml(workbook);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rowBreak = worksheetXml.Root!
            .Element(worksheetNs + "rowBreaks")!
            .Element(worksheetNs + "brk")!;
        rowBreak.Attribute("max")!.Value.Should().Be("16383");
        rowBreak.Attribute("man")!.Value.Should().Be("1");

        var columnBreak = worksheetXml.Root!
            .Element(worksheetNs + "colBreaks")!
            .Element(worksheetNs + "brk")!;
        columnBreak.Attribute("max")!.Value.Should().Be("1048575");
        columnBreak.Attribute("man")!.Value.Should().Be("1");
        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void CombinedNonChartFeatures_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("Combined");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);
        sheet.FrozenRows = 1;
        sheet.AddMergedRegion(Range(sheet, 1, 1, 1, 2));
        sheet.Comments[new CellAddress(sheet.Id, 3, 3)] = "Note";
        workbook.DefineNamedRange("Combined_Range", Range(sheet, 2, 1, 5, 2));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Type = DvType.Decimal,
            Operator = DvOperator.GreaterThan,
            Formula1 = "0",
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 2, 5, 2),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = false,
        });

        SchemaErrors(workbook).Should().BeEmpty();
    }

    [Fact]
    public void PivotTable_ProducesSchemaValidWorkbook()
    {
        var workbook = new Workbook("PivotTable");
        var sheet = workbook.AddSheet("PivotData");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A1:B3",
            PackagePart = "xl/pivotCache/pivotCacheDefinition1.xml",
            RecordCount = 2,
            CreatedVersion = 8,
            MinRefreshableVersion = 4,
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount", 4));
        workbook.PivotCaches.Add(cache);

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, 1, 1, 3, 2),
            TargetRange = Range(sheet, 5, 1, 8, 2),
            PackagePart = "xl/pivotTables/pivotTable1.xml",
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 4));
        sheet.PivotTables.Add(pivot);

        SchemaErrors(workbook).Should().BeEmpty();
    }

    private static void SeedNumericGrid(Sheet sheet)
    {
        for (uint row = 2; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 3));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 7));
        }
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));

    private static MemoryStream Save(Workbook workbook)
    {
        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    private static Workbook CreateExcelAuthoredSchemaRegressionWorkbook()
    {
        var workbook = new Workbook("ExcelAuthoredSchemaRegression");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Metric"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        for (uint row = 2; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Item {row - 1}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        var hyperlinkAddress = new CellAddress(sheet.Id, 5, 1);
        sheet.Hyperlinks[hyperlinkAddress] = "https://example.com/freex";
        sheet.HyperlinkMetadata[hyperlinkAddress] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Excel smoke link",
            "");

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 2, 5, 2),
            Priority = 1,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 2,
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(0, 97, 0),
                FillColor = new CellColor(198, 239, 206)
            }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 2, 5, 2),
            Priority = 2,
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = new CellStyle
            {
                Bold = true,
                FontColor = new CellColor(255, 255, 255),
                FillColor = new CellColor(31, 78, 121)
            }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = Range(sheet, 2, 2, 5, 2),
            Priority = 3,
            RuleType = CfRuleType.AboveAverage,
            FormatIfTrue = new CellStyle { NumberFormat = "yyyy-mm-dd" }
        });

        return workbook;
    }

    private static void AddExcelRevisionUidMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace markupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        XNamespace revisionNs = "http://schemas.microsoft.com/office/spreadsheetml/2014/revision";
        XNamespace revision2Ns = "http://schemas.microsoft.com/office/spreadsheetml/2015/revision2";

        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        workbookXml.Root!.SetAttributeValue(XNamespace.Xmlns + "mc", markupCompatNs.NamespaceName);
        workbookXml.Root.SetAttributeValue(XNamespace.Xmlns + "xr2", revision2Ns.NamespaceName);
        workbookXml.Root.SetAttributeValue(markupCompatNs + "Ignorable", AppendIgnorablePrefix(workbookXml.Root.Attribute(markupCompatNs + "Ignorable")?.Value, "xr2"));
        workbookXml.Root
            .Element(workbookNs + "bookViews")!
            .Element(workbookNs + "workbookView")!
            .SetAttributeValue(revision2Ns + "uid", "{48973FB0-6DDF-407F-BFF1-05D2BBB0F9CF}");
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        worksheetXml.Root!.SetAttributeValue(XNamespace.Xmlns + "mc", markupCompatNs.NamespaceName);
        worksheetXml.Root.SetAttributeValue(XNamespace.Xmlns + "xr", revisionNs.NamespaceName);
        worksheetXml.Root.SetAttributeValue(markupCompatNs + "Ignorable", AppendIgnorablePrefix(worksheetXml.Root.Attribute(markupCompatNs + "Ignorable")?.Value, "xr"));
        worksheetXml.Root
            .Element(workbookNs + "hyperlinks")!
            .Elements(workbookNs + "hyperlink")
            .Single(element => element.Attribute("ref")?.Value == "A5")
            .SetAttributeValue(revisionNs + "uid", "{EB1F693D-8528-450A-BC10-895DEFE5B6D9}");
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void ReplaceDifferentialStylesWithExcelIndexedStyles(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var stylesXml = LoadPackageXml(archive.GetEntry("xl/styles.xml")!);
        stylesXml.Root!
            .Element(workbookNs + "dxfs")!
            .ReplaceWith(new XElement(
                workbookNs + "dxfs",
                new XAttribute("count", "3"),
                new XElement(
                    workbookNs + "dxf",
                    new XElement(
                        workbookNs + "font",
                        new XElement(workbookNs + "color", new XAttribute("rgb", "FF006100"))),
                    new XElement(
                        workbookNs + "fill",
                        new XElement(
                            workbookNs + "patternFill",
                            new XElement(workbookNs + "bgColor", new XAttribute("rgb", "FFC6EFCE"))))),
                new XElement(
                    workbookNs + "dxf",
                    new XElement(
                        workbookNs + "font",
                        new XElement(workbookNs + "b"),
                        new XElement(workbookNs + "i", new XAttribute("val", "0")),
                        new XElement(workbookNs + "strike", new XAttribute("val", "0")),
                        new XElement(workbookNs + "condense", new XAttribute("val", "0")),
                        new XElement(workbookNs + "extend", new XAttribute("val", "0")),
                        new XElement(workbookNs + "outline", new XAttribute("val", "0")),
                        new XElement(workbookNs + "shadow", new XAttribute("val", "0")),
                        new XElement(workbookNs + "u", new XAttribute("val", "none")),
                        new XElement(workbookNs + "vertAlign", new XAttribute("val", "baseline")),
                        new XElement(workbookNs + "sz", new XAttribute("val", "11")),
                        new XElement(workbookNs + "color", new XAttribute("rgb", "FFFFFFFF")),
                        new XElement(workbookNs + "name", new XAttribute("val", "Aptos Narrow")),
                        new XElement(workbookNs + "family", new XAttribute("val", "2")),
                        new XElement(workbookNs + "scheme", new XAttribute("val", "minor"))),
                    new XElement(
                        workbookNs + "fill",
                        new XElement(
                            workbookNs + "patternFill",
                            new XAttribute("patternType", "solid"),
                            new XElement(workbookNs + "fgColor", new XAttribute("indexed", "64")),
                            new XElement(workbookNs + "bgColor", new XAttribute("rgb", "FF1F4E79"))))),
                new XElement(
                    workbookNs + "dxf",
                    new XElement(
                        workbookNs + "numFmt",
                        new XAttribute("numFmtId", "165"),
                        new XAttribute("formatCode", @"yyyy\-mm\-dd")))));
        ReplacePackageXml(archive, "xl/styles.xml", stylesXml);
    }

    private static void AddMalformedSourceOnlyRelationshipPart(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace packageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var customXmlEntry = archive.CreateEntry("customXml/item99.xml", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(customXmlEntry.Open()))
        {
            writer.Write("<item xmlns=\"urn:freex:test\" />");
        }

        ReplacePackageXml(archive, "customXml/_rels/item99.xml.rels", new XDocument(
            new XElement(
                packageRelationshipNs + "Relationships",
                new XElement(
                    packageRelationshipNs + "Relationship",
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath"),
                    new XAttribute("TargetMode", "External")))));
    }

    private static void AddLowercaseNativeCustomViews(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string lowercaseGuid = "{a9519446-ebd3-4d7e-9dba-8858ead6d331}";

        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        workbookXml.Root!.Add(new XElement(
            workbookNs + "customWorkbookViews",
            new XElement(
                workbookNs + "customWorkbookView",
                new XAttribute("name", "NativeView"),
                new XAttribute("guid", lowercaseGuid),
                new XAttribute("autoUpdate", "0"),
                new XAttribute("mergeInterval", "0"),
                new XAttribute("personalView", "0"))));
        ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        worksheetXml.Root!.Add(new XElement(
            workbookNs + "customSheetViews",
            new XElement(
                workbookNs + "customSheetView",
                new XAttribute("guid", lowercaseGuid),
                new XAttribute("scale", "90"),
                new XAttribute("state", "visible"))));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AddCssFontFamilyRichSharedString(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sharedStringsXml = LoadPackageXml(archive.GetEntry("xl/sharedStrings.xml")!);
        var sharedString = sharedStringsXml.Root!
            .Elements(workbookNs + "si")
            .Single(element => element.Element(workbookNs + "t")?.Value == "Rich font");
        sharedString.ReplaceNodes(new XElement(
            workbookNs + "r",
            new XElement(
                workbookNs + "rPr",
                new XElement(workbookNs + "rFont", new XAttribute("val", "\"Google Sans\", Roboto, sans-serif")),
                new XElement(workbookNs + "sz", new XAttribute("val", "11"))),
            new XElement(workbookNs + "t", "Rich font")));
        ReplacePackageXml(archive, "xl/sharedStrings.xml", sharedStringsXml);
    }

    private static XDocument WorksheetXml(Workbook workbook)
    {
        using var stream = Save(workbook);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        return LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
    }

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static string AppendIgnorablePrefix(string? current, string prefix)
    {
        var prefixes = (current ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (!prefixes.Contains(prefix, StringComparer.Ordinal))
            prefixes.Add(prefix);
        return string.Join(" ", prefixes);
    }

    private static List<string> SchemaErrors(Workbook workbook)
    {
        using var stream = Save(workbook);
        return SchemaErrors(stream);
    }

    private static List<string> SchemaErrors(Stream stream)
    {
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }

    private static bool IsOfficeRevisionAttribute(XAttribute attribute) =>
        !attribute.IsNamespaceDeclaration &&
        string.Equals(attribute.Name.LocalName, "uid", StringComparison.Ordinal) &&
        attribute.Name.NamespaceName.StartsWith("http://schemas.microsoft.com/office/spreadsheetml/", StringComparison.Ordinal) &&
        attribute.Name.NamespaceName.Contains("/revision", StringComparison.Ordinal);

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
}
