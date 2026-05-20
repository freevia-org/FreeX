using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Formula.Tests;

public class RangeArgumentScalarArrayTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(0.1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new NumberValue(45000));
        return sheet;
    }

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, MakeSheet());

    private static double Num(ScalarValue value) => ((NumberValue)value).Value;

    private static RangeValue Rv(ScalarValue value) => (RangeValue)value;

    [Fact]
    public void RangeArgumentFunctions_TreatScalarArraysAsSingleCellArrays()
    {
        Assert.Equal(110.0, Num(Eval("=FVSCHEDULE(100,0.1)")), 12);
        Assert.Equal(110.0, Num(Eval("=FVSCHEDULE(100,A1)")), 12);
        Assert.Equal(2.0, Num(Eval("=SERIESSUM(1,0,1,2)")));
        Assert.Equal(2.0, Num(Eval("=SERIESSUM(1,0,1,B1)")));
        Assert.Equal(100.0, Num(Eval("=XNPV(0.1,100,45000)")));
        Assert.Equal(100.0, Num(Eval("=XNPV(A1,C1,D1)")));

        Assert.Equal(6.0, Num(Rv(Eval("=MMULT(2,3)")).At(1, 1)));
        Assert.Equal(2.0, Num(Eval("=MDETERM(2)")));
        Assert.Equal(0.5, Num(Rv(Eval("=MINVERSE(2)")).At(1, 1)));
    }
}
