using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void Vstack_AppendsRowsAndPadsShorterArraysWithNA()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")),
            (2,1,new TextValue("C")), (2,2,new TextValue("D")),
            (1,3,new TextValue("E")));

        var result = _eval.Evaluate("=VSTACK(A1:B2,C1:C1)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 1].Should().Be(new TextValue("D"));
        rv.Cells[2, 0].Should().Be(new TextValue("E"));
        rv.Cells[2, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Hstack_AppendsColumnsAndPadsShorterArraysWithNA()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (2,1,new TextValue("B")),
            (1,2,new TextValue("C")));

        var result = _eval.Evaluate("=HSTACK(A1:A2,B1:B1)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 0].Should().Be(new TextValue("B"));
        rv.Cells[0, 1].Should().Be(new TextValue("C"));
        rv.Cells[1, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void VstackAndHstack_TreatScalarArgumentsAsSingleCellArrays()
    {
        var vstack = _eval.Evaluate("=VSTACK(1,\"two\",TRUE)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        vstack.RowCount.Should().Be(3);
        vstack.ColCount.Should().Be(1);
        vstack.Cells[0, 0].Should().Be(new NumberValue(1));
        vstack.Cells[1, 0].Should().Be(new TextValue("two"));
        vstack.Cells[2, 0].Should().Be(new BoolValue(true));

        var hstack = _eval.Evaluate("=HSTACK(1,\"two\",TRUE)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        hstack.RowCount.Should().Be(1);
        hstack.ColCount.Should().Be(3);
        hstack.Cells[0, 0].Should().Be(new NumberValue(1));
        hstack.Cells[0, 1].Should().Be(new TextValue("two"));
        hstack.Cells[0, 2].Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Vstack_ScalarErrorArgument_SpillsErrorAsCell()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        var result = _eval.Evaluate("=VSTACK(A1:A1,NA())", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[1, 0].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Hstack_ScalarErrorArgument_SpillsErrorAsCell()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        var result = _eval.Evaluate("=HSTACK(A1:A1,NA())", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(2);
        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[0, 1].Should().Be(ErrorValue.NA);
    }
}
