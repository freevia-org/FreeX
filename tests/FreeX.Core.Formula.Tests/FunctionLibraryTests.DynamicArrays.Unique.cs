using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void Unique_SingleColumn_RemovesDuplicates()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (2,1,new NumberValue(2)),
            (3,1,new NumberValue(1)), (4,1,new NumberValue(3)));
        var result = _eval.Evaluate("=UNIQUE(A1:A4)", sheet);
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Unique_TreatsScalarArrayAsSingleCellArray()
    {
        var result = _eval.Evaluate("=UNIQUE(5)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Unique_ExactlyOnce_ReturnsOnlySingletons()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (2,1,new NumberValue(2)),
            (3,1,new NumberValue(1)), (4,1,new NumberValue(3)));
        // UNIQUE(A1:A4, FALSE, TRUE) → only values appearing exactly once
        var result = _eval.Evaluate("=UNIQUE(A1:A4,FALSE,TRUE)", sheet);
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(2));
        rv.Cells[1, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Unique_ExactlyOnceWithNoSingletons_ReturnsCalcError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(1)),
            (3, 1, new NumberValue(2)), (4, 1, new NumberValue(2)));

        _eval.Evaluate("=UNIQUE(A1:A4,FALSE,TRUE)", sheet)
            .Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void Unique_MultiColumn_DeduplicatesRows()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(1)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(2)),
            (3,1,new TextValue("A")), (3,2,new NumberValue(1)));
        var result = _eval.Evaluate("=UNIQUE(A1:B3)", sheet);
        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
    }

    [Fact]
    public void Unique_DistinguishesScalarTypesWhenDeduplicating()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new TextValue("1")),
            (3, 1, new BoolValue(true)),
            (4, 1, new TextValue("TRUE")),
            (5, 1, new NumberValue(1)));

        var result = _eval.Evaluate("=UNIQUE(A1:A5)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(4);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new TextValue("1"));
        rv.Cells[2, 0].Should().Be(new BoolValue(true));
        rv.Cells[3, 0].Should().Be(new TextValue("TRUE"));
    }

    [Fact] public void Unique_ByColError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=UNIQUE(A1:A1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Unique_ArrayArgumentError_PropagatesError()
    {
        _eval.Evaluate("=UNIQUE(NA())", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Unique_ExactlyOnceError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=UNIQUE(A1:A1,FALSE,NA())", sheet).Should().Be(ErrorValue.NA);
    }
}
