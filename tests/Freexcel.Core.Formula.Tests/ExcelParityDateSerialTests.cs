using System.Globalization;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

public sealed class ExcelParityDateSerialTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData("=DATE(1900,1,1)", 1)]
    [InlineData("=DATE(1900,2,28)", 59)]
    [InlineData("=DATE(1900,2,29)", 60)]
    [InlineData("=DATE(1900,3,0)", 60)]
    [InlineData("=DATE(1900,2,0)", 31)]
    [InlineData("=DATE(1900,3,-1)", 59)]
    [InlineData("=DATE(1900,3,1)", 61)]
    [InlineData("=DATE(2024,1,15)", 45306)]
    public void Date_ReturnsExcelSerialNumbers(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void DateAndTime_RangeFirstArgument_SpillElementwise()
    {
        var years = Sheet((1, 1, 2024), (2, 1, 2025));
        var hours = Sheet((1, 1, 1), (2, 1, 13));

        AssertColumn(_eval.Evaluate("=DATE(A1:A2,1,15)", years), new NumberValue(45306), new NumberValue(45672));
        AssertColumn(
            _eval.Evaluate("=TIME(A1:A2,30,0)", hours),
            new NumberValue(0.0625),
            new NumberValue(0.5625));
    }

    [Theory]
    [InlineData("=DATE(1900,0,31)")]
    [InlineData("=DATE(1900,-1,62)")]
    public void Date_ReturnsNumWhenMonthUnderflowReachesSerialZero(string formula)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.Num);
    }

    [Theory]
    [InlineData("=YEAR(1)", 1900)]
    [InlineData("=MONTH(1)", 1)]
    [InlineData("=DAY(1)", 1)]
    [InlineData("=WEEKDAY(1)", 1)]
    [InlineData("=YEAR(0)", 1900)]
    [InlineData("=MONTH(0)", 1)]
    [InlineData("=DAY(0)", 0)]
    [InlineData("=WEEKDAY(0)", 7)]
    [InlineData("=WEEKNUM(0)", 0)]
    [InlineData("=ISOWEEKNUM(0)", 52)]
    [InlineData("=YEAR(59)", 1900)]
    [InlineData("=MONTH(59)", 2)]
    [InlineData("=DAY(59)", 28)]
    [InlineData("=WEEKDAY(59)", 3)]
    [InlineData("=YEAR(60)", 1900)]
    [InlineData("=MONTH(60)", 2)]
    [InlineData("=DAY(60)", 29)]
    [InlineData("=WEEKDAY(60)", 4)]
    [InlineData("=YEAR(61)", 1900)]
    [InlineData("=MONTH(61)", 3)]
    [InlineData("=DAY(61)", 1)]
    [InlineData("=WEEKDAY(61)", 5)]
    public void DatePartFunctions_InterpretExcelSerialNumbers(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData(1900, 1, 1, 1)]
    [InlineData(1900, 2, 28, 59)]
    [InlineData(1900, 2, 29, 60)]
    [InlineData(1900, 3, 1, 61)]
    [InlineData(1999, 12, 31, 36525)]
    [InlineData(2024, 2, 29, 45351)]
    public void DateAndDateParts_RoundTripAcrossExcelSerialBoundaries(int year, int month, int day, double serial)
    {
        var sheet = Sheet();
        var dateExpression = $"DATE({year},{month},{day})";

        _eval.Evaluate($"={dateExpression}", sheet).Should().Be(new NumberValue(serial));
        _eval.Evaluate($"=YEAR({dateExpression})", sheet).Should().Be(new NumberValue(year));
        _eval.Evaluate($"=MONTH({dateExpression})", sheet).Should().Be(new NumberValue(month));
        _eval.Evaluate($"=DAY({dateExpression})", sheet).Should().Be(new NumberValue(day));
    }

    [Theory]
    [InlineData("=YEAR(-1)")]
    [InlineData("=MONTH(-1)")]
    [InlineData("=DAY(-1)")]
    [InlineData("=WEEKDAY(-1)")]
    [InlineData("=WEEKNUM(-1)")]
    [InlineData("=ISOWEEKNUM(-1)")]
    [InlineData("=EDATE(-1,1)")]
    [InlineData("=EOMONTH(-1,0)")]
    [InlineData("=DATEDIF(-1,1,\"D\")")]
    public void DateFunctions_ReturnNumForNegativeSerials(string formula)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.Num);
    }

    [Theory]
    [InlineData("=DATEVALUE(\"1/1/1900\")", 1)]
    [InlineData("=DATEVALUE(\"2/28/1900\")", 59)]
    [InlineData("=DATEVALUE(\"2/29/1900\")", 60)]
    [InlineData("=DATEVALUE(\"1900-02-29\")", 60)]
    [InlineData("=DATEVALUE(\"3/1/1900\")", 61)]
    [InlineData("=DATEVALUE(\"2024-01-15\")", 45306)]
    public void DateValue_ReturnsExcelSerialNumbers(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=TIMEVALUE(\"00:00:00\")", 0)]
    [InlineData("=TIMEVALUE(\"12:00:00\")", 0.5)]
    [InlineData("=TIMEVALUE(\"23:59:59\")", 0.999988425925926)]
    [InlineData("=TIMEVALUE(\"12:00 AM\")", 0)]
    [InlineData("=TIMEVALUE(\"12:00 PM\")", 0.5)]
    public void TimeValue_ReturnsExcelDayFraction(string formula, double expected)
    {
        var result = _eval.Evaluate(formula, Sheet()).Should().BeOfType<NumberValue>().Subject;

        result.Value.Should().BeApproximately(expected, 1e-12);
    }

    [Theory]
    [InlineData("=TIME(1.9,2.9,3.9)", 0.0430902777777778)]
    [InlineData("=TIME(25,61,61)", 0.0847337962962964)]
    [InlineData("=TIME(0,0,32767)", 0.379247685185185)]
    public void Time_TruncatesComponentsAndWrapsWithinDay(string formula, double expected)
    {
        var result = _eval.Evaluate(formula, Sheet()).Should().BeOfType<NumberValue>().Subject;

        result.Value.Should().BeApproximately(expected, 1e-12);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(12, 0, 0, 12, 0, 0)]
    [InlineData(23, 59, 59, 23, 59, 59)]
    [InlineData(25, 61, 61, 2, 2, 1)]
    [InlineData(1.9, 2.9, 3.9, 1, 2, 3)]
    public void TimeAndTimeParts_RoundTripTruncatedWrappedComponents(
        double hour,
        double minute,
        double second,
        double expectedHour,
        double expectedMinute,
        double expectedSecond)
    {
        var sheet = Sheet();
        var timeExpression = string.Create(
            CultureInfo.InvariantCulture,
            $"TIME({hour},{minute},{second})");

        _eval.Evaluate($"=HOUR({timeExpression})", sheet).Should().Be(new NumberValue(expectedHour));
        _eval.Evaluate($"=MINUTE({timeExpression})", sheet).Should().Be(new NumberValue(expectedMinute));
        _eval.Evaluate($"=SECOND({timeExpression})", sheet).Should().Be(new NumberValue(expectedSecond));
    }

    [Theory]
    [InlineData("=HOUR(-0.25)")]
    [InlineData("=MINUTE(-0.25)")]
    [InlineData("=SECOND(-0.25)")]
    public void TimePartFunctions_ReturnNumForNegativeSerials(string formula)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.Num);
    }

    [Theory]
    [InlineData("=EDATE(DATE(1900,1,1),1)", 32)]
    [InlineData("=EDATE(DATE(1900,1,31),1)", 59)]
    [InlineData("=EDATE(DATE(1900,2,28),1)", 88)]
    [InlineData("=EOMONTH(DATE(1900,1,1),0)", 31)]
    [InlineData("=EOMONTH(DATE(1900,1,1),1)", 59)]
    [InlineData("=EOMONTH(DATE(1900,2,1),0)", 59)]
    [InlineData("=EOMONTH(DATE(1900,2,28),0)", 59)]
    [InlineData("=EOMONTH(DATE(1900,3,1),0)", 91)]
    public void DateOffsetFunctions_ReturnExcelSerialNumbers(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=DATEDIF(DATE(1900,1,1),DATE(1900,3,1),\"D\")", 60)]
    [InlineData("=DATEDIF(DATE(1900,2,28),DATE(1900,3,1),\"D\")", 2)]
    public void DatedifDays_UsesExcelSerialBoundaries(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=DATEDIF(DATE(1900,2,28),DATE(1900,3,1),\"YD\")", 2)]
    [InlineData("=DATEDIF(DATE(1900,1,31),DATE(1900,3,1),\"MD\")", -1)]
    [InlineData("=DATEDIF(DATE(1900,2,28),DATE(1901,3,1),\"YD\")", 1)]
    [InlineData("=DATEDIF(DATE(1900,2,28),DATE(1901,3,1),\"MD\")", 1)]
    public void DatedifRelativeUnits_UseExcelSerialBoundaries(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=WORKDAY(DATE(1900,1,1),1)", 2)]
    [InlineData("=WORKDAY(DATE(1900,1,5),1)", 6)]
    [InlineData("=WORKDAY(DATE(1900,1,5),-1)", 4)]
    [InlineData("=WORKDAY.INTL(DATE(1900,1,1),1,\"0000011\")", 2)]
    [InlineData("=WORKDAY.INTL(DATE(1900,1,5),1,\"0000011\")", 6)]
    [InlineData("=WORKDAY.INTL(DATE(1900,1,5),-1,\"0000011\")", 4)]
    public void WorkdayFunctions_ReturnExcelSerialNumbers(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=NETWORKDAYS(DATE(1900,1,1),DATE(1900,1,5))", 4)]
    [InlineData("=NETWORKDAYS(DATE(1900,1,5),DATE(1900,1,1))", -4)]
    [InlineData("=NETWORKDAYS(DATE(1900,1,6),DATE(1900,1,7))", 1)]
    [InlineData("=NETWORKDAYS.INTL(DATE(1900,1,1),DATE(1900,1,5),\"0000011\")", 4)]
    [InlineData("=NETWORKDAYS.INTL(DATE(1900,1,5),DATE(1900,1,1),\"0000011\")", -4)]
    [InlineData("=NETWORKDAYS.INTL(DATE(1900,1,6),DATE(1900,1,7),\"0000011\")", 1)]
    public void NetworkdaysFunctions_CountExcelSerialWeekdays(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=WEEKNUM(DATE(1900,1,1),1)", 1)]
    [InlineData("=WEEKNUM(DATE(1900,1,7),1)", 1)]
    [InlineData("=WEEKNUM(DATE(1900,1,8),1)", 2)]
    [InlineData("=WEEKNUM(DATE(1900,1,1),2)", 1)]
    [InlineData("=WEEKNUM(DATE(1900,1,7),2)", 2)]
    [InlineData("=WEEKNUM(DATE(1900,1,8),2)", 2)]
    [InlineData("=WEEKNUM(DATE(1900,1,1),21)", 52)]
    [InlineData("=WEEKNUM(DATE(1900,1,7),21)", 1)]
    [InlineData("=WEEKNUM(DATE(1900,1,8),21)", 1)]
    public void Weeknum_UsesExcelSerialWeekdays(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=ISOWEEKNUM(DATE(1900,1,1))", 52)]
    [InlineData("=ISOWEEKNUM(DATE(1900,1,7))", 1)]
    [InlineData("=ISOWEEKNUM(DATE(1900,1,8))", 1)]
    public void IsoWeeknum_UsesExcelSerialWeekdays(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=DAYS(DATE(1900,3,1),DATE(1900,1,1))", 60)]
    [InlineData("=DAYS(DATE(1900,3,1),DATE(1900,2,28))", 2)]
    [InlineData("=DAYS360(DATE(1900,1,1),DATE(1900,3,1))", 60)]
    [InlineData("=DAYS360(DATE(1900,2,28),DATE(1900,3,1))", 3)]
    public void DayCountFunctions_UseExcelSerialBoundaries(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=YEARFRAC(DATE(1900,1,1),DATE(1900,3,1),3)", 0.164383561643836)]
    [InlineData("=YEARFRAC(DATE(1900,2,28),DATE(1900,3,1),3)", 0.00547945205479452)]
    public void YearfracActual365_UsesExcelSerialBoundaries(string formula, double expected)
    {
        var result = _eval.Evaluate(formula, Sheet()).Should().BeOfType<NumberValue>().Subject;

        result.Value.Should().BeApproximately(expected, 1e-12);
    }

    private static void AssertColumn(ScalarValue value, params ScalarValue[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.Cells.GetLength(0).Should().Be(expected.Length);
        range.Cells.GetLength(1).Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            range.Cells[row, 0].Should().Be(expected[row]);
    }

    private static Sheet Sheet(params (int Row, int Column, double Value)[] values)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, column, value) in values)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)column), new NumberValue(value));
        return sheet;
    }
}
