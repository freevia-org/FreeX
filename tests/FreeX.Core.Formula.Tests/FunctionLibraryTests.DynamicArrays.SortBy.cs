using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void Sortby_SortsRowsBySeparateKeyArray()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(3)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(1)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(2)));

        var result = _eval.Evaluate("=SORTBY(A1:A3,B1:B3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(1);
        rv.Cells[0, 0].Should().Be(new TextValue("B"));
        rv.Cells[1, 0].Should().Be(new TextValue("C"));
        rv.Cells[2, 0].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Sortby_OmittedSortOrder_DefaultsAscending()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(3)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(1)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(2)));

        var result = _eval.Evaluate("=SORTBY(A1:A3,B1:B3,)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.Cells[0, 0].Should().Be(new TextValue("B"));
        rv.Cells[1, 0].Should().Be(new TextValue("C"));
        rv.Cells[2, 0].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Sortby_TreatsScalarArrayAndKeyAsSingleCellArrays()
    {
        var result = _eval.Evaluate("=SORTBY(5,1)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Sortby_SortsColumnsBySeparateKeyArrayDescending()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")), (1,3,new TextValue("C")),
            (2,1,new NumberValue(1)), (2,2,new NumberValue(3)), (2,3,new NumberValue(2)));

        var result = _eval.Evaluate("=SORTBY(A1:C1,A2:C2,-1)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(1);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new TextValue("B"));
        rv.Cells[0, 1].Should().Be(new TextValue("C"));
        rv.Cells[0, 2].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Sortby_AcceptsSpilledScalarSortOrder()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(3)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(1)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(2)));

        var rv = _eval.Evaluate("=SORTBY(A1:A3,B1:B3,SEQUENCE(1,,-1))", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 0].Should().Be(new TextValue("C"));
        rv.Cells[2, 0].Should().Be(new TextValue("B"));
    }

    [Fact]
    public void Sortby_SortOrderError_PropagatesError()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(2)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(1)));

        _eval.Evaluate("=SORTBY(A1:A2,B1:B2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sortby_RangeInSortOrderSlot_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(1)), (2,3,new NumberValue(4)));

        _eval.Evaluate("=SORTBY(A1:A2,B1:B2,C1:C2)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Sortby_MismatchedKeyShape_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (2,1,new TextValue("B")),
            (1,2,new NumberValue(1)));

        _eval.Evaluate("=SORTBY(A1:A2,B1:C1)", sheet).Should().Be(ErrorValue.Value,
            "SORTBY key arrays must align to either the sorted rows or sorted columns");
    }
}
