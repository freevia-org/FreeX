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

}
