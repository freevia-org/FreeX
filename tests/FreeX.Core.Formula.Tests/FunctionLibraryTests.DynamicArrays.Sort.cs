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
    public void Sort_ArraySortIndexAndOrder_PerformsMultiKeySort()
    {
        // SORT(arr, {1,2}, {1,-1}) — multi-key: primary key column 1 ascending, then column 2 descending.
        // Rows (col1,col2,col3): the three col1=10 rows must be ordered by col2 DESC, so the
        // (10,100.1,100.2) row comes first. Top-left of the result is 10.
        var sheet = MakeSheet();
        var result = _eval.Evaluate(
                "=SORT({10,10.1,10.2;12,12.1,12.2;11,11.1,11.2;13,13.1,13.2;10,100.1,100.2;12,120.1,120.2;10,10.1,10.2},{1,2},{1,-1})",
                sheet)
            .Should().BeOfType<RangeValue>("multi-key SORT with array sort_index/sort_order must not error").Subject;

        result.RowCount.Should().Be(7);
        result.ColCount.Should().Be(3);
        result.Cells[0, 0].Should().Be(new NumberValue(10), "primary key 10 sorts first");
        result.Cells[0, 1].Should().Be(new NumberValue(100.1), "secondary key descending puts 100.1 before 10.1");
        result.Cells[0, 2].Should().Be(new NumberValue(100.2));
    }

    [Fact]
    public void Sort_ArraySortIndex_DefaultsOrderToAscending()
    {
        // sort_order omitted but sort_index is an array: each key defaults to ascending (1).
        var sheet = MakeSheet();
        var result = _eval.Evaluate("=SORT({\"b\",2;\"a\",2;\"a\",1},{1,2})", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        // sort by col1 asc then col2 asc → a/1, a/2, b/2
        result.Cells[0, 0].Should().Be(new TextValue("a"));
        result.Cells[0, 1].Should().Be(new NumberValue(1));
        result.Cells[1, 0].Should().Be(new TextValue("a"));
        result.Cells[1, 1].Should().Be(new NumberValue(2));
        result.Cells[2, 0].Should().Be(new TextValue("b"));
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
