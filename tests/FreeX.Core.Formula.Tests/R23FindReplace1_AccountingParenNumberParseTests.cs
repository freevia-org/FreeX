using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for R23-find-replace-1: ExcelTextNumberParser.TryParse rejected a
/// parenthesized, thousands-grouped negative number (e.g. accounting-format "(9,999.99)")
/// because ValidGroupingRegex had no parenthesis support, forcing rejectedNumericComma=true
/// and short-circuiting before the lenient NumberStyles.Any (AllowParentheses|AllowThousands)
/// parse that would otherwise succeed. This silently turned a Find&amp;Replace-driven reparse of an
/// accounting-formatted NumberValue into a literal TextValue.
/// </summary>
public sealed class R23FindReplace1_AccountingParenNumberParseTests
{
    private readonly FormulaEvaluator _eval = new();

    private ScalarValue Eval(string formula)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        return _eval.Evaluate(formula, sheet);
    }

    [Theory]
    [InlineData("(9,999.99)", -9999.99)]
    [InlineData("($1,234.50)", -1234.50)]
    [InlineData("(1,234,567.5)", -1234567.5)]
    [InlineData("(999.99)", -999.99)] // non-grouped parenthesized negative already worked before the fix
    public void ParenthesizedGroupedNegative_ParsesToNumber(string text, double expected)
    {
        ExcelTextNumberParser.TryParse(text, out var number).Should().BeTrue();
        number.Should().Be(expected);
    }

    [Theory]
    [InlineData("(1,2)")] // bad grouping inside parens must still be rejected
    [InlineData("(12,34)")]
    [InlineData("(9,999.99")] // unmatched opening paren
    [InlineData("9,999.99)")] // unmatched closing paren
    public void InvalidGroupedOrUnmatchedParens_StillRejected(string text)
    {
        ExcelTextNumberParser.TryParse(text, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("=\"(9,999.99)\"+0", -9999.99)]
    [InlineData("=0+\"($1,234.50)\"", -1234.50)]
    public void FindReplaceStyleReparse_AccountingNegative_CoercesToNumber(string formula, double expected) =>
        Eval(formula).Should().Be(new NumberValue(expected));

    [Fact]
    public void ValueFunction_ParenthesizedGroupedNegative_CoercesToNumber() =>
        Eval("=VALUE(\"(9,999.99)\")").Should().Be(new NumberValue(-9999.99));

    [Fact]
    public void BadGroupingInsideParens_ValueFunction_IsValueError() =>
        Eval("=VALUE(\"(1,2)\")").Should().Be(ErrorValue.Value);
}
