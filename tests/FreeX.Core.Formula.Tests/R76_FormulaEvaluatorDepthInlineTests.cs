using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R76-perf-recursion-sweep-1: FormulaEvaluator.RunWithDepthEscalation had become a pure pass-through
/// after round 75 removed its large-stack worker-thread escalation ("_effectiveMaxEvalDepth =
/// MaxEvalDepth; return attempt();"), yet all three public Evaluate() entry points still wrapped
/// their body in a "() => {...}" closure passed to it -- a heap display-class + delegate allocation
/// on every formula-cell evaluation (the recalc hot path) for zero behavioral benefit. The fix
/// inlines "_effectiveMaxEvalDepth = MaxEvalDepth; _evalDepth = 0;" directly at each of the three
/// call sites (Evaluate(string), Evaluate(FormulaNode), EvaluateSpilling) and removes the helper
/// entirely. Behavior must be byte-for-byte identical: a normal formula evaluates unchanged, and a
/// deep/infinite recursive LAMBDA still returns #NUM! via the MaxEvalDepth guard (verified in depth
/// by the sibling R72_LambdaRecursionDepthTests, which must still pass after this change).
/// </summary>
public sealed class R76_FormulaEvaluatorDepthInlineTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Evaluate_StringOverload_NormalFormula_StillComputesCorrectly()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));

        _eval.Evaluate("=A1*2+1", sheet, wb).Should().Be(new NumberValue(21));
    }

    [Fact]
    public void Evaluate_StringOverload_DeepRecursiveLambda_StillReturnsNumError()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        // f(150) recurses far past the depth cap; the inlined guard must still cut it off
        // gracefully with #NUM! rather than any other outcome (crash, wrong value, hang).
        var result = _eval.Evaluate("=LET(f, LAMBDA(n, IF(n=0,0,n+f(n-1))), f(150))", sheet, wb);

        result.Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Evaluate_AstOverload_NormalFormula_StillComputesCorrectly()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var ast = FormulaEvaluator.ParseFormula("=3+4*2");

        _eval.Evaluate(ast, sheet, wb).Should().Be(new NumberValue(11));
    }

    [Fact]
    public void EvaluateSpilling_BareRange_StillSpillsWholeRange()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        var ast = FormulaEvaluator.ParseFormula("=A1:A2");

        var result = _eval.EvaluateSpilling(ast, sheet, wb);

        var rv = result.Should().BeOfType<RangeValue>().Subject;
        rv.RowCount.Should().Be(2);
        rv.Cells[0, 0].Should().Be(new NumberValue(1));
        rv.Cells[1, 0].Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Evaluate_SecondCallOnSameThread_DepthCounterResetsCorrectly()
    {
        // Sibling no-regression: since _evalDepth is now reset inline at the top of every
        // Evaluate() call (rather than inside a closure handed to a helper), a formula that
        // previously tripped the depth guard must not leave stale state that affects the
        // NEXT, unrelated, shallow evaluation on the same thread.
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");

        var deep = _eval.Evaluate("=LET(f, LAMBDA(n, IF(n=0,0,n+f(n-1))), f(150))", sheet, wb);
        deep.Should().Be(ErrorValue.Num);

        var shallow = _eval.Evaluate("=1+1", sheet, wb);
        shallow.Should().Be(new NumberValue(2));
    }
}
