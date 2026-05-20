using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Formula.Tests;

public class ChiSqScalarArrayTests
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

    [Fact]
    public void ChiSqTest_TreatsScalarArraysAsSingleCellArrays()
    {
        Assert.Equal(ErrorValue.NA, Eval("=CHISQ.TEST(5,5)"));
        Assert.Equal(ErrorValue.NA, Eval("=CHISQ.TEST(A1,B1)"));
    }
}
