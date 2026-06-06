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

public sealed partial class XlsxNonChartSchemaValidationTests
{
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
    public void LoadedWorkbookPatchSave_WithStructuredTable_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateStructuredTableSourceWorkbook());
        var sourceTablePart = ReadPackageRootElement(source, "xl/tables/table1.xml");
        var sourceTableParts = ReadWorksheetChildElement(source, "tableParts");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadPackageRootElement(saved, "xl/tables/table1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceTablePart.ToString(SaveOptions.DisableFormatting));
        ReadWorksheetChildElement(saved, "tableParts")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceTableParts.ToString(SaveOptions.DisableFormatting));
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

    private static Workbook CreateStructuredTableSourceWorkbook()
    {
        var workbook = new Workbook("StructuredTablePatchSave");
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

        return workbook;
    }

    private static XElement ReadPackageRootElement(Stream stream, string entryName)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        return new XElement(LoadPackageXml(archive.GetEntry(entryName)!).Root!);
    }

}
