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

    private static Sheet Sheet() => new(SheetId.New(), "S");
}
