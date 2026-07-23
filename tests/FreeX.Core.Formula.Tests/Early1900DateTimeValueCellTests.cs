using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R82-datetimevalue-1900-serial: end-to-end pins for a CELL holding a DateTimeValue built from a
/// calendar date (what typed entry, paste, and every text/spreadsheet reader produce) in the
/// 1900-01-01..1900-02-28 window. The date functions read the stored value as a true Excel serial,
/// so before the fix a cell created from 1900-01-15 held the OADate 16 and YEAR/MONTH/DAY, plain
/// arithmetic, and the number formatter all reported 1900-01-16.
///
/// Distinct from <see cref="ExcelParityDateSerialTests"/>, which covers DATE()/DATEVALUE() — those
/// build serials through ExcelDateSystem directly and were never affected.
/// </summary>
public sealed class Early1900DateTimeValueCellTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet SheetWithDate(DateTime date)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(date));
        return sheet;
    }

    [Theory]
    [InlineData(1900, 1, 15)]
    [InlineData(1900, 1, 1)]
    [InlineData(1900, 2, 28)]
    [InlineData(1900, 3, 1)]
    [InlineData(2024, 1, 15)]
    public void DatePartFunctions_OverADateTimeValueCell_ReturnTheDateItWasBuiltFrom(int year, int month, int day)
    {
        var sheet = SheetWithDate(new DateTime(year, month, day));

        _eval.Evaluate("=YEAR(A1)", sheet).Should().Be(new NumberValue(year));
        _eval.Evaluate("=MONTH(A1)", sheet).Should().Be(new NumberValue(month));
        _eval.Evaluate("=DAY(A1)", sheet).Should().Be(new NumberValue(day));
    }

    [Theory]
    [InlineData(1900, 1, 15, 15)]
    [InlineData(1900, 2, 28, 59)]
    [InlineData(1900, 3, 1, 61)]
    public void DateTimeValueCell_CoercesToTheSameSerialAsDate(int year, int month, int day, double serial)
    {
        var sheet = SheetWithDate(new DateTime(year, month, day));

        _eval.Evaluate("=A1+0", sheet).Should().Be(new NumberValue(serial));
        _eval.Evaluate($"=A1-DATE({year},{month},{day})", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void AddingADayToAnEarly1900DateTimeValueCell_AdvancesByOneCalendarDay()
    {
        var sheet = SheetWithDate(new DateTime(1900, 1, 15));

        _eval.Evaluate("=A1+1", sheet).Should().Be(new NumberValue(16));
        _eval.Evaluate("=DAY(A1+1)", sheet).Should().Be(new NumberValue(16));
        _eval.Evaluate("=MONTH(A1+1)", sheet).Should().Be(new NumberValue(1));
    }

    [Theory]
    [InlineData(1900, 1, 15, "1/15/1900")]
    [InlineData(1900, 2, 28, "2/28/1900")]
    [InlineData(1900, 3, 1, "3/1/1900")]
    public void NumberFormatter_RendersADateTimeValueCellAsTheDateItWasBuiltFrom(
        int year, int month, int day, string expected)
    {
        var value = DateTimeValue.FromDateTime(new DateTime(year, month, day));

        NumberFormatter.Format(value, "m/d/yyyy", uses1904DateSystem: false).Should().Be(expected);
    }
}
