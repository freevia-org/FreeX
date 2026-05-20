using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Formula.Tests;

public class PercentRankScalarArrayTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        return sheet;
    }

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, MakeSheet());

    private static double Num(ScalarValue value) => ((NumberValue)value).Value;

    [Fact]
    public void PercentRankInc_TreatsScalarArrayAsSingleCellArray()
    {
        Assert.Equal(1.0, Num(Eval("=PERCENTRANK.INC(5,5)")));
        Assert.Equal(1.0, Num(Eval("=PERCENTRANK(A1,5)")));
        Assert.Equal(ErrorValue.NA, Eval("=PERCENTRANK.INC(5,4)"));
        Assert.Equal(ErrorValue.NA, Eval("=PERCENTRANK.INC(5,6)"));
    }
}
