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
    [InlineData("=DATE(1900,3,1)", 61)]
    [InlineData("=DATE(2024,1,15)", 45306)]
    public void Date_ReturnsExcelSerialNumbers(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=YEAR(1)", 1900)]
    [InlineData("=MONTH(1)", 1)]
    [InlineData("=DAY(1)", 1)]
    [InlineData("=WEEKDAY(1)", 1)]
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
    [InlineData("=DATEVALUE(\"1/1/1900\")", 1)]
    [InlineData("=DATEVALUE(\"2/28/1900\")", 59)]
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

    private static Sheet Sheet() => new(SheetId.New(), "S");
}
