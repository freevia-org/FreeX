using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void Sort_ArrayArgumentError_PropagatesError()
    {
        _eval.Evaluate("=SORT(NA())", MakeSheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sort_TreatsScalarArrayAsSingleCellArray()
    {
        var result = _eval.Evaluate("=SORT(5)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(1);
        result.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Sort_SingleColumn_SortsAscending()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(3)), (2,1,new NumberValue(1)), (3,1,new NumberValue(2)));
        var result = _eval.Evaluate("=SORT(A1:A3)", sheet);
        var rv = (RangeValue)result;
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 0].Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Sort_SingleColumn_SortsDescending()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(3)), (2,1,new NumberValue(1)), (3,1,new NumberValue(2)));
        var result = _eval.Evaluate("=SORT(A1:A3,1,-1)", sheet);
        var rv = (RangeValue)result;
        rv.Cells[0, 0].Should().Be(new NumberValue(3));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 0].Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Sort_MultiColumn_SortsBySecondColumn()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("B")), (1,2,new NumberValue(2)),
            (2,1,new TextValue("A")), (2,2,new NumberValue(1)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(3)));
        // SORT(A1:B3, 2, 1) → sort by col 2 ascending
        var result = _eval.Evaluate("=SORT(A1:B3,2,1)", sheet);
        var rv = (RangeValue)result;
        rv.Cells[0, 0].Should().Be(new TextValue("A"));
        rv.Cells[1, 0].Should().Be(new TextValue("B"));
        rv.Cells[2, 0].Should().Be(new TextValue("C"));
    }

    [Fact]
    public void Sort_AcceptsSpilledScalarControlArguments()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("B")), (1,2,new NumberValue(2)),
            (2,1,new TextValue("A")), (2,2,new NumberValue(1)),
            (3,1,new TextValue("C")), (3,2,new NumberValue(3)));

        var rv = _eval.Evaluate("=SORT(A1:B3,SEQUENCE(1,,2),SEQUENCE(1,,-1))", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        rv.Cells[0, 0].Should().Be(new TextValue("C"));
        rv.Cells[1, 0].Should().Be(new TextValue("B"));
        rv.Cells[2, 0].Should().Be(new TextValue("A"));
    }

    [Fact]
    public void Sort_ZeroSortIndex_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (2,1,new NumberValue(2)));
        _eval.Evaluate("=SORT(A1:A2,0)", sheet).Should().Be(ErrorValue.Value,
            "sort_index=0 is invalid (1-based) and must not cause an IndexOutOfRangeException");
    }

    [Fact]
    public void Sort_OutOfBoundsRowSortIndex_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new NumberValue(1)),
            (2,1,new TextValue("B")), (2,2,new NumberValue(2)));

        _eval.Evaluate("=SORT(A1:B2,3)", sheet).Should().Be(ErrorValue.Value,
            "row-oriented SORT sort_index must refer to an existing column");
    }

    [Fact]
    public void Sort_OutOfBoundsColumnSortIndex_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1,1,new TextValue("A")), (1,2,new TextValue("B")),
            (2,1,new NumberValue(1)), (2,2,new NumberValue(2)));

        _eval.Evaluate("=SORT(A1:B2,3,1,TRUE)", sheet).Should().Be(ErrorValue.Value,
            "column-oriented SORT sort_index must refer to an existing row");
    }

    [Fact]
    public void Sort_InvalidSortOrder_ReturnsValueError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(2)), (2,1,new NumberValue(1)));

        _eval.Evaluate("=SORT(A1:A2,1,0)", sheet).Should().Be(ErrorValue.Value,
            "Excel only accepts 1 or -1 for SORT sort_order");
    }

    [Fact] public void Sort_SortIndexError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=SORT(A1:A1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Sort_SortOrderError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=SORT(A1:A1,1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact] public void Sort_ByColError_PropagatesError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)));
        _eval.Evaluate("=SORT(A1:A1,1,1,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Sort_InlineArrayConstant_SortsBySecondColumnAscending()
    {
        // SORT({"b",2;"a",1;"c",3}, 2, 1) must sort rows by col 2 ascending → a/1, b/2, c/3
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=SORT({\"b\",2;\"a\",1;\"c\",3},2,1)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(2);
        // Row 0: "a", 1
        result.Cells[0, 0].Should().Be(new TextValue("a"), "first row col 1 should be 'a' when sorted by col 2 asc");
        result.Cells[0, 1].Should().Be(new NumberValue(1), "first row col 2 should be 1");
        // Row 1: "b", 2
        result.Cells[1, 0].Should().Be(new TextValue("b"), "second row col 1 should be 'b'");
        result.Cells[1, 1].Should().Be(new NumberValue(2), "second row col 2 should be 2");
        // Row 2: "c", 3
        result.Cells[2, 0].Should().Be(new TextValue("c"), "third row col 1 should be 'c'");
        result.Cells[2, 1].Should().Be(new NumberValue(3), "third row col 2 should be 3");
    }

    [Fact]
    public void Sort_InlineArrayConstant_SortsByFirstColumnAscending()
    {
        // SORT({"b",2;"a",1;"c",3}, 1, 1) must sort rows by col 1 ascending → a/1, b/2, c/3
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=SORT({\"b\",2;\"a\",1;\"c\",3},1,1)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(2);
        result.Cells[0, 0].Should().Be(new TextValue("a"), "first row should be 'a' when sorted by col 1 asc");
        result.Cells[1, 0].Should().Be(new TextValue("b"));
        result.Cells[2, 0].Should().Be(new TextValue("c"));
    }
}
