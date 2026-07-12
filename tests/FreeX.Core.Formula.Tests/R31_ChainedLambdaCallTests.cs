using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R31-formula-logical-lambda-2: immediate/chained invocation of a call/lambda RESULT
/// (e.g. LAMBDA(x,x+1)(5), or the curried mk(5)(3)) was entirely unparseable — ParsePostfix
/// never recognized a trailing '(' after an already-parsed primary/postfix expression, so the
/// whole formula threw a FormulaParseException that FormulaEvaluator.Evaluate silently mapped
/// to #VALUE!. Fixed by desugaring `expr(args)` into a synthetic
/// `LET(__callN, expr, __callN(args))`, reusing the existing LET-scoped lambda-binding call path
/// (no new AST node or evaluator support needed).
/// </summary>
public class R31_ChainedLambdaCallTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void ImmediatelyInvokedLambdaLiteral_ReturnsCalculatedResult()
    {
        // The bug-report headline case straight from Microsoft's own LAMBDA docs.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=LAMBDA(x,x+1)(5)", sheet, workbook);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void CurriedLambdaCall_ChainedInvocation_ReturnsCalculatedResult()
    {
        // mk(5) returns a LAMBDA(b, a+b) closure; calling that result again with (3) must work
        // the same way LET-name-mediated calling already does.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate(
            "=LET(mk,LAMBDA(a,LAMBDA(b,a+b)),mk(5)(3))", sheet, workbook);

        result.Should().Be(new NumberValue(8));
    }

    [Fact]
    public void LetNameMediatedLambdaCall_SiblingAlreadyWorkingCase_StillWorks()
    {
        // Sibling already-working case this fix must not disturb: binding the intermediate
        // lambda value to a LET name first, then calling *that* name, was already correct.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=LET(f,LAMBDA(x,x*2),f(5))", sheet, workbook);

        result.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void LetNameMediatedCurriedCall_SiblingAlreadyWorkingCase_StillWorks()
    {
        // Sibling already-working case: binding BOTH the outer call result (inner) and the
        // curried maker (mk) to LET names, then calling the bound name, must keep working.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate(
            "=LET(mk,LAMBDA(a,LAMBDA(b,a+b)),inner,mk(5),inner(3))", sheet, workbook);

        result.Should().Be(new NumberValue(8));
    }

    [Fact]
    public void ChainedCallOnNonLambdaResult_ReturnsValueError()
    {
        // A chained call on something that isn't actually a lambda must still error, not crash
        // or silently coerce — matches what happened (via the parse-exception fallback) before
        // this feature existed.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=SUM(1,2)(3)", sheet, workbook);

        result.Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void PlainFunctionCall_SiblingAlreadyWorkingCase_Unaffected()
    {
        // Sibling already-working case: an ordinary (non-chained) function call must be
        // completely unaffected by the new postfix call-chaining branch.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=SUM(1,2,3)", sheet, workbook);

        result.Should().Be(new NumberValue(6));
    }
}
