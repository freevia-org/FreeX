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
    public void LoadedWorkbookPatchSave_WithDataValidation_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateDataValidationSourceWorkbook());
        var sourceDataValidations = ReadDataValidationsElement(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadDataValidationsElement(saved)
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceDataValidations.ToString(SaveOptions.DisableFormatting));
    }

    private static Workbook CreateDataValidationSourceWorkbook()
    {
        var workbook = new Workbook("DataValidationPatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "100",
            AllowBlank = true,
            ShowInputMessage = true,
            PromptTitle = "Enter a number",
            PromptMessage = "Between 1 and 100",
            ShowErrorMessage = true,
            ErrorTitle = "Invalid",
            ErrorMessage = "Out of range",
            AlertStyle = DvAlertStyle.Warning,
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, 2, 2, 5, 2),
            Type = DvType.List,
            Formula1 = "\"Red,Green,Blue\"",
            ShowDropdown = true,
        });

        return workbook;
    }

    private static XElement ReadDataValidationsElement(Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        return new XElement(worksheetXml.Root!.Element(worksheetNs + "dataValidations")!);
    }

}
