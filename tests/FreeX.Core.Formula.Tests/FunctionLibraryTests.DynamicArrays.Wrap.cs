using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void Wraprows_WrapsRowVectorAndPadsWithNA()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (1,4,new NumberValue(4)), (1,5,new NumberValue(5)));

        var result = _eval.Evaluate("=WRAPROWS(A1:E1,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 2].Should().Be(new NumberValue(3));
        rv.Cells[1, 0].Should().Be(new NumberValue(4));
        rv.Cells[1, 1].Should().Be(new NumberValue(5));
        rv.Cells[1, 2].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Wraprows_UsesCustomPadValue()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)));

        var result = _eval.Evaluate("=WRAPROWS(A1:C1,2,\"x\")", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[1, 0].Should().Be(new NumberValue(3));
        rv.Cells[1, 1].Should().Be(new TextValue("x"));
    }

    [Fact]
    public void WraprowsAndWrapcols_PadWithOneCellRange_UsesScalarValue()
    {
        var rowSheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (1, 2, new NumberValue(2)),
            (1, 3, new NumberValue(3)),
            (1, 4, new NumberValue(9)));
        var rows = _eval.Evaluate("=WRAPROWS(A1:C1,2,D1:D1)", rowSheet)
            .Should().BeOfType<RangeValue>().Subject;

        rows.RowCount.Should().Be(2);
        rows.ColCount.Should().Be(2);
        rows.Cells[1, 0].Should().Be(new NumberValue(3));
        rows.Cells[1, 1].Should().Be(new NumberValue(9));

        var colSheet = MakeSheet(
            (1, 1, new TextValue("a")),
            (1, 2, new TextValue("b")),
            (1, 3, new TextValue("c")),
            (1, 4, new TextValue("z")));
        var cols = _eval.Evaluate("=WRAPCOLS(A1:C1,2,D1:D1)", colSheet)
            .Should().BeOfType<RangeValue>().Subject;

        cols.RowCount.Should().Be(2);
        cols.ColCount.Should().Be(2);
        cols.Cells[0, 1].Should().Be(new TextValue("c"));
        cols.Cells[1, 1].Should().Be(new TextValue("z"));
    }

    [Fact]
    public void WraprowsAndWrapcols_OmittedPadWith_DefaultsToNA()
    {
        var rowSheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)));
        var rows = _eval.Evaluate("=WRAPROWS(A1:C1,2,)", rowSheet)
            .Should().BeOfType<RangeValue>().Subject;

        rows.Cells[1, 1].Should().Be(ErrorValue.NA);

        var colSheet = MakeSheet(
            (1,1,new NumberValue(1)), (2,1,new NumberValue(2)), (3,1,new NumberValue(3)));
        var cols = _eval.Evaluate("=WRAPCOLS(A1:A3,2,)", colSheet)
            .Should().BeOfType<RangeValue>().Subject;

        cols.Cells[1, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Wrapcols_WrapsColumnVectorByColumns()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)),
            (2,1,new NumberValue(2)),
            (3,1,new NumberValue(3)),
            (4,1,new NumberValue(4)),
            (5,1,new NumberValue(5)));

        var result = _eval.Evaluate("=WRAPCOLS(A1:A5,3)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[2, 0].Should().Be(new NumberValue(3));
        rv.Cells[0, 1].Should().Be(new NumberValue(4));
        rv.Cells[1, 1].Should().Be(new NumberValue(5));
        rv.Cells[2, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void WraprowsAndWrapcols_TreatScalarArgumentAsOneItemVector()
    {
        // A wrap_count larger than the item count is one full row/column of exactly the items - no padding
        // (WRAPROWS(5,2) is just 5 in Excel, measured through COM).
        var one = _eval.Evaluate("=WRAPROWS(1,2)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        (one.RowCount, one.ColCount).Should().Be((1, 1));
        one.Cells[0, 0].Should().Be(new NumberValue(1));
        var text = _eval.Evaluate("=WRAPCOLS(\"x\",2)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        (text.RowCount, text.ColCount).Should().Be((1, 1));
        text.Cells[0, 0].Should().Be(new TextValue("x"));

        var padded = _eval.Evaluate("=WRAPROWS({1,2,3},2)", MakeSheet()).Should().BeOfType<RangeValue>().Subject;
        padded.RowCount.Should().Be(2);
        padded.ColCount.Should().Be(2);
        padded.Cells[1, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Wraprows_InvalidWrapCount_ReturnsNumError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=WRAPROWS(A1:A1,0)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void WraprowsAndWrapcols_WrapCountError_PropagatesBeforeArrayShapeValidation()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)),
            (2,1,new NumberValue(3)), (2,2,new NumberValue(4)));

        _eval.Evaluate("=WRAPROWS(A1:B2,NA())", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=WRAPCOLS(A1:B2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void WraprowsAndWrapcols_HugeFiniteWrapCount_ReturnsNumError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=WRAPROWS(A1:A1,2147483648)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=WRAPROWS(A1:A1,-2147483648)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=WRAPCOLS(A1:A1,2147483648)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=WRAPCOLS(A1:A1,-2147483648)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Wrapcols_TwoDimensionalArray_ReturnsValueError()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)),
            (2,1,new NumberValue(3)), (2,2,new NumberValue(4)));

        _eval.Evaluate("=WRAPCOLS(A1:B2,2)", sheet).Should().Be(ErrorValue.Value);
    }
}
