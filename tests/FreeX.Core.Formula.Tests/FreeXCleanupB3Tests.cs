using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for FreeX cleanup batch B3 (HIGH findings P72).
/// LAMBDA must be lexically scoped (a true closure over its definition-site environment),
/// not dynamically scoped against whatever context happens to be active when it is invoked.
/// </summary>
public class FreeXCleanupB3Tests
{
    private readonly FormulaEvaluator _eval = new();
    private readonly Sheet _sheet = new(SheetId.New(), "Sheet1");

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, _sheet);

    private void Set(int row, int col, ScalarValue val) =>
        _sheet.SetCell(new CellAddress(_sheet.Id, (uint)row, (uint)col), val);

    private static double Num(ScalarValue v) => ((NumberValue)v).Value;
    private static RangeValue Rv(ScalarValue v) => (RangeValue)v;

    // P72: escaping lambda must capture its defining LET scope, i.e. a curried lambda that
    // returns another lambda closing over the outer parameter (Excel: 15). NOTE: the returned
    // lambda is bound to "adderFive" rather than "add5" on purpose — "add5" is a valid A1 cell
    // reference (column ADD = 784, row 5), so LET/Excel rejects it as a binding name.
    [Fact]
    public void Lambda_EscapingClosure_CapturesDefiningLetScope()
    {
        var result = Eval("=LET(makeAdder, LAMBDA(n, LAMBDA(x, x+n)), adderFive, makeAdder(5), adderFive(10))");
        Assert.Equal(15.0, Num(result));
    }

    // P72: a lambda passed into MAP must resolve free variables against the LET scope
    // that defined it, not the outer/cell evaluation context (Excel: v*2 per element).
    [Fact]
    public void Lambda_PassedToMap_ResolvesFreeVariableFromDefiningLetScope()
    {
        Set(1, 1, new NumberValue(1));
        Set(2, 1, new NumberValue(2));
        Set(3, 1, new NumberValue(3));
        var result = Rv(Eval("=MAP(A1:A3, LET(k, 2, LAMBDA(v, v*k)))"));
        Assert.Equal(2.0, Num(result.At(1, 1)));
        Assert.Equal(4.0, Num(result.At(2, 1)));
        Assert.Equal(6.0, Num(result.At(3, 1)));
    }

    // P72: shadowing must resolve lexically at definition time, not dynamically at call time
    // (Excel: 2, because f's free variable "a" is bound to the outer a=1 at the point f was defined).
    [Fact]
    public void Lambda_ShadowedOuterBinding_ResolvesLexicallyNotDynamically()
    {
        var result = Eval("=LET(a, 1, f, LAMBDA(b, a+b), LET(a, 10, f(1)))");
        Assert.Equal(2.0, Num(result));
    }

    // A lambda with no free variables and no enclosing LET must still work unchanged
    // (top-level LAMBDA with a null closure falls back to the call-site context).
    [Fact]
    public void Lambda_NoFreeVariables_StillEvaluatesCorrectly()
    {
        var result = Eval("=LET(double, LAMBDA(x, x*2), double(21))");
        Assert.Equal(42.0, Num(result));
    }
}
