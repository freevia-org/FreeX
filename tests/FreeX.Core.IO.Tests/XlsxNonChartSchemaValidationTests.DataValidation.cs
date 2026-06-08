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
    private const string DataValidationsExtensionUri = "{FREEX-DATA-VALIDATIONS-EXT}";
    private const string DataValidationExtensionUri = "{FREEX-DATA-VALIDATION-EXT}";

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
    public void DataValidationExtensionLists_RemovesInvalidNativeMetadataForSchemaValidity()
    {
        var workbook = new Workbook("DataValidationExtensionListInvalidSchema");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, 2, 1, 5, 1),
            Type = DvType.List,
            Formula1 = "\"Red,Green,Blue\"",
            NativeContainerChildXmls =
            [
                CreateInvalidExtensionListXml(DataValidationsExtensionUri, "FreeXDataValidationsExtension", "customDataValidationsExtLstFlag", "customDataValidationsExtFlag", "nativeDataValidationsExtLstChild"),
                CreateDuplicateExtensionListXml("{FREEX-DUPLICATE-DATA-VALIDATIONS-EXTLST}"),
                "<nativeDataValidationsChild xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />"
            ],
            NativeChildXmls =
            [
                CreateInvalidExtensionListXml(DataValidationExtensionUri, "FreeXDataValidationExtension", "customDataValidationExtLstFlag", "customDataValidationExtFlag", "nativeDataValidationExtLstChild"),
                CreateDuplicateExtensionListXml("{FREEX-DUPLICATE-DATA-VALIDATION-EXTLST}"),
                "<nativeDataValidationChild xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" />"
            ]
        });

        using var stream = Save(workbook);

        SchemaErrors(stream).Should().BeEmpty();
        AssertDataValidationInvalidExtensionListsRemoved(stream);
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
    public void DataValidationAdditionalTypes_ProducesSchemaValidWorkbook()
    {
        using var stream = Save(CreateAdditionalDataValidationTypesSourceWorkbook());

        SchemaErrors(stream).Should().BeEmpty();
        AssertAdditionalDataValidationTypesAuthored(stream);
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
        AssertDataValidationModel(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        AssertAdditionalDataValidationTypesAuthored(saved);
        ReadDataValidationsElement(saved)
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceDataValidations.ToString(SaveOptions.DisableFormatting));

        saved.Position = 0;
        AssertDataValidationModel(adapter.Load(saved).GetSheetAt(0));
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithAdditionalDataValidationTypes_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateAdditionalDataValidationTypesSourceWorkbook());
        var sourceDataValidations = ReadDataValidationsElement(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        AssertAdditionalDataValidationTypesModel(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 8, 8), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadDataValidationsElement(saved)
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceDataValidations.ToString(SaveOptions.DisableFormatting));

        saved.Position = 0;
        AssertAdditionalDataValidationTypesModel(adapter.Load(saved).GetSheetAt(0));
    }

    [Fact]
    public void LoadedWorkbookPatchSave_SanitizesInvalidDataValidationExtensionListsForSchemaValidity()
    {
        using var source = Save(CreateDataValidationSourceWorkbook());
        SetDataValidationExtensionListsInvalidNativeMetadata(source);
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        AssertDataValidationModel(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 8, 8), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch, adapter.LastSaveDiagnostics.Reason);
        SchemaErrors(saved).Should().BeEmpty();
        AssertDataValidationInvalidExtensionListsRemoved(saved);

        saved.Position = 0;
        AssertDataValidationModel(adapter.Load(saved).GetSheetAt(0));
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

    private static Workbook CreateAdditionalDataValidationTypesSourceWorkbook()
    {
        var workbook = new Workbook("DataValidationAdditionalTypesPatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        var decimalValidation = new DataValidation
        {
            AppliesTo = Range(sheet, 2, 3, 5, 3),
            Type = DvType.Decimal,
            Operator = DvOperator.Between,
            Formula1 = "0",
            Formula2 = "1",
            AlertStyle = DvAlertStyle.Information,
            ErrorTitle = "Decimal required",
            ErrorMessage = "Enter a value from 0 through 1"
        };
        decimalValidation.AdditionalRanges.Add(Range(sheet, 2, 8, 5, 8));
        sheet.DataValidations.Add(decimalValidation);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, 2, 4, 5, 4),
            Type = DvType.Date,
            Operator = DvOperator.GreaterThanOrEqual,
            Formula1 = "DATE(2026,1,1)"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, 2, 5, 5, 5),
            Type = DvType.Time,
            Operator = DvOperator.Between,
            Formula1 = "TIME(8,0,0)",
            Formula2 = "TIME(18,0,0)"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, 2, 6, 5, 6),
            Type = DvType.TextLength,
            Operator = DvOperator.LessThanOrEqual,
            Formula1 = "50"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = Range(sheet, 2, 7, 5, 7),
            Type = DvType.Custom,
            Formula1 = "LEN(G2)>0"
        });

        return workbook;
    }

    private static void AssertAdditionalDataValidationTypesAuthored(Stream stream)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var dataValidations = ReadDataValidationsElement(stream);
        var validations = dataValidations.Elements(worksheetNs + "dataValidation").ToList();

        dataValidations.Attribute("count")!.Value.Should().Be("5");
        validations
            .Select(element => element.Attribute("type")?.Value)
            .Should()
            .BeEquivalentTo(["decimal", "date", "time", "textLength", "custom"]);
        validations
            .Single(element => element.Attribute("type")?.Value == "decimal")
            .Attribute("sqref")!
            .Value
            .Should()
            .Contain("C2:C5")
            .And
            .Contain("H2:H5");
        validations
            .Single(element => element.Attribute("type")?.Value == "decimal")
            .Attribute("operator")!
            .Value
            .Should()
            .Be("between");
        validations
            .Single(element => element.Attribute("type")?.Value == "date")
            .Element(worksheetNs + "formula1")!
            .Value
            .Should()
            .Be("DATE(2026,1,1)");
        validations
            .Single(element => element.Attribute("type")?.Value == "time")
            .Element(worksheetNs + "formula2")!
            .Value
            .Should()
            .Be("TIME(18,0,0)");
        validations
            .Single(element => element.Attribute("type")?.Value == "textLength")
            .Attribute("operator")!
            .Value
            .Should()
            .Be("lessThanOrEqual");
        validations
            .Single(element => element.Attribute("type")?.Value == "custom")
            .Element(worksheetNs + "formula1")!
            .Value
            .Should()
            .Be("LEN(G2)>0");
    }

    private static void AssertDataValidationModel(Sheet sheet)
    {
        sheet.DataValidations.Should().HaveCount(2);

        var wholeNumber = sheet.DataValidations.Single(validation => validation.Type == DvType.WholeNumber);
        wholeNumber.AppliesTo.ToString().Should().Be("A2:A5");
        wholeNumber.Operator.Should().Be(DvOperator.Between);
        wholeNumber.Formula1.Should().Be("1");
        wholeNumber.Formula2.Should().Be("100");
        wholeNumber.AllowBlank.Should().BeTrue();
        wholeNumber.ShowInputMessage.Should().BeTrue();
        wholeNumber.PromptTitle.Should().Be("Enter a number");
        wholeNumber.PromptMessage.Should().Be("Between 1 and 100");
        wholeNumber.ShowErrorMessage.Should().BeTrue();
        wholeNumber.ErrorTitle.Should().Be("Invalid");
        wholeNumber.ErrorMessage.Should().Be("Out of range");
        wholeNumber.AlertStyle.Should().Be(DvAlertStyle.Warning);

        var list = sheet.DataValidations.Single(validation => validation.Type == DvType.List);
        list.AppliesTo.ToString().Should().Be("B2:B5");
        list.Formula1.Should().Be("Red,Green,Blue");
        list.ShowDropdown.Should().BeTrue();
    }

    private static void AssertAdditionalDataValidationTypesModel(Sheet sheet)
    {
        sheet.DataValidations.Should().HaveCount(5);
        sheet.DataValidations.Select(validation => validation.Type)
            .Should()
            .BeEquivalentTo([DvType.Decimal, DvType.Date, DvType.Time, DvType.TextLength, DvType.Custom]);

        var decimalValidation = sheet.DataValidations.Single(validation => validation.Type == DvType.Decimal);
        decimalValidation.AppliesTo.ToString().Should().Be("C2:C5");
        decimalValidation.AdditionalRanges.Select(range => range.ToString()).Should().ContainSingle().Which.Should().Be("H2:H5");
        decimalValidation.AppliesTo.Should().Be(Range(sheet, 2, 3, 5, 3));
        decimalValidation.AdditionalRanges.Should().ContainSingle()
            .Which.Should().Be(Range(sheet, 2, 8, 5, 8));
        decimalValidation.Operator.Should().Be(DvOperator.Between);
        decimalValidation.Formula1.Should().Be("0");
        decimalValidation.Formula2.Should().Be("1");
        decimalValidation.AlertStyle.Should().Be(DvAlertStyle.Information);

        sheet.DataValidations.Single(validation => validation.Type == DvType.Date).Formula1.Should().Be("DATE(2026,1,1)");
        sheet.DataValidations.Single(validation => validation.Type == DvType.Time).Formula2.Should().Be("TIME(18,0,0)");
        sheet.DataValidations.Single(validation => validation.Type == DvType.TextLength).Operator.Should().Be(DvOperator.LessThanOrEqual);
        sheet.DataValidations.Single(validation => validation.Type == DvType.Custom).Formula1.Should().Be("LEN(G2)>0");
    }

    private static XElement ReadDataValidationsElement(Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        return new XElement(worksheetXml.Root!.Element(worksheetNs + "dataValidations")!);
    }

    private static void SetDataValidationExtensionListsInvalidNativeMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var dataValidations = worksheetXml.Root!.Element(worksheetNs + "dataValidations")!;
        dataValidations.Add(
            CreateInvalidExtensionList(worksheetNs, DataValidationsExtensionUri, "FreeXDataValidationsExtension", "customDataValidationsExtLstFlag", "customDataValidationsExtFlag", "nativeDataValidationsExtLstChild"),
            new XElement(worksheetNs + "extLst", new XElement(worksheetNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-DATA-VALIDATIONS-EXTLST}"))),
            new XElement(worksheetNs + "nativeDataValidationsChild"));

        var validation = dataValidations.Element(worksheetNs + "dataValidation")!;
        validation.Add(
            CreateInvalidExtensionList(worksheetNs, DataValidationExtensionUri, "FreeXDataValidationExtension", "customDataValidationExtLstFlag", "customDataValidationExtFlag", "nativeDataValidationExtLstChild"),
            new XElement(worksheetNs + "extLst", new XElement(worksheetNs + "ext", new XAttribute("uri", "{FREEX-DUPLICATE-DATA-VALIDATION-EXTLST}"))),
            new XElement(worksheetNs + "nativeDataValidationChild"));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static void AssertDataValidationInvalidExtensionListsRemoved(Stream stream)
    {
        var dataValidations = ReadDataValidationsElement(stream);
        var worksheetNs = dataValidations.Name.Namespace;
        dataValidations.Elements(worksheetNs + "extLst").Should().BeEmpty();
        dataValidations.Element(worksheetNs + "nativeDataValidationsChild").Should().BeNull();

        var validation = dataValidations.Element(worksheetNs + "dataValidation")!;
        validation.Elements(worksheetNs + "extLst").Should().BeEmpty();
        validation.Element(worksheetNs + "nativeDataValidationChild").Should().BeNull();
        validation.Element(worksheetNs + "formula1").Should().NotBeNull();
    }

}
