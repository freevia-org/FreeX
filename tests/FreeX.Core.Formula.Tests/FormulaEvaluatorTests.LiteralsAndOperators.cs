using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FormulaEvaluatorTests
{
    // ── Literal values ──

    [Fact]
    public void Number_Literal()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=42", sheet).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Decimal_Literal()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=3.14", sheet).Should().Be(new NumberValue(3.14));
    }

    [Fact]
    public void String_Literal()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=\"hello\"", sheet).Should().Be(new TextValue("hello"));
    }

    [Fact]
    public void Boolean_True()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=TRUE", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Boolean_False()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=FALSE", sheet).Should().Be(new BoolValue(false));
    }

    // ── Arithmetic operators ──

    [Fact]
    public void Addition()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=1+2", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Subtraction()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=10-3", sheet).Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Multiplication()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=4*5", sheet).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Division()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=10/4", sheet).Should().Be(new NumberValue(2.5));
    }

    [Fact]
    public void DivisionByZero_ReturnsError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=1/0", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Power()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=2^10", sheet).Should().Be(new NumberValue(1024));
    }

    [Theory]
    [InlineData("=0^0", "#NUM!")]
    [InlineData("=0^(-1)", "#DIV/0!")]
    public void PowerOperator_ZeroBaseInvalidExponents_ReturnsExcelErrors(string formula, string errorCode)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate(formula, sheet).Should().Be(new ErrorValue(errorCode));
    }

    // ── Operator precedence ──

    [Fact]
    public void Precedence_MultiplyBeforeAdd()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=1+2*3", sheet).Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Precedence_ParensOverride()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=(1+2)*3", sheet).Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Precedence_PowerBeforeMultiply()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=2*3^2", sheet).Should().Be(new NumberValue(18));
    }

    [Fact]
    public void Precedence_UnaryNegation()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=-5+3", sheet).Should().Be(new NumberValue(-2));
    }

    [Fact]
    public void Precedence_UnaryNegationBeforePower()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=-2^2", sheet).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Precedence_BinarySubtractionAfterPower()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=0-2^2", sheet).Should().Be(new NumberValue(-4));
    }

    [Fact]
    public void Precedence_ParenthesesOverrideUnaryPower()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=(-2)^2", sheet).Should().Be(new NumberValue(4));
    }

    // ── String concatenation ──

    [Fact]
    public void Concatenation()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=\"Hello\" & \" \" & \"World\"", sheet)
            .Should().Be(new TextValue("Hello World"));
    }

    // ── Comparison operators ──

    [Fact]
    public void Comparison_Equal_True()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=1=1", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Comparison_Equal_False()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=1=2", sheet).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void Comparison_LessThan()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=1<2", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Comparison_GreaterThan()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=5>3", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Comparison_NotEqual()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=1<>2", sheet).Should().Be(new BoolValue(true));
    }

    // ── Percent operator ──

    [Fact]
    public void Percent_DividesByHundred()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=50%", sheet).Should().Be(new NumberValue(0.5));
    }
}
