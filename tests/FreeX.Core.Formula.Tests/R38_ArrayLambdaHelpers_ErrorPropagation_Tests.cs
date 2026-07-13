using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R38-formula-array-lambda-helpers-1: MAP/BYROW/BYCOL/REDUCE/SCAN/MAKEARRAY must propagate a
/// real error (e.g. #NAME? from an undefined LAMBDA name) surfacing in the lambda-argument slot,
/// rather than masking it with the generic "not a lambda" #VALUE! error. Real Excel surfaces the
/// underlying error (e.g. #NAME? for an undefined name) in this situation.
/// </summary>
public class R38_ArrayLambdaHelpers_ErrorPropagation_Tests
{
    private readonly FormulaEvaluator _eval = new();
    private readonly Sheet _sheet = new(SheetId.New(), "Sheet1");

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, _sheet);

    private void Set(int row, int col, ScalarValue val) =>
        _sheet.SetCell(new CellAddress(_sheet.Id, (uint)row, (uint)col), val);

    private static double Num(ScalarValue v) => ((NumberValue)v).Value;
    private static RangeValue Rv(ScalarValue v) => (RangeValue)v;

    [Fact]
    public void Map_UndefinedLambdaName_ReturnsNameErrorNotValueError()
    {
        Set(1, 1, new NumberValue(1));
        Set(2, 1, new NumberValue(2));
        Set(3, 1, new NumberValue(3));

        // Doubler is not a defined name/lambda anywhere → real Excel surfaces #NAME?, not #VALUE!.
        var result = Eval("=MAP(A1:A3, Doubler)");
        Assert.Equal(ErrorValue.Name, result);
    }

    [Fact]
    public void Map_ValidLambda_StillWorks()
    {
        Set(1, 1, new NumberValue(1));
        Set(2, 1, new NumberValue(2));
        Set(3, 1, new NumberValue(3));

        var result = Rv(Eval("=LET(Doubler, LAMBDA(x, x*2), MAP(A1:A3, Doubler))"));
        Assert.Equal(2.0, Num(result.At(1, 1)));
        Assert.Equal(4.0, Num(result.At(2, 1)));
        Assert.Equal(6.0, Num(result.At(3, 1)));
    }

    [Fact]
    public void ByRow_UndefinedLambdaName_ReturnsNameErrorNotValueError()
    {
        Set(1, 1, new NumberValue(1));
        Set(1, 2, new NumberValue(2));

        var result = Eval("=BYROW(A1:B1, Summer)");
        Assert.Equal(ErrorValue.Name, result);
    }

    [Fact]
    public void ByRow_ValidLambda_StillWorks()
    {
        Set(1, 1, new NumberValue(1));
        Set(1, 2, new NumberValue(2));

        var result = Rv(Eval("=LET(Summer, LAMBDA(row, SUM(row)), BYROW(A1:B1, Summer))"));
        Assert.Equal(3.0, Num(result.At(1, 1)));
    }

    [Fact]
    public void ByCol_UndefinedLambdaName_ReturnsNameErrorNotValueError()
    {
        Set(1, 1, new NumberValue(1));
        Set(1, 2, new NumberValue(2));

        var result = Eval("=BYCOL(A1:B1, Summer)");
        Assert.Equal(ErrorValue.Name, result);
    }

    [Fact]
    public void ByCol_ValidLambda_StillWorks()
    {
        Set(1, 1, new NumberValue(1));
        Set(1, 2, new NumberValue(2));

        var result = Rv(Eval("=LET(Ident, LAMBDA(col, SUM(col)), BYCOL(A1:B1, Ident))"));
        Assert.Equal(1.0, Num(result.At(1, 1)));
        Assert.Equal(2.0, Num(result.At(1, 2)));
    }

    [Fact]
    public void Reduce_UndefinedLambdaName_ReturnsNameErrorNotValueError()
    {
        Set(1, 1, new NumberValue(1));
        Set(1, 2, new NumberValue(2));

        var result = Eval("=REDUCE(0, A1:B1, Adder)");
        Assert.Equal(ErrorValue.Name, result);
    }

    [Fact]
    public void Reduce_ValidLambda_StillWorks()
    {
        Set(1, 1, new NumberValue(1));
        Set(1, 2, new NumberValue(2));

        var result = Eval("=LET(Adder, LAMBDA(acc, x, acc+x), REDUCE(0, A1:B1, Adder))");
        Assert.Equal(3.0, Num(result));
    }

    [Fact]
    public void Scan_UndefinedLambdaName_ReturnsNameErrorNotValueError()
    {
        Set(1, 1, new NumberValue(1));
        Set(1, 2, new NumberValue(2));

        var result = Eval("=SCAN(0, A1:B1, Adder)");
        Assert.Equal(ErrorValue.Name, result);
    }

    [Fact]
    public void Scan_ValidLambda_StillWorks()
    {
        Set(1, 1, new NumberValue(1));
        Set(1, 2, new NumberValue(2));

        var result = Rv(Eval("=LET(Adder, LAMBDA(acc, x, acc+x), SCAN(0, A1:B1, Adder))"));
        Assert.Equal(1.0, Num(result.At(1, 1)));
        Assert.Equal(3.0, Num(result.At(1, 2)));
    }

    [Fact]
    public void MakeArray_UndefinedLambdaName_ReturnsNameErrorNotValueError()
    {
        var result = Eval("=MAKEARRAY(2, 2, Filler)");
        Assert.Equal(ErrorValue.Name, result);
    }

    [Fact]
    public void MakeArray_ValidLambda_StillWorks()
    {
        var result = Rv(Eval("=LET(Filler, LAMBDA(rowNum, colNum, rowNum+colNum), MAKEARRAY(2, 2, Filler))"));
        Assert.Equal(2.0, Num(result.At(1, 1)));
        Assert.Equal(4.0, Num(result.At(2, 2)));
    }
}
