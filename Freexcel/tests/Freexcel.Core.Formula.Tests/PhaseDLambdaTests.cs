using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Formula.Tests;

/// <summary>
/// Tests for LET, LAMBDA, and higher-order array functions (MAP, REDUCE, SCAN, BYROW, BYCOL, MAKEARRAY).
/// </summary>
public class PhaseDLambdaTests
{
    private readonly FormulaEvaluator _eval = new();
    private readonly Sheet _sheet = new(SheetId.New(), "Sheet1");

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, _sheet);

    private void Set(int row, int col, ScalarValue val) =>
        _sheet.SetCell(new CellAddress(_sheet.Id, (uint)row, (uint)col), val);

    private static double Num(ScalarValue v) => ((NumberValue)v).Value;
    private static RangeValue Rv(ScalarValue v) => (RangeValue)v;

    // ── LET ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Let_SingleBinding_ReturnsCalculation()
    {
        var result = Eval("=LET(x, 5, x*2)");
        Assert.Equal(10.0, Num(result));
    }

    [Fact]
    public void Let_MultipleBindings_LaterCanReferenceEarlier()
    {
        // y = x+1 = 4, x*y = 3*4 = 12
        var result = Eval("=LET(x, 3, y, x+1, x*y)");
        Assert.Equal(12.0, Num(result));
    }

    [Fact]
    public void Let_ThreeBindings()
    {
        // a=1, b=2, c=a+b=3, a+b+c=6
        var result = Eval("=LET(a, 1, b, 2, c, a+b, a+b+c)");
        Assert.Equal(6.0, Num(result));
    }

    [Fact]
    public void Let_BindingUsedInCalculation_ReturnsText()
    {
        var result = Eval("=LET(name, \"world\", CONCAT(\"hello \", name))");
        Assert.Equal("hello world", ((TextValue)result).Value);
    }

    [Fact]
    public void Let_RangeBindingCanBeUsedInArrayExpression()
    {
        Set(1, 1, new NumberValue(1));
        Set(2, 1, new NumberValue(2));
        Set(3, 1, new NumberValue(3));

        var result = Eval("=LET(x, A1:A3, SUM(x*2))");

        Assert.Equal(12.0, Num(result));
    }

    [Fact]
    public void Let_TooFewArgs_ReturnsValueError()
    {
        Assert.Equal(ErrorValue.Value, Eval("=LET(x, 5)"));
    }

    [Fact]
    public void Let_EvenArgCount_ReturnsValueError()
    {
        Assert.Equal(ErrorValue.Value, Eval("=LET(x, 5, y, 3)"));
    }

    // ── LAMBDA ──────────────────────────────────────────────────────────────

    [Fact]
    public void Lambda_CalledViaLet_SingleParam()
    {
        var result = Eval("=LET(double, LAMBDA(x, x*2), double(5))");
        Assert.Equal(10.0, Num(result));
    }

    [Fact]
    public void Lambda_RangeArgumentCanBeUsedInArrayExpression()
    {
        Set(1, 1, new NumberValue(1));
        Set(2, 1, new NumberValue(2));
        Set(3, 1, new NumberValue(3));

        var result = Eval("=LET(doubleSum, LAMBDA(x, SUM(x*2)), doubleSum(A1:A3))");

        Assert.Equal(12.0, Num(result));
    }

    [Fact]
    public void Lambda_CalledViaLet_TwoParams()
    {
        var result = Eval("=LET(add, LAMBDA(a, b, a+b), add(3, 4))");
        Assert.Equal(7.0, Num(result));
    }

    [Fact]
    public void Lambda_ZeroParams_CalledWithNoArgs()
    {
        // LAMBDA(42) creates a zero-param lambda that always returns 42
        var result = Eval("=LET(forty2, LAMBDA(42), forty2())");
        Assert.Equal(42.0, Num(result));
    }

    [Fact]
    public void Lambda_WrongArgCount_ReturnsValueError()
    {
        var result = Eval("=LET(f, LAMBDA(x, x+1), f(1, 2))");
        Assert.Equal(ErrorValue.Value, result);
    }

    [Fact]
    public void Lambda_NestedLet_InnerShadowsOuter()
    {
        // outer x=10; inner LET rebinds x=3 → 3*2=6
        var result = Eval("=LET(x, 10, LET(x, 3, x*2))");
        Assert.Equal(6.0, Num(result));
    }

    [Fact]
    public void Lambda_RecursiveFactorial()
    {
        // fact(n) = IF(n<=1, 1, n*fact(n-1))
        var result = Eval("=LET(fact, LAMBDA(n, IF(n<=1, 1, n*fact(n-1))), fact(5))");
        Assert.Equal(120.0, Num(result));
    }

    [Fact]
    public void Lambda_RecursiveFibonacci()
    {
        var result = Eval("=LET(fib, LAMBDA(n, IF(n<=1, n, fib(n-1)+fib(n-2))), fib(7))");
        Assert.Equal(13.0, Num(result));
    }

    // ── MAP ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Map_SingleArray_DoubleEachElement()
    {
        Set(1, 1, new NumberValue(1));
        Set(1, 2, new NumberValue(2));
        Set(1, 3, new NumberValue(3));

        var result = Rv(Eval("=MAP(A1:C1, LAMBDA(x, x*2))"));
        Assert.Equal(1, result.RowCount);
        Assert.Equal(3, result.ColCount);
        Assert.Equal(2.0, Num(result.At(1, 1)));
        Assert.Equal(4.0, Num(result.At(1, 2)));
        Assert.Equal(6.0, Num(result.At(1, 3)));
    }

    [Fact]
    public void Map_TwoArrays_ElementWiseSum()
    {
        Set(1, 1, new NumberValue(10));
        Set(1, 2, new NumberValue(20));
        Set(2, 1, new NumberValue(1));
        Set(2, 2, new NumberValue(2));

        var result = Rv(Eval("=MAP(A1:B1, A2:B2, LAMBDA(a, b, a+b))"));
        Assert.Equal(11.0, Num(result.At(1, 1)));
        Assert.Equal(22.0, Num(result.At(1, 2)));
    }

    [Fact]
    public void Map_MismatchedArraySizes_ReturnsValueError()
    {
        Set(1, 1, new NumberValue(1));
        Set(1, 2, new NumberValue(2));
        Set(2, 1, new NumberValue(1));

        // A1:B1 is 1x2, A2 is 1x1 → size mismatch
        var result = Eval("=MAP(A1:B1, A2:A2, LAMBDA(a, b, a+b))");
        Assert.Equal(ErrorValue.Value, result);
    }

    [Fact]
    public void Map_ArrayReturningLambda_ReturnsCalcError()
    {
        Set(16, 1, new NumberValue(1));
        Set(16, 2, new NumberValue(2));

        Assert.Equal(ErrorValue.Calc, Eval("=MAP(A16:B16, LAMBDA(x, HSTACK(x,x)))"));
    }

    // ── REDUCE ──────────────────────────────────────────────────────────────

    [Fact]
    public void Reduce_SumArray()
    {
        Set(3, 1, new NumberValue(1));
        Set(3, 2, new NumberValue(2));
        Set(3, 3, new NumberValue(3));
        Set(3, 4, new NumberValue(4));

        var result = Eval("=REDUCE(0, A3:D3, LAMBDA(acc, x, acc+x))");
        Assert.Equal(10.0, Num(result));
    }

    [Fact]
    public void Reduce_ProductWithInitialOne()
    {
        Set(4, 1, new NumberValue(2));
        Set(4, 2, new NumberValue(3));
        Set(4, 3, new NumberValue(4));

        var result = Eval("=REDUCE(1, A4:C4, LAMBDA(acc, x, acc*x))");
        Assert.Equal(24.0, Num(result));
    }

    [Fact]
    public void Reduce_MaxValue()
    {
        Set(5, 1, new NumberValue(7));
        Set(5, 2, new NumberValue(3));
        Set(5, 3, new NumberValue(9));
        Set(5, 4, new NumberValue(2));

        var result = Eval("=REDUCE(A5, B5:D5, LAMBDA(acc, x, IF(x>acc, x, acc)))");
        Assert.Equal(9.0, Num(result));
    }

    // ── SCAN ────────────────────────────────────────────────────────────────

    [Fact]
    public void Scan_RunningSum()
    {
        Set(6, 1, new NumberValue(1));
        Set(6, 2, new NumberValue(2));
        Set(6, 3, new NumberValue(3));

        var result = Rv(Eval("=SCAN(0, A6:C6, LAMBDA(acc, x, acc+x))"));
        Assert.Equal(1, result.RowCount);
        Assert.Equal(3, result.ColCount);
        Assert.Equal(1.0, Num(result.At(1, 1)));   // 0+1
        Assert.Equal(3.0, Num(result.At(1, 2)));   // 1+2
        Assert.Equal(6.0, Num(result.At(1, 3)));   // 3+3
    }

    [Fact]
    public void Scan_RunningProduct()
    {
        Set(7, 1, new NumberValue(2));
        Set(7, 2, new NumberValue(3));
        Set(7, 3, new NumberValue(4));

        var result = Rv(Eval("=SCAN(1, A7:C7, LAMBDA(acc, x, acc*x))"));
        Assert.Equal(2.0,  Num(result.At(1, 1)));  // 1*2
        Assert.Equal(6.0,  Num(result.At(1, 2)));  // 2*3
        Assert.Equal(24.0, Num(result.At(1, 3)));  // 6*4
    }

    // ── BYROW ───────────────────────────────────────────────────────────────

    [Fact]
    public void Scan_ArrayReturningLambda_ReturnsCalcError()
    {
        Set(16, 1, new NumberValue(1));
        Set(16, 2, new NumberValue(2));

        Assert.Equal(ErrorValue.Calc, Eval("=SCAN(0, A16:B16, LAMBDA(acc, x, HSTACK(acc,x)))"));
    }

    [Fact]
    public void ByRow_SumEachRow()
    {
        Set(8, 1, new NumberValue(1));
        Set(8, 2, new NumberValue(2));
        Set(9, 1, new NumberValue(3));
        Set(9, 2, new NumberValue(4));

        var result = Rv(Eval("=BYROW(A8:B9, LAMBDA(row, SUM(row)))"));
        Assert.Equal(2, result.RowCount);
        Assert.Equal(1, result.ColCount);
        Assert.Equal(3.0, Num(result.At(1, 1)));   // 1+2
        Assert.Equal(7.0, Num(result.At(2, 1)));   // 3+4
    }

    [Fact]
    public void ByRow_MaxEachRow()
    {
        Set(10, 1, new NumberValue(5));
        Set(10, 2, new NumberValue(2));
        Set(11, 1, new NumberValue(1));
        Set(11, 2, new NumberValue(8));

        var result = Rv(Eval("=BYROW(A10:B11, LAMBDA(row, MAX(row)))"));
        Assert.Equal(5.0, Num(result.At(1, 1)));
        Assert.Equal(8.0, Num(result.At(2, 1)));
    }

    // ── BYCOL ───────────────────────────────────────────────────────────────

    [Fact]
    public void ByCol_SumEachColumn()
    {
        Set(12, 1, new NumberValue(1));
        Set(12, 2, new NumberValue(2));
        Set(13, 1, new NumberValue(3));
        Set(13, 2, new NumberValue(4));

        var result = Rv(Eval("=BYCOL(A12:B13, LAMBDA(col, SUM(col)))"));
        Assert.Equal(1, result.RowCount);
        Assert.Equal(2, result.ColCount);
        Assert.Equal(4.0, Num(result.At(1, 1)));   // 1+3
        Assert.Equal(6.0, Num(result.At(1, 2)));   // 2+4
    }

    [Fact]
    public void ByCol_MinEachColumn()
    {
        Set(14, 1, new NumberValue(5));
        Set(14, 2, new NumberValue(2));
        Set(15, 1, new NumberValue(1));
        Set(15, 2, new NumberValue(9));

        var result = Rv(Eval("=BYCOL(A14:B15, LAMBDA(col, MIN(col)))"));
        Assert.Equal(1.0, Num(result.At(1, 1)));
        Assert.Equal(2.0, Num(result.At(1, 2)));
    }

    [Fact]
    public void ByRowAndByCol_ArrayReturningLambda_ReturnsCalcError()
    {
        Set(16, 1, new NumberValue(1));
        Set(16, 2, new NumberValue(2));
        Set(17, 1, new NumberValue(3));
        Set(17, 2, new NumberValue(4));

        Assert.Equal(ErrorValue.Calc, Eval("=BYROW(A16:B17, LAMBDA(row, row))"));
        Assert.Equal(ErrorValue.Calc, Eval("=BYCOL(A16:B17, LAMBDA(col, col))"));
    }

    // ── MAKEARRAY ───────────────────────────────────────────────────────────

    [Fact]
    public void MakeArray_MultiplicationTable()
    {
        var result = Rv(Eval("=MAKEARRAY(3, 4, LAMBDA(r, c, r*c))"));
        Assert.Equal(3, result.RowCount);
        Assert.Equal(4, result.ColCount);
        Assert.Equal(1.0, Num(result.At(1, 1)));
        Assert.Equal(4.0, Num(result.At(1, 4)));
        Assert.Equal(6.0, Num(result.At(2, 3)));
        Assert.Equal(12.0, Num(result.At(3, 4)));
    }

    [Fact]
    public void MakeArray_RowPlusCol()
    {
        var result = Rv(Eval("=MAKEARRAY(2, 2, LAMBDA(r, c, r+c))"));
        Assert.Equal(2.0, Num(result.At(1, 1)));   // 1+1
        Assert.Equal(3.0, Num(result.At(1, 2)));   // 1+2
        Assert.Equal(3.0, Num(result.At(2, 1)));   // 2+1
        Assert.Equal(4.0, Num(result.At(2, 2)));   // 2+2
    }

    [Fact]
    public void MakeArray_ZeroRows_ReturnsValueError()
    {
        Assert.Equal(ErrorValue.Value, Eval("=MAKEARRAY(0, 3, LAMBDA(r, c, 1))"));
    }

    [Fact]
    public void MakeArray_RowOrColumnError_PropagatesError()
    {
        Assert.Equal(ErrorValue.NA, Eval("=MAKEARRAY(NA(), 3, LAMBDA(r, c, 1))"));
        Assert.Equal(ErrorValue.DivByZero, Eval("=MAKEARRAY(3, 1/0, LAMBDA(r, c, 1))"));
    }

    [Fact]
    public void MakeArray_CoercesNumericTextDimensions()
    {
        var result = Rv(Eval("=MAKEARRAY(\"2\", \"2\", LAMBDA(r, c, r+c))"));

        Assert.Equal(2, result.RowCount);
        Assert.Equal(2, result.ColCount);
        Assert.Equal(2.0, Num(result.At(1, 1)));
        Assert.Equal(4.0, Num(result.At(2, 2)));
    }

    [Fact]
    public void MakeArray_ArrayReturningLambda_ReturnsCalcError()
    {
        Set(16, 1, new NumberValue(1));
        Set(16, 2, new NumberValue(2));

        Assert.Equal(ErrorValue.Calc, Eval("=MAKEARRAY(2, 1, LAMBDA(r, c, HSTACK(r,c)))"));
    }

    // ── Edge cases ──────────────────────────────────────────────────────────

    [Fact]
    public void Let_LambdaInLambda_Composition()
    {
        // double(triple(x)) = double(3*x) = 6*x  → 6*4 = 24
        var result = Eval("=LET(triple, LAMBDA(x, x*3), double, LAMBDA(x, x*2), double(triple(4)))");
        Assert.Equal(24.0, Num(result));
    }

    [Fact]
    public void Map_LambdaWithLetBinding()
    {
        Set(16, 1, new NumberValue(1));
        Set(16, 2, new NumberValue(2));
        Set(16, 3, new NumberValue(3));

        // MAP with lambda that uses an IF inside
        var result = Rv(Eval("=MAP(A16:C16, LAMBDA(x, IF(x>1, x*10, x)))"));
        Assert.Equal(1.0,  Num(result.At(1, 1)));   // 1 stays 1
        Assert.Equal(20.0, Num(result.At(1, 2)));   // 2 > 1 → 20
        Assert.Equal(30.0, Num(result.At(1, 3)));   // 3 > 1 → 30
    }
}
