using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Formula.Tests;

public class StatisticalScalarArrayTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));
        return sheet;
    }

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, MakeSheet());

    private static double Num(ScalarValue value) => ((NumberValue)value).Value;

    [Fact]
    public void StatisticalFunctions_TreatScalarArraysAsSingleCellArrays()
    {
        Assert.Equal(5.0, Num(Eval("=PERCENTILE.EXC(5,0.5)")));
        Assert.Equal(5.0, Num(Eval("=PERCENTILE.EXC(A1,0.5)")));

        Assert.Equal(ErrorValue.DivByZero, Eval("=CORREL(5,5)"));
        Assert.Equal(ErrorValue.DivByZero, Eval("=CORREL(A1,B1)"));
        Assert.Equal(ErrorValue.DivByZero, Eval("=FORECAST.LINEAR(8,2,4)"));
        Assert.Equal(ErrorValue.DivByZero, Eval("=FORECAST.LINEAR(8,A1,B1)"));
    }
}
