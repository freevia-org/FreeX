using FluentAssertions;
using FreeX.App.Presentation;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using System.Globalization;

namespace FreeX.App.Host.Tests;

public sealed class SpreadsheetDisplayFormatterTests
{
    [Fact]
    public void FormatRangeReference_UsesA1OrR1C1Mode()
    {
        var sheetId = SheetId.New();
        var start = new CellAddress(sheetId, 2, 3);
        var end = new CellAddress(sheetId, 4, 5);

        SpreadsheetDisplayFormatter.FormatRangeReference(start, end, useR1C1ReferenceStyle: false)
            .Should().Be("C2:E4");
        SpreadsheetDisplayFormatter.FormatRangeReference(start, end, useR1C1ReferenceStyle: true)
            .Should().Be("R2C3:R4C5");
    }

    [Fact]
    public void FormatCellAndColumnReference_FormatsA1AndR1C1WithoutIntermediateReferences()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 1_048_576, 16_384);

        SpreadsheetDisplayFormatter.FormatCellReference(address, useR1C1ReferenceStyle: false)
            .Should().Be("XFD1048576");
        SpreadsheetDisplayFormatter.FormatCellReference(address, useR1C1ReferenceStyle: true)
            .Should().Be("R1048576C16384");
        SpreadsheetDisplayFormatter.FormatColumnReference(16_384, useR1C1ReferenceStyle: false)
            .Should().Be("XFD");
        SpreadsheetDisplayFormatter.FormatColumnReference(16_384, useR1C1ReferenceStyle: true)
            .Should().Be("C16384");
    }

    [Fact]
    public void FormatFormulaBarText_ConvertsFormulaToR1C1WhenRequested()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 3, 3);
        var cell = Cell.FromFormula("A1+B2");

        SpreadsheetDisplayFormatter.FormatFormulaBarText(cell, address, useR1C1ReferenceStyle: false)
            .Should().Be("=A1+B2");
        SpreadsheetDisplayFormatter.FormatFormulaBarText(cell, address, useR1C1ReferenceStyle: true)
            .Should().Be("=R[-2]C[-2]+R[-1]C[-1]");
    }

    [Fact]
    public void FormatCellValue_UsesExcelStyleScalarText()
    {
        SpreadsheetDisplayFormatter.FormatCellValue(new BoolValue(true)).Should().Be("TRUE");
        SpreadsheetDisplayFormatter.FormatCellValue(new TextValue("hello")).Should().Be("hello");
        SpreadsheetDisplayFormatter.FormatCellValue(ErrorValue.DivByZero).Should().Be("#DIV/0!");
    }

    [Fact]
    public void FormatFormulaBarText_BuiltInCurrentDateUsesLocalizedShortDate()
    {
        var workbook = new Workbook("Dates");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromValue(new NumberValue(DateTimeEntryService.CurrentDate(new DateTime(2026, 8, 31)).Value));
        cell.StyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = DateTimeEntryService.CurrentDateNumberFormat });
        sheet.SetCell(address, cell);

        SpreadsheetDisplayFormatter.FormatFormulaBarText(
                cell,
                address,
                useR1C1ReferenceStyle: false,
                sheet,
                workbook,
                CultureInfo.GetCultureInfo("en-GB"))
            .Should().Be("31/08/2026");
    }
}
