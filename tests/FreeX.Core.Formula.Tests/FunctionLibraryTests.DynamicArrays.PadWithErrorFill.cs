using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FunctionLibraryTests
{
    [Fact]
    public void Expand_PadWithError_UsesErrorAsFillValue()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(3)), (2, 2, new NumberValue(4)));

        var result = _eval.Evaluate("=EXPAND(A1:B2,4,4,NA())", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(4);
        rv.ColCount.Should().Be(4);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(2));
        rv.Cells[1, 0].Should().Be(new NumberValue(3));
        rv.Cells[1, 1].Should().Be(new NumberValue(4));
        rv.Cells[0, 2].Should().Be(ErrorValue.NA);
        rv.Cells[0, 3].Should().Be(ErrorValue.NA);
        rv.Cells[2, 0].Should().Be(ErrorValue.NA);
        rv.Cells[3, 3].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Expand_ErroringRowsArgument_StillReturnsThatError()
    {
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)));

        // Sibling case: an error in a genuine control argument (rows) must still abort the
        // whole call, unlike an error used as the pad_with fill value above.
        _eval.Evaluate("=EXPAND(A1:B1,NA())", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=EXPAND(A1:B1,2,NA())", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Wraprows_PadWithError_UsesErrorAsFillValue()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (1, 3, new NumberValue(3)), (1, 4, new NumberValue(4)),
            (1, 5, new NumberValue(5)));

        var result = _eval.Evaluate("=WRAPROWS(A1:E1,2,NA())", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(3);
        rv.ColCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[0, 1].Should().Be(new NumberValue(2));
        rv.Cells[2, 0].Should().Be(new NumberValue(5));
        rv.Cells[2, 1].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Wrapcols_PadWithError_UsesErrorAsFillValue()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (1, 3, new NumberValue(3)), (1, 4, new NumberValue(4)),
            (1, 5, new NumberValue(5)));

        var result = _eval.Evaluate("=WRAPCOLS(A1:E1,2,NA())", sheet);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(3);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
        rv.Cells[0, 2].Should().Be(new NumberValue(5));
        rv.Cells[1, 2].Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Wraprows_ErroringWrapCountArgument_StillReturnsThatError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)),
            (2, 1, new NumberValue(3)), (2, 2, new NumberValue(4)));

        // Sibling case: an error in the wrap_count control argument must still abort the call.
        _eval.Evaluate("=WRAPROWS(A1:B2,NA())", sheet).Should().Be(ErrorValue.NA);
    }
}
