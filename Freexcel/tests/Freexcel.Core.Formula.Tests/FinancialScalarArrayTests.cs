using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using Xunit;

namespace Freexcel.Core.Formula.Tests;

public class FinancialScalarArrayTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(-5));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(45000));
        return sheet;
    }

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, MakeSheet());

    [Fact]
    public void FinancialFunctions_TreatScalarArraysAsSingleCellArrays()
    {
        Assert.Equal(ErrorValue.Num, Eval("=IRR(5)"));
        Assert.Equal(ErrorValue.Num, Eval("=IRR(A1)"));
        Assert.Equal(ErrorValue.DivByZero, Eval("=MIRR(5,0.1,0.1)"));
        Assert.Equal(ErrorValue.DivByZero, Eval("=MIRR(A1,0.1,0.1)"));
        Assert.Equal(ErrorValue.NA, Eval("=XIRR(5,45000)"));
        Assert.Equal(ErrorValue.NA, Eval("=XIRR(A1,C1)"));
    }
}
