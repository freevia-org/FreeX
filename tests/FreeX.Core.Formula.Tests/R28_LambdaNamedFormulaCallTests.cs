using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R28-lambda-helpers-deep-1: calling a workbook/sheet-defined Name Manager name that holds a
/// LAMBDA (e.g. Name Manager &gt; New Name &gt; "FACT" refers-to =LAMBDA(...)) must work exactly
/// like calling any other function — real Excel's standard "custom function via Name Manager"
/// pattern. Previously EvaluateFunction only ever consulted the LET-scope lambda-binding chain
/// for a callable name and fell straight to #NAME? for any name not found there, even when the
/// name genuinely resolved to a LAMBDA via the workbook/sheet Name Manager.
/// </summary>
public class R28_LambdaNamedFormulaCallTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void NamedFormulaLambda_SimpleCall_Invokes()
    {
        // DOUBLE -> LAMBDA(x, x*2), Name Manager style (no LET involved at all).
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["DOUBLE"] = "LAMBDA(x,x*2)";

        var result = _evaluator.Evaluate("=DOUBLE(21)", sheet, workbook);

        result.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void NamedFormulaLambda_RecursiveFactorial_ViaNameManager_ReturnsCorrectValue()
    {
        // The exact bug-report scenario: FACT -> LAMBDA(n, IF(n<=1,1,n*FACT(n-1))), called
        // as a plain cell formula =FACT(5). Real Excel returns 120.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["FACT"] = "LAMBDA(n,IF(n<=1,1,n*FACT(n-1)))";

        var result = _evaluator.Evaluate("=FACT(5)", sheet, workbook);

        result.Should().Be(new NumberValue(120));
    }

    [Fact]
    public void NamedFormulaLambda_SheetScoped_Invokes()
    {
        // Sheet-scoped Name Manager lambda (DefineNamedFormula with a scope sheet id) must also
        // be callable, not just workbook-global names.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.DefineNamedFormula("TRIPLE", "LAMBDA(x,x*3)", sheet.Id);

        var result = _evaluator.Evaluate("=TRIPLE(4)", sheet, workbook);

        result.Should().Be(new NumberValue(12));
    }

    [Fact]
    public void UnknownFunctionName_StillReturnsNameError()
    {
        // Sibling already-working case: a genuinely-undefined identifier used with call syntax
        // must still be #NAME? — the fallback must not swallow real "unknown function" errors.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=TOTALLYUNDEFINEDFUNC(5)", sheet, workbook);

        result.Should().Be(ErrorValue.Name);
    }

    [Fact]
    public void NonLambdaNamedFormula_CalledWithParens_StillReturnsNameError()
    {
        // Sibling already-working case: a formula-backed name that is NOT a LAMBDA must not be
        // invoked with call syntax just because it happens to resolve via TryEvaluateNamedFormula
        // — only LAMBDA-valued names are callable. Guards against over-correcting the fallback
        // into a generic "call any named formula" path.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["MyConst"] = "10";

        var result = _evaluator.Evaluate("=MyConst(5)", sheet, workbook);

        result.Should().Be(ErrorValue.Name);
    }

    [Fact]
    public void NonLambdaNamedFormula_BareReference_StillEvaluatesNormally()
    {
        // Sibling already-working case: the existing bare-name (no call syntax) path for a
        // scalar formula-backed name must be untouched by this fix.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.NamedFormulas["MyConst"] = "10";

        var result = _evaluator.Evaluate("=MyConst*2", sheet, workbook);

        result.Should().Be(new NumberValue(20));
    }
}
