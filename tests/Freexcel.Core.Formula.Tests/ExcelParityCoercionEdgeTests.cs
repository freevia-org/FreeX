using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

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
