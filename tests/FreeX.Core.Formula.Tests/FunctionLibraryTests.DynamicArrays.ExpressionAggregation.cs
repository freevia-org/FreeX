using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void Sum_FlattensSequenceDynamicArrayResult()
    {
        _eval.Evaluate("=SUM(SEQUENCE(3,2,1,1))", MakeSheet())
            .Should().Be(new NumberValue(21));
    }

    [Fact]
    public void Sumproduct_AcceptsArrayArithmeticExpression()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(10)),
            (2, 1, new NumberValue(2)), (2, 2, new NumberValue(20)),
            (3, 1, new NumberValue(3)), (3, 2, new NumberValue(30)));

        _eval.Evaluate("=SUMPRODUCT(A1:A3+1,B1:B3)", sheet).Should().Be(new NumberValue(200));
    }

    [Fact]
    public void Aggregate_FlattensDynamicArrayArithmeticResult()
    {
        _eval.Evaluate("=SUM(SEQUENCE(2,2)*2)", MakeSheet()).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Aggregate_FlattensDynamicArrayUnaryMinusResult()
    {
        _eval.Evaluate("=SUM(-SEQUENCE(2,2))", MakeSheet()).Should().Be(new NumberValue(-10));
    }

    [Fact]
    public void Aggregate_FlattensDynamicArrayPercentResult()
    {
        _eval.Evaluate("=SUM(SEQUENCE(2,2)%)", MakeSheet()).Should().Be(new NumberValue(0.1));
    }

    [Fact]
    public void DynamicArrayBinaryExpression_BroadcastsRowAndColumnVectors()
    {
        _eval.Evaluate("=SUM(SEQUENCE(3,1)+SEQUENCE(1,3))", MakeSheet()).Should().Be(new NumberValue(36));
    }

    [Fact]
    public void Sum_FlattensFilterDynamicArrayResult()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(3)), (1, 2, new BoolValue(true)),
            (2, 1, new NumberValue(1)), (2, 2, new BoolValue(false)),
            (3, 1, new NumberValue(2)), (3, 2, new BoolValue(true)));

        _eval.Evaluate("=SUM(FILTER(A1:A3,B1:B3))", sheet)
            .Should().Be(new NumberValue(5));
    }
}
