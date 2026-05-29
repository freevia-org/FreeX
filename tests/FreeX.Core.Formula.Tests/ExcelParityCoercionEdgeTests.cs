using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public sealed class ExcelParityCoercionEdgeTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData("=ABS(\"-2.5\")", 2.5)]
    [InlineData("=SUM(\"2\",\"3\")", 5)]
    [InlineData("=PRODUCT(\"2\",\"3\")", 6)]
    [InlineData("=AVERAGE(\"2\",\"4\")", 3)]
    [InlineData("=MIN(\"2\",4)", 2)]
    [InlineData("=MAX(\"2\",4)", 4)]
    [InlineData("=COUNT(\"2\",4)", 2)]
    public void LiteralNumericText_IsCoercedByScalarNumericArguments(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("=\"50%\"+0", 0.5)]
    [InlineData("=\"50%%\"+0", 0.005)]
    [InlineData("=0+\"$1,234.50\"", 1234.5)]
    [InlineData("=\"1/2/2024\"+1", 45294)]
    [InlineData("=\"1:30 PM\"*24", 13.5)]
    [InlineData("=\"2/29/1900\"+0.25", 60.25)]
    [InlineData("=-\"50%\"", -0.5)]
    [InlineData("=SUM(\"50%\",\"$1,234.50\")", 1235.0)]
    [InlineData("=ABS(\"50%\")", 0.5)]
    public void RichNumericText_IsCoercedByScalarMathAndOperators(string formula, double expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void Value_ParsesMultipleTrailingPercentSignsLikeExcel()
    {
        _eval.Evaluate("=VALUE(\"50%%\")", Sheet()).Should().Be(new NumberValue(0.005));
    }

    [Fact]
    public void ReferencedRichNumericText_IsCoercedByArithmeticOperators()
    {
        var sheet = Sheet(
            (1, 1, new TextValue("50%")),
            (2, 1, new TextValue("$1,234.50")),
            (3, 1, new TextValue("1/2/2024")),
            (4, 1, new TextValue("1:30 PM")));

        _eval.Evaluate("=A1+0", sheet).Should().Be(new NumberValue(0.5));
        _eval.Evaluate("=A2+0", sheet).Should().Be(new NumberValue(1234.5));
        _eval.Evaluate("=A3+1", sheet).Should().Be(new NumberValue(45294));
        _eval.Evaluate("=A4*24", sheet).Should().Be(new NumberValue(13.5));
    }

    [Fact]
    public void ReferencedNumericText_IsIgnoredByAggregateRanges()
    {
        var sheet = Sheet(
            (1, 1, new TextValue("2")),
            (2, 1, new NumberValue(3)),
            (3, 1, new BoolValue(true)));

        _eval.Evaluate("=SUM(A1:A3)", sheet).Should().Be(new NumberValue(3));
        _eval.Evaluate("=AVERAGE(A1:A3)", sheet).Should().Be(new NumberValue(3));
        _eval.Evaluate("=NPV(0,A3)", sheet).Should().Be(new NumberValue(0));
        _eval.Evaluate("=COUNT(A1:A3)", sheet).Should().Be(new NumberValue(1));
        _eval.Evaluate("=COUNTA(A1:A3)", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void SingleDirectRangeFastAggregates_PreserveDateAndBooleanReferenceRules()
    {
        var date = DateTimeValue.FromDateTime(new DateTime(2026, 5, 17));
        var sheet = Sheet(
            (1, 1, date),
            (2, 1, new BoolValue(true)),
            (3, 1, BlankValue.Instance));

        _eval.Evaluate("=SUM(A1:A3)", sheet).Should().Be(new NumberValue(date.Value));
        _eval.Evaluate("=AVERAGE(A1:A3)", sheet).Should().Be(new NumberValue(date.Value));
        _eval.Evaluate("=MIN(A1:A3)", sheet).Should().Be(new NumberValue(date.Value));
        _eval.Evaluate("=MAX(A1:A3)", sheet).Should().Be(new NumberValue(date.Value));
        _eval.Evaluate("=COUNT(A1:A3)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void AggregateFastPath_DoesNotChangeMultiArgumentOrArraySemantics()
    {
        var sheet = Sheet(
            (1, 1, new TextValue("2")),
            (2, 1, new NumberValue(3)));

        _eval.Evaluate("=SUM(A1:A2,\"4\")", sheet).Should().Be(new NumberValue(7));
        _eval.Evaluate("=COUNT(A1:A2,\"4\")", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=SUM(SEQUENCE(2,1))", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void SingleDirectRangeFastAggregates_PreserveAllIgnoredRangeRules()
    {
        var sheet = Sheet(
            (1, 1, new TextValue("2")),
            (2, 1, new BoolValue(true)));

        _eval.Evaluate("=SUM(A1:A3)", sheet).Should().Be(new NumberValue(0));
        _eval.Evaluate("=AVERAGE(A1:A3)", sheet).Should().Be(ErrorValue.DivByZero);
        _eval.Evaluate("=MIN(A1:A3)", sheet).Should().Be(new NumberValue(0));
        _eval.Evaluate("=MAX(A1:A3)", sheet).Should().Be(new NumberValue(0));
        _eval.Evaluate("=COUNT(A1:A3)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void EmptyTextAndBlankCells_FollowExcelCountingRules()
    {
        var sheet = Sheet((1, 1, new TextValue("")));

        _eval.Evaluate("=COUNTBLANK(A1:A2)", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=COUNTA(A1:A2)", sheet).Should().Be(new NumberValue(1));
        _eval.Evaluate("=COUNT(A1:A2)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void AggregateRanges_HandleErrorsByFunctionSemantics()
    {
        var sheet = Sheet(
            (1, 1, new NumberValue(1)),
            (2, 1, ErrorValue.NA));

        _eval.Evaluate("=SUM(A1:A2)", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=AVERAGE(A1:A2)", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=COUNT(A2)", sheet).Should().Be(new NumberValue(0));
        _eval.Evaluate("=COUNT(A1:A2)", sheet).Should().Be(new NumberValue(1));
        _eval.Evaluate("=MIN(A1:A2)", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=MAX(A1:A2)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void LazySelectionFunctions_DoNotEvaluateUnselectedErrorBranches()
    {
        var sheet = Sheet();

        _eval.Evaluate("=IF(FALSE,1/0,42)", sheet).Should().Be(new NumberValue(42));
        _eval.Evaluate("=CHOOSE(2,1/0,42)", sheet).Should().Be(new NumberValue(42));
        _eval.Evaluate("=IFS(FALSE,1/0,TRUE,42)", sheet).Should().Be(new NumberValue(42));
        _eval.Evaluate("=SWITCH(2,1,1/0,2,42)", sheet).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void DynamicArrayArithmetic_PreservesCellLevelErrorPrecedence()
    {
        var sheet = Sheet(
            (1, 1, new NumberValue(1)),
            (1, 2, ErrorValue.NA),
            (2, 1, ErrorValue.DivByZero),
            (2, 2, new NumberValue(4)));

        var result = _eval.Evaluate("=A1:B2+10", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.At(1, 1).Should().Be(new NumberValue(11));
        result.At(1, 2).Should().Be(ErrorValue.NA);
        result.At(2, 1).Should().Be(ErrorValue.DivByZero);
        result.At(2, 2).Should().Be(new NumberValue(14));
    }

    [Theory]
    [InlineData("=XLOOKUP(\"B\",A1:A2,B1:B2)")]
    [InlineData("=XMATCH(\"B\",A1:A2)")]
    public void ModernLookupFunctions_PropagateLookupArrayErrorsBeforeLaterMatches(string formula)
    {
        var sheet = Sheet(
            (1, 1, ErrorValue.DivByZero),
            (2, 1, new TextValue("B")),
            (1, 2, new NumberValue(10)),
            (2, 2, new NumberValue(20)));

        _eval.Evaluate(formula, sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Theory]
    [InlineData("=XLOOKUP(5,A1:A3,B1:B3,\"\",-1)")]
    [InlineData("=XMATCH(5,A1:A3,-1)")]
    public void ModernApproximateLookupFunctions_PropagateLookupArrayErrorsBeforeFallbackMatches(string formula)
    {
        var sheet = Sheet(
            (1, 1, ErrorValue.DivByZero),
            (2, 1, new NumberValue(4)),
            (3, 1, new NumberValue(6)),
            (1, 2, new TextValue("error row")),
            (2, 2, new TextValue("smaller")),
            (3, 2, new TextValue("larger")));

        _eval.Evaluate(formula, sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void VolatileDynamicArrays_ReturnBoundedValuesOnEachEvaluation()
    {
        var sheet = Sheet();

        for (var i = 0; i < 10; i++)
        {
            var result = _eval.Evaluate("=RANDARRAY(2,2,5,7)", sheet)
                .Should().BeOfType<RangeValue>()
                .Subject;

            result.RowCount.Should().Be(2);
            result.ColCount.Should().Be(2);
            foreach (var value in result.Flatten())
                value.Should().BeOfType<NumberValue>().Subject.Value.Should().BeInRange(5, 7);
        }
    }

    private static Sheet Sheet(params (int Row, int Col, ScalarValue Value)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in cells)
        {
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), value);
        }

        return sheet;
    }
}
