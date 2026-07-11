using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-21 findings R21-dynamic-array-functions-2 and -3: LET's final calc_expr and a LAMBDA's
/// body must be evaluated array-aware (mirroring how top-level dynamic-array cell evaluation and
/// LET's own bindings already work), not via the plain scalar-collapsing EvaluateNode path.
///
/// - LET: a bare range as the whole calc_expr (e.g. =LET(x, 5, A1:A3)) must materialize as a
///   RangeValue so RecalcEngine.EvaluateSpilling's `result is RangeValue` branch can spill it,
///   instead of silently collapsing to the top-left cell via implicit intersection.
/// - LAMBDA: a bare range as the whole body (e.g. LAMBDA(x, B1:B3)) must also materialize as a
///   RangeValue so MAP/BYROW/BYCOL/SCAN/MAKEARRAY's `is RangeValue -> #CALC!` nested-array guard
///   actually fires, instead of the range silently collapsing to B1's scalar value and being
///   returned as if it were a valid per-element result.
/// </summary>
public sealed class R21_LetLambdaArrayAwareness_Tests
{
    private readonly FormulaEvaluator _eval = new();
    private readonly Sheet _sheet = new(SheetId.New(), "Sheet1");

    private void Set(int row, int col, double value) =>
        _sheet.SetCell(new CellAddress(_sheet.Id, (uint)row, (uint)col), new NumberValue(value));

    [Fact]
    public void Let_BareRangeBody_SpillsAllCells_NotJustTopLeft()
    {
        // Matches the failure scenario exactly: x is bound but unused; the calc_expr is a bare
        // multi-cell range. Real Excel spills all 3 rows of A1:A3, just like a bare =A1:A3 would.
        Set(1, 1, 10);
        Set(2, 1, 20);
        Set(3, 1, 30);

        var ast = FormulaEvaluator.ParseFormula("=LET(x, 5, A1:A3)");

        // RecalcEngine calls EvaluateSpilling for ArrayMode.Dynamic cells (RecalcEngine.cs:208-209);
        // exercise that same public entry point directly.
        var result = _eval.EvaluateSpilling(ast, _sheet);

        var range = Assert.IsType<RangeValue>(result);
        Assert.Equal(3, range.RowCount);
        Assert.Equal(1, range.ColCount);
        Assert.Equal(10.0, ((NumberValue)range.At(1, 1)).Value);
        Assert.Equal(20.0, ((NumberValue)range.At(2, 1)).Value);
        Assert.Equal(30.0, ((NumberValue)range.At(3, 1)).Value);
    }

    [Fact]
    public void Let_ScalarBody_StillReturnsScalar_NoRegression()
    {
        // Regression guard: the common case (a computed scalar body) must be unaffected by routing
        // through the array-aware operand evaluator instead of plain EvaluateNode.
        var ast = FormulaEvaluator.ParseFormula("=LET(x, 3, y, x+1, x*y)");
        var result = _eval.EvaluateSpilling(ast, _sheet);

        Assert.Equal(12.0, ((NumberValue)result).Value);
    }

    [Fact]
    public void Map_LambdaBodyBareRange_ReturnsCalcError_NotWrongScalar()
    {
        // The lambda ignores its bound parameter x and returns a bare multi-cell range B1:B3
        // instead. MapFunc's own guard (`if (value is RangeValue) return ErrorValue.Calc;`) can only
        // fire if InvokeLambda actually hands back a RangeValue for that body; before the fix it
        // silently collapsed to B1's scalar via implicit intersection and MAP returned {B1;B1;B1}
        // with no error at all.
        Set(1, 1, 1);
        Set(2, 1, 2);
        Set(3, 1, 3);
        Set(1, 2, 100);
        Set(2, 2, 200);
        Set(3, 2, 300);

        var result = _eval.Evaluate("=MAP(A1:A3, LAMBDA(x, B1:B3))", _sheet);

        Assert.Equal(ErrorValue.Calc, result);
    }

    [Fact]
    public void Lambda_ScalarBody_StillEvaluatesNormally_NoRegression()
    {
        // Regression guard for the common LAMBDA case: a scalar-producing body must behave
        // identically after routing InvokeLambda through the array-aware operand evaluator.
        Set(1, 1, 1);
        Set(1, 2, 2);
        Set(1, 3, 3);

        var result = _eval.Evaluate("=MAP(A1:C1, LAMBDA(x, x*2))", _sheet);

        var range = Assert.IsType<RangeValue>(result);
        Assert.Equal(2.0, ((NumberValue)range.At(1, 1)).Value);
        Assert.Equal(4.0, ((NumberValue)range.At(1, 2)).Value);
        Assert.Equal(6.0, ((NumberValue)range.At(1, 3)).Value);
    }
}
