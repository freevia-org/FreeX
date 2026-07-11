using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void Take_PositiveRowsAndColumns_ReturnsTopLeftSlice()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=TAKE(A1:C3,2,2)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(2));
        rv.Cells[1, 0].Should().Be(new NumberValue(4));
        rv.Cells[1, 1].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Take_TreatsScalarArrayAsSingleCellArray()
    {
        var taken = _eval.Evaluate("=TAKE(5,1)", MakeSheet())
            .Should().BeOfType<RangeValue>().Subject;
        taken.RowCount.Should().Be(1);
        taken.ColCount.Should().Be(1);
        taken.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void TakeAndDrop_AcceptSpilledScalarSliceCounts()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var taken = _eval.Evaluate("=TAKE(A1:C3,SEQUENCE(1,,2),SEQUENCE(1,,2))", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        taken.RowCount.Should().Be(2);
        taken.ColCount.Should().Be(2);
        taken.Cells[1, 1].Should().Be(new NumberValue(5));

        var dropped = _eval.Evaluate("=DROP(A1:C3,SEQUENCE(1,,1),SEQUENCE(1,,1))", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        dropped.RowCount.Should().Be(2);
        dropped.ColCount.Should().Be(2);
        dropped.Cells[0, 0].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Take_OmittedRows_TakesRequestedColumnsFromAllRows()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=TAKE(A1:C3,,2)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[2, 1].Should().Be(new NumberValue(8));
    }

    [Fact]
    public void Drop_OmittedRows_DropsRequestedColumnsFromAllRows()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=DROP(A1:C3,,1)", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(2));
        rv.Cells[2, 1].Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Take_NegativeRowsAndColumns_ReturnsBottomRightSlice()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=TAKE(A1:C3,-2,-2)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(5));
        rv.Cells[0, 1].Should().Be(new NumberValue(6));
        rv.Cells[1, 0].Should().Be(new NumberValue(8));
        rv.Cells[1, 1].Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Drop_PositiveRowsAndColumns_RemovesTopLeftSlice()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=DROP(A1:C3,1,1)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(5));
        rv.Cells[0, 1].Should().Be(new NumberValue(6));
        rv.Cells[1, 0].Should().Be(new NumberValue(8));
        rv.Cells[1, 1].Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Drop_NegativeRowsAndColumns_RemovesBottomRightSlice()
    {
        var sheet = MakeSheet(
            (1,1,new NumberValue(1)), (1,2,new NumberValue(2)), (1,3,new NumberValue(3)),
            (2,1,new NumberValue(4)), (2,2,new NumberValue(5)), (2,3,new NumberValue(6)),
            (3,1,new NumberValue(7)), (3,2,new NumberValue(8)), (3,3,new NumberValue(9)));

        var result = _eval.Evaluate("=DROP(A1:C3,-1,-1)", sheet);

        var rv = (RangeValue)result;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(2));
        rv.Cells[1, 0].Should().Be(new NumberValue(4));
        rv.Cells[1, 1].Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Take_ZeroRows_ReturnsCalcError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=TAKE(A1:A1,0)", sheet).Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void Drop_ZeroRowsOrColumns_ReturnsCalcError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)), (1,2,new NumberValue(2)));

        _eval.Evaluate("=DROP(A1:B1,0)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=DROP(A1:B1,,0)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=DROP(5,0)", MakeSheet()).Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void Take_HugeSliceCountBeyondInt32Range_ClampsToWholeDimension()
    {
        // Real Excel: a rows/cols count whose magnitude exceeds the array's size - even one far outside
        // Int32 range, e.g. 1E10 - is treated as "take everything", not as an error. This also covers the
        // exact Int32.MinValue boundary (-2147483648), which is a legally-representable finite double.
        var sheet = MakeSheet((1,1,new NumberValue(1)), (2,1,new NumberValue(2)));

        foreach (var formula in new[]
                 {
                     "=TAKE(A1:A2,2147483648)",
                     "=TAKE(A1:A2,-2147483648)",
                     "=TAKE(A1:A2,-2147483649)",
                     "=TAKE(A1:A2,1E10)",
                     "=TAKE(A1:A2,-1E10)",
                 })
        {
            var rv = _eval.Evaluate(formula, sheet).Should().BeOfType<RangeValue>().Subject;
            rv.RowCount.Should().Be(2, because: formula);
            rv.ColCount.Should().Be(1, because: formula);
            rv.Cells[0, 0].Should().Be(new NumberValue(1), because: formula);
            rv.Cells[1, 0].Should().Be(new NumberValue(2), because: formula);
        }
    }

    [Fact]
    public void Take_InRangeSliceCountLargerThanDimension_StillClampsToWholeDimension()
    {
        // Sibling already-working case: an in-range (fits in Int32) but still oversized count already
        // clamped correctly before this fix, and must continue to do so.
        var sheet = MakeSheet((1,1,new NumberValue(1)), (2,1,new NumberValue(2)));

        var rv = _eval.Evaluate("=TAKE(A1:A2,100)", sheet).Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Drop_HugeSliceCountBeyondInt32Range_ReturnsCalcError()
    {
        // Dropping more rows than exist is a #CALC! error in Excel regardless of whether the requested
        // magnitude fits in Int32 - it must not surface as #VALUE! just because the raw count overflows
        // Int32 range (e.g. 1E10, or the exact Int32.MinValue boundary).
        var sheet = MakeSheet((1,1,new NumberValue(1)), (2,1,new NumberValue(2)));

        _eval.Evaluate("=DROP(A1:A2,2147483648)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=DROP(A1:A2,-2147483648)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=DROP(A1:A2,-2147483649)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=DROP(A1:A2,1E10)", sheet).Should().Be(ErrorValue.Calc);
        _eval.Evaluate("=DROP(A1:A2,-1E10)", sheet).Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void Drop_AllRows_ReturnsCalcError()
    {
        var sheet = MakeSheet((1,1,new NumberValue(1)));

        _eval.Evaluate("=DROP(A1:A1,1)", sheet).Should().Be(ErrorValue.Calc);
    }
}
