using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R28-lambda-helpers-deep-3: MapFunc required every array argument to match array1's exact
/// dimensions, so a single-cell (scalar) array argument failed to broadcast against a
/// multi-row/column array1. Real Excel broadcasts a 1x1 array argument (whether it arrives as
/// a bare cell reference like B1 or an explicit single-cell range like B1:B1) across every
/// row/column of the other array(s). Fixed in MapFunc (BuiltInFunctions.HigherOrder.cs) so a
/// 1x1 operand is treated as a scalar and broadcast, while arrays that are NOT 1x1 must still
/// all share one shape (a genuine dimension mismatch still returns #VALUE!).
/// </summary>
public sealed class Round28MapScalarBroadcastTests
{
    private readonly FormulaEvaluator _eval = new();
    private readonly Sheet _sheet = new(SheetId.New(), "Sheet1");

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, _sheet);

    private void Set(int row, int col, ScalarValue val) =>
        _sheet.SetCell(new CellAddress(_sheet.Id, (uint)row, (uint)col), val);

    private static double Num(ScalarValue v) => ((NumberValue)v).Value;
    private static RangeValue Rv(ScalarValue v) => (RangeValue)v;

    [Fact]
    public void Map_VerticalArrayPlusScalarCell_BroadcastsScalarAcrossAllRows()
    {
        // Bug case straight from the review finding: A1:A3 = {1;2;3} (vertical), B1 = 10.
        // Real Excel returns {11;12;13}; FreeX used to return #VALUE!.
        Set(1, 1, new NumberValue(1));
        Set(2, 1, new NumberValue(2));
        Set(3, 1, new NumberValue(3));
        Set(1, 2, new NumberValue(10));

        var result = Rv(Eval("=MAP(A1:A3, B1, LAMBDA(v, t, v+t))"));
        Assert.Equal(3, result.RowCount);
        Assert.Equal(1, result.ColCount);
        Assert.Equal(11.0, Num(result.At(1, 1)));
        Assert.Equal(12.0, Num(result.At(2, 1)));
        Assert.Equal(13.0, Num(result.At(3, 1)));
    }

    [Fact]
    public void Map_TwoArraysSameShape_StillComputesElementWise()
    {
        // Sibling (already-working) case: two arrays of identical shape must be unaffected.
        Set(1, 1, new NumberValue(10));
        Set(1, 2, new NumberValue(20));
        Set(2, 1, new NumberValue(1));
        Set(2, 2, new NumberValue(2));

        var result = Rv(Eval("=MAP(A1:B1, A2:B2, LAMBDA(a, b, a+b))"));
        Assert.Equal(11.0, Num(result.At(1, 1)));
        Assert.Equal(22.0, Num(result.At(1, 2)));
    }

    [Fact]
    public void Map_GenuinelyMismatchedNonScalarShapes_StillReturnsValueError()
    {
        // Must NOT swing to the opposite extreme: two non-1x1 arrays with different shapes
        // still have no defined broadcast rule and must still error.
        Set(1, 1, new NumberValue(1));
        Set(1, 2, new NumberValue(2));
        Set(2, 1, new NumberValue(1));
        Set(2, 2, new NumberValue(2));
        Set(3, 1, new NumberValue(3));

        var result = Eval("=MAP(A1:B1, A2:A3, LAMBDA(a, b, a+b))");
        Assert.Equal(ErrorValue.Value, result);
    }
}
