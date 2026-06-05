using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// TEXT() must format through the same Excel number-format engine the grid uses, not a naive .NET ToString
// that renders Excel's '?' digit placeholder as a literal character. Regression for the fidelity-batch
// finding (NumberFormatTests): TEXT(1234567,"?,???") emitted "????" instead of "1,234,567".
public class TextFormatCodeTests
{
    private readonly FormulaEvaluator _evaluator = new();

    private ScalarValue Eval(string formula)
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        return _evaluator.Evaluate(formula, sheet, wb);
    }

    [Theory]
    [InlineData("=TEXT(1234567,\"?,???\")", "1,234,567")]
    [InlineData("=TEXT(1234567,\"?,????\")", "1,234,567")]
    [InlineData("=TEXT(12,\"?,?\")", "12")]
    [InlineData("=TEXT(123,\"?,??\")", "123")]
    public void Text_QuestionPlaceholderWithGrouping(string formula, string expected) =>
        Eval(formula).Should().Be(new TextValue(expected));

    [Theory]
    [InlineData("=TEXT(1234567,\"#,##0\")", "1,234,567")]
    [InlineData("=TEXT(1234.5,\"0.00\")", "1234.50")]
    [InlineData("=TEXT(0.25,\"0%\")", "25%")]
    public void Text_CommonFormats_StillWork(string formula, string expected) =>
        Eval(formula).Should().Be(new TextValue(expected));
}
