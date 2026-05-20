using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Formula.Tests;

public class PhaseDLambdaScalarArrayTests
{
    private readonly FormulaEvaluator _eval = new();
    private readonly Sheet _sheet = new(SheetId.New(), "Sheet1");

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, _sheet);

    private static double Num(ScalarValue value) => ((NumberValue)value).Value;

    private static RangeValue Rv(ScalarValue value) => (RangeValue)value;

    [Fact]
    public void HigherOrderFunctions_TreatScalarArraysAsSingleCellArrays()
    {
        var mapped = Rv(Eval("=MAP(5, LAMBDA(x, x*2))"));
        Assert.Equal(1, mapped.RowCount);
        Assert.Equal(1, mapped.ColCount);
        Assert.Equal(10.0, Num(mapped.At(1, 1)));

        Assert.Equal(6.0, Num(Eval("=REDUCE(1, 5, LAMBDA(acc, x, acc+x))")));

        var scanned = Rv(Eval("=SCAN(1, 5, LAMBDA(acc, x, acc+x))"));
        Assert.Equal(1, scanned.RowCount);
        Assert.Equal(1, scanned.ColCount);
        Assert.Equal(6.0, Num(scanned.At(1, 1)));

        var byRow = Rv(Eval("=BYROW(5, LAMBDA(row, SUM(row)))"));
        Assert.Equal(1, byRow.RowCount);
        Assert.Equal(1, byRow.ColCount);
        Assert.Equal(5.0, Num(byRow.At(1, 1)));

        var byCol = Rv(Eval("=BYCOL(5, LAMBDA(col, SUM(col)))"));
        Assert.Equal(1, byCol.RowCount);
        Assert.Equal(1, byCol.ColCount);
        Assert.Equal(5.0, Num(byCol.At(1, 1)));
    }
}
