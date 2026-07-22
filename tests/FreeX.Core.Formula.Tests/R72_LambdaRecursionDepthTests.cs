using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R72-formula-lambda-let-4-1: the shared MaxEvalDepth AST-node-depth guard
/// (FormulaEvaluator.cs) used to be 256, which cuts off an ordinary self-recursive LAMBDA at
/// only ~85-90 real recursion levels (each recursive call consumes ~3 EvaluateNode levels: the
/// FunctionCallNode invocation, the IF condition, and the arithmetic on the recursive result) —
/// well short of what Excel itself supports. MaxEvalDepth was raised to 1024 (~340 real levels)
/// so ordinary recursive LAMBDA formulas to ~200+ levels now compute correctly, while a
/// genuinely infinite recursive LAMBDA still returns #NUM! rather than crashing the process.
/// </summary>
public sealed class R72_LambdaRecursionDepthTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void RecursiveLambda_90Levels_ComputesCorrectSum()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        // f(n) = n + (n-1) + ... + 1 + 0 = n*(n+1)/2. f(90) = 90*91/2 = 4095... wait, check formula.
        var result = _evaluator.Evaluate(
            "=LET(f, LAMBDA(n, IF(n=0,0,n+f(n-1))), f(90))",
            sheet, wb);

        result.Should().Be(new NumberValue(90 * 91 / 2d));
    }

    [Fact]
    public void RecursiveLambda_150Levels_ComputesCorrectSum()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        // Previously (MaxEvalDepth = 256) this cut off around ~85 real recursion levels and
        // returned #NUM!; with the raised cap, 150 levels of ordinary recursion succeed.
        var result = _evaluator.Evaluate(
            "=LET(f, LAMBDA(n, IF(n=0,0,n+f(n-1))), f(150))",
            sheet, wb);

        result.Should().Be(new NumberValue(150 * 151 / 2d));
    }

    [Fact]
    public void InfiniteRecursiveLambda_StillReturnsNumError_NotStackOverflow()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        // A genuinely infinite recursive LAMBDA must still be cut off gracefully by the depth
        // guard (raising MaxEvalDepth must not remove the backstop, only raise its ceiling).
        var result = _evaluator.Evaluate(
            "=LET(f, LAMBDA(n, f(n)), f(1))",
            sheet, wb);

        result.Should().Be(ErrorValue.Num,
            "an infinite recursive LAMBDA must return #NUM! rather than crashing the process");
    }

    [Fact]
    public void NonRecursive_DeepArithmeticFormula_StillEvaluatesNormally()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        // Sibling no-regression check: an ordinary (non-recursive) formula is unaffected by the
        // raised depth cap.
        var result = _evaluator.Evaluate("=1+2*3-4/2+5", sheet, wb);

        result.Should().Be(new NumberValue(1 + 2 * 3 - 4 / 2d + 5));
    }
}
