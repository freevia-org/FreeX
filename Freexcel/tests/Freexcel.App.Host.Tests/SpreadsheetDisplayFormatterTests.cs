using FluentAssertions;
using Freexcel.App.Host;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

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
}
