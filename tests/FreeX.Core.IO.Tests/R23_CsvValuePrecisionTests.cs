using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-23 regression tests for csv-value-precision:
/// - R23-csv-text-import-export-2: CSV export must write a formula cell's calculated value, not
///   its formula source text (CSV has no formula syntax; real Excel's CSV Save-As always exports
///   the computed result).
/// - R23-csv-text-import-export-3: CSV import must cap a parsed numeric literal at Excel's
///   15-significant-digit storage precision, the same cap the calc engine applies elsewhere.
/// </summary>
public sealed class R23_CsvValuePrecisionTests
{
    [Fact]
    public void Save_ExportsFormulaCellComputedValue_NotFormulaSourceText()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "A1*2");
        // The calc engine computes this normally; simulate that here since this unit test does not
        // run a full recalc pass.
        sheet.GetCell(new CellAddress(sheet.Id, 1, 2))!.Value = new NumberValue(20);

        using var stream = new MemoryStream();
        new CsvFileAdapter().Save(workbook, stream);

        var csv = Encoding.UTF8.GetString(stream.ToArray());
        csv.Should().Be("10,20\r\n");
        csv.Should().NotContain("=A1*2");
    }

    [Fact]
    public void CoerceValue_RoundsSeventeenDigitNumericLiteralToFifteenSignificantDigits()
    {
        // 17 significant digits in the fractional part; Excel caps stored numbers at 15 significant
        // digits regardless of source (typed, pasted, or CSV-imported).
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("1.2345678901234567\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        var value = sheet.GetValue(new CellAddress(sheet.Id, 1, 1));
        value.Should().BeOfType<NumberValue>();
        ((NumberValue)value).Value.Should().Be(1.23456789012346d);
    }

    [Fact]
    public void CoerceValue_RoundsSixteenDigitDecimalLiteralToFifteenSignificantDigits()
    {
        // 16 significant digits split across the integer/fractional boundary — still within the
        // magnitude range where Math.Round(value, scale) (scale clamped to [0, 15], mirroring
        // RecalcEngine.RoundToSignificantDigits) can express the 15-significant-digit cap.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("123456789012345.6\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        var value = sheet.GetValue(new CellAddress(sheet.Id, 1, 1));
        value.Should().BeOfType<NumberValue>();
        ((NumberValue)value).Value.Should().Be(123456789012346d);
    }

    [Fact]
    public void CoerceValue_PreservesTinyFiniteNumericLiteral()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("5E-200\r\n"));
        var workbook = new CsvFileAdapter().Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(new NumberValue(5e-200));
    }
}
