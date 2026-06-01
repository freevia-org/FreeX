using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    private static List<string> SchemaErrors(Workbook workbook)
    {
        using var stream = Save(workbook);
        using var document = SpreadsheetDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }
}
