using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for FreeX cleanup batch MED9 (MED findings P75).
/// </summary>
public class FreeXCleanupMED9Tests
{
    private readonly FormulaEvaluator _eval = new();
    private readonly Sheet _sheet = new(SheetId.New(), "Sheet1");

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, _sheet);

    private static double Num(ScalarValue value) => ((NumberValue)value).Value;

    private static RangeValue Rv(ScalarValue value) => (RangeValue)value;

    // P75: REDUCE must permit an array-valued accumulator (Excel's own REDUCE documentation shows
    // exactly this VSTACK-accumulation idiom). Previously the RangeValue guard borrowed from
    // MAP/BYROW/BYCOL/MAKEARRAY (which Excel does reject for nested-array results) incorrectly
    // fired for REDUCE too, turning every intermediate array accumulator into #CALC!.
    [Fact]
    public void Reduce_WithVStackArrayAccumulator_ReturnsStackedArray_NotCalcError()
    {
        var result = Eval("=REDUCE(\"\", SEQUENCE(3), LAMBDA(a,v, VSTACK(a, v*2)))");

        var range = Rv(result);
        Assert.Equal(4, range.RowCount);
        Assert.Equal(1, range.ColCount);
        Assert.Equal("", ((TextValue)range.At(1, 1)).Value);
        Assert.Equal(2.0, Num(range.At(2, 1)));
        Assert.Equal(4.0, Num(range.At(3, 1)));
        Assert.Equal(6.0, Num(range.At(4, 1)));
    }

    // P75 (error propagation guard): REDUCE must still surface a genuine error raised inside the
    // lambda rather than silently continuing accumulation once the RangeValue-only guard is removed.
    [Fact]
    public void Reduce_LambdaRaisesError_PropagatesErrorInsteadOfContinuing()
    {
        var result = Eval("=REDUCE(0, SEQUENCE(3), LAMBDA(a,v, IF(v=2, 1/0, a+v)))");

        Assert.IsType<ErrorValue>(result);
        Assert.Equal(ErrorValue.DivByZero, result);
    }
}
