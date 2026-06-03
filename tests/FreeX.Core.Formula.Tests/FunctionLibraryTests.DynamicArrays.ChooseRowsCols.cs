using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void Chooserows_ReordersRowsAndAllowsRepeats()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(1)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(2)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(3)));

        var result = _eval.Evaluate("=CHOOSEROWS(A1:B3,3,1,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[1, 0].Should().Be(new TextValue("A"));
        rv.Cells[2, 0].Should().Be(new TextValue("C"));
    }

    [Fact]
    public void Chooserows_NegativeIndexSelectsFromEnd()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")),
            (2,1,new TextValue("B")),
            (3,1,new TextValue("C")));

        var result = _eval.Evaluate("=CHOOSEROWS(A1:A3,-1,-3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[1, 0].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Chooserows_AcceptsDynamicArrayRowIndexes()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(1)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(2)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(3)));

        var result = _eval.Evaluate("=CHOOSEROWS(A1:B3,VSTACK(3,1))", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[0, 1].Should().Be(new NumberValue(3));
        rv.Cells[1, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 1].Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Choosecols_ReordersColumnsAndAllowsRepeats()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")), (1,3,new TextValue("C")),
            (2,1,new NumberValue(1)), (2,2,new NumberValue(2)), (2,3,new NumberValue(3)));

        var result = _eval.Evaluate("=CHOOSECOLS(A1:C2,3,1,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[0, 1].Should().Be(new TextValue("A"));
        rv.Cells[0, 2].Should().Be(new TextValue("C"));
    }

    [Fact]
    public void Choosecols_NegativeIndexSelectsFromEnd()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")), (1,3,new TextValue("C")));

        var result = _eval.Evaluate("=CHOOSECOLS(A1:C1,-1,-3)", sheet);

        var rv = (RangeValue)result;
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[0, 1].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Choosecols_AcceptsDynamicArrayColumnIndexes()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")), (1,3,new TextValue("C")),
            (2,1,new NumberValue(1)), (2,2,new NumberValue(2)), (2,3,new NumberValue(3)));

        var result = _eval.Evaluate("=CHOOSECOLS(A1:C2,HSTACK(1,3))", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[0, 1].Should().Be(new TextValue("C"));
        rv.Cells[1, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 1].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void ChooserowsAndChoosecols_TreatScalarArrayAsSingleCellArray()
    {
        var rows = _eval.Evaluate("=CHOOSEROWS(5,1)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        rows.RowCount.Should().Be(1);
        rows.ColCount.Should().Be(1);
        rows.Cells[0, 0].Should().Be(new NumberValue(5));

        var cols = _eval.Evaluate("=CHOOSECOLS(\"x\",1)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        cols.RowCount.Should().Be(1);
        cols.ColCount.Should().Be(1);
        cols.Cells[0, 0].Should().Be(new TextValue("x"));
    }

    [Fact]
    public void Chooserows_ZeroIndex_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=CHOOSEROWS(A1:A1,0)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Choosecols_OutOfRangeIndex_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=CHOOSECOLS(A1:A1,2)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void ChooserowsAndChoosecols_HugeFiniteIndex_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new TextValue("A")), (2,1,new TextValue("B")));

        _eval.Evaluate("=CHOOSEROWS(A1:A2,2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CHOOSEROWS(A1:A2,-2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CHOOSEROWS(A1:A2,-2147483649)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CHOOSECOLS(A1:A2,2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CHOOSECOLS(A1:A2,-2147483648)", sheet).Should().Be(ErrorValue.Value);
        _eval.Evaluate("=CHOOSECOLS(A1:A2,-2147483649)", sheet).Should().Be(ErrorValue.Value);
    }
}
