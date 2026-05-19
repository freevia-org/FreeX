using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

public sealed class ExcelParityLogicalTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData("=AND(TRUE,1)", true)]
    [InlineData("=AND(TRUE,0)", false)]
    [InlineData("=FALSE()", false)]
    [InlineData("=IF(TRUE,TRUE,FALSE)", true)]
    [InlineData("=IFS(FALSE,FALSE,TRUE,TRUE)", true)]
    [InlineData("=NOT(FALSE)", true)]
    [InlineData("=OR(FALSE,0,1)", true)]
    [InlineData("=SWITCH(2,1,FALSE,2,TRUE,FALSE)", true)]
    [InlineData("=TRUE()", true)]
    [InlineData("=XOR(TRUE,FALSE,TRUE)", false)]
    public void LogicalFunctions_MatchExcelBooleanResults(string formula, bool expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new BoolValue(expected));
    }

    [Fact]
    public void If_ShortCircuitsUnselectedBranch()
    {
        _eval.Evaluate("=IF(TRUE,1,1/0)", Sheet()).Should().Be(new NumberValue(1));
        _eval.Evaluate("=IF(FALSE,1/0,2)", Sheet()).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void IfError_CatchesAllExcelErrorValues()
    {
        _eval.Evaluate("=IFERROR(1/0,\"fallback\")", Sheet()).Should().Be(new TextValue("fallback"));
        _eval.Evaluate("=IFERROR(NA(),\"fallback\")", Sheet()).Should().Be(new TextValue("fallback"));
        _eval.Evaluate("=IFERROR(42,\"fallback\")", Sheet()).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void IfNa_CatchesOnlyNa()
    {
        _eval.Evaluate("=IFNA(NA(),\"fallback\")", Sheet()).Should().Be(new TextValue("fallback"));
        _eval.Evaluate("=IFNA(1/0,\"fallback\")", Sheet()).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void AndOr_PropagateErrorsWhenNoResultDeterminesOutcomeFirst()
    {
        _eval.Evaluate("=AND(TRUE,NA())", Sheet()).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=OR(FALSE,NA())", Sheet()).Should().Be(ErrorValue.NA);
    }

    private static Sheet Sheet() => new(SheetId.New(), "S");
}
