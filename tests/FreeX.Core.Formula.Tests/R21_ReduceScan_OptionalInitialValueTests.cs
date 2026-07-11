using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R21-dynamic-array-functions-1: REDUCE and SCAN's initial_value argument is optional in Excel
/// ("If no value is supplied for the initial_value, the first value in the array will be used
/// as the starting value"). FreeX previously registered both with minArgs==maxArgs==3, so the
/// 2-arg form REDUCE(array, lambda) / SCAN(array, lambda) was rejected with #VALUE! before ever
/// reaching ReduceFunc/ScanFunc.
/// </summary>
public class R21_ReduceScan_OptionalInitialValueTests
{
    private readonly FormulaEvaluator _eval = new();
    private readonly Sheet _sheet = new(SheetId.New(), "Sheet1");

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, _sheet);

    private void Set(int row, int col, ScalarValue val) =>
        _sheet.SetCell(new CellAddress(_sheet.Id, (uint)row, (uint)col), val);

    private static double Num(ScalarValue v) => ((NumberValue)v).Value;
    private static RangeValue Rv(ScalarValue v) => (RangeValue)v;

    [Fact]
    public void Reduce_TwoArgForm_SeedsFromFirstArrayElement()
    {
        // A1:A3 = {10,20,30}; omitted initial_value -> seed = 10, then fold 20, then 30 => 60.
        Set(1, 1, new NumberValue(10));
        Set(2, 1, new NumberValue(20));
        Set(3, 1, new NumberValue(30));

        var result = Eval("=REDUCE(A1:A3, LAMBDA(a,v,a+v))");
        Assert.Equal(60.0, Num(result));
    }

    [Fact]
    public void Reduce_TwoArgForm_MatchesThreeArgFormWithFirstElementAsInitialValue()
    {
        Set(1, 1, new NumberValue(2));
        Set(2, 1, new NumberValue(3));
        Set(3, 1, new NumberValue(4));

        var twoArg = Eval("=REDUCE(A1:A3, LAMBDA(a,v,a*v))");
        var threeArg = Eval("=REDUCE(2, A2:A3, LAMBDA(a,v,a*v))");
        Assert.Equal(Num(threeArg), Num(twoArg));
        Assert.Equal(24.0, Num(twoArg));
    }

    [Fact]
    public void Scan_TwoArgForm_SeedsFromFirstArrayElement_AndKeepsItAsFirstOutput()
    {
        // A1:A3 = {1,2,3}; omitted initial_value -> output = {1, 1+2, 3+3} = {1,3,6}.
        Set(1, 1, new NumberValue(1));
        Set(2, 1, new NumberValue(2));
        Set(3, 1, new NumberValue(3));

        var result = Rv(Eval("=SCAN(A1:A3, LAMBDA(a,v,a+v))"));
        Assert.Equal(3, result.RowCount);
        Assert.Equal(1, result.ColCount);
        Assert.Equal(1.0, Num(result.At(1, 1)));
        Assert.Equal(3.0, Num(result.At(2, 1)));
        Assert.Equal(6.0, Num(result.At(3, 1)));
    }

    [Fact]
    public void ReduceAndScan_ThreeArgForm_StillWorksUnchanged()
    {
        Set(1, 1, new NumberValue(1));
        Set(1, 2, new NumberValue(2));
        Set(1, 3, new NumberValue(3));

        var reduced = Eval("=REDUCE(0, A1:C1, LAMBDA(acc, x, acc+x))");
        Assert.Equal(6.0, Num(reduced));

        var scanned = Rv(Eval("=SCAN(0, A1:C1, LAMBDA(acc, x, acc+x))"));
        Assert.Equal(1.0, Num(scanned.At(1, 1)));
        Assert.Equal(3.0, Num(scanned.At(1, 2)));
        Assert.Equal(6.0, Num(scanned.At(1, 3)));
    }
}
