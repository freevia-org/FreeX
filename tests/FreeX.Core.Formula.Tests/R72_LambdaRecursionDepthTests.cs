using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R72-formula-lambda-let-4-1 / R75 crash-safety follow-up: the shared MaxEvalDepth AST-node-depth
/// guard (FormulaEvaluator.cs) bounds recursion so a genuinely-infinite recursive LAMBDA returns
/// #NUM! rather than crashing the process. Round 72 added a large-stack worker-thread escalation to
/// let ordinary recursive LAMBDA formulas exceed the default ~85-level cap, but round 75 found that
/// escalation could NOT bound a truly-infinite recursion before its worker stack overflowed (a
/// StackOverflowException is uncatchable and terminated the whole process), so the escalation was
/// removed. The default guard now cuts recursion off with #NUM! -- the graceful, Excel-consistent
/// outcome. Shallow recursion still computes; deep/infinite recursion returns #NUM! and never
/// crashes. A stack-SAFE deep-recursion path (bounded by real CLR stack headroom) is deferred.
/// </summary>
public sealed class R72_LambdaRecursionDepthTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void ShallowRecursiveLambda_ComputesCorrectSum()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        // f(n) = n + (n-1) + ... + 1 + 0 = n*(n+1)/2. f(40) recurses ~40 levels (~120 EvaluateNode
        // levels, well within the 256 default depth budget), so it computes correctly.
        var result = _evaluator.Evaluate(
            "=LET(f, LAMBDA(n, IF(n=0,0,n+f(n-1))), f(40))",
            sheet, wb);

        result.Should().Be(new NumberValue(40 * 41 / 2d));
    }

    [Fact]
    public void DeepRecursiveLambda_ReturnsNumError_NotCrashOrWrongValue()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        // f(150) recurses far past the ~85-level (256 EvaluateNode) cut-off, so the depth guard
        // returns #NUM! gracefully. (Round 72 briefly computed this via a worker-thread escalation,
        // but that escalation could crash the process on a truly-infinite recursion and was removed
        // in round 75 -- #NUM! is the safe, Excel-consistent behavior for over-deep recursion.)
        var result = _evaluator.Evaluate(
            "=LET(f, LAMBDA(n, IF(n=0,0,n+f(n-1))), f(150))",
            sheet, wb);

        result.Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void InfiniteRecursiveLambda_ReturnsNumError_NotStackOverflow()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        // A genuinely infinite recursive LAMBDA must be cut off gracefully by the depth guard and
        // return #NUM! -- never a StackOverflowException, which is uncatchable and would crash the
        // whole process (this is exactly what the removed round-72 escalation risked).
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

        // Sibling no-regression check: an ordinary (non-recursive) formula is unaffected.
        var result = _evaluator.Evaluate("=1+2*3-4/2+5", sheet, wb);

        result.Should().Be(new NumberValue(1 + 2 * 3 - 4 / 2d + 5));
    }
}
