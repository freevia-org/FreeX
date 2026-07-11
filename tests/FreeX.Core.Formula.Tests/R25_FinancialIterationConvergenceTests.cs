using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-25 fixes for IRR/XIRR iteration convergence:
///
/// R25-financial-iteration-1: IRR (and XIRR) used to return a non-converged, non-root value
/// with no residual check whenever Newton exhausted its iteration cap without ever hitting a
/// genuine convergence break — now tracked via a `converged` flag and rejected as #NUM!.
///
/// R25-financial-iteration-2: IRR's Newton-diverge handling used to fall back to an unbounded,
/// guess-blind bisection over (-0.99999, 10) that could silently substitute an unrelated root
/// for a multi-root cash flow — the bisection fallback has been removed; Newton divergence now
/// returns #NUM!, matching Excel's documented "can't find a result after N tries -> #NUM!".
///
/// R25-financial-iteration-3: XIRR never validated guess > -1 (unlike IRR's existing guard) —
/// an invalid guess like -2 now returns #NUM! immediately instead of silently "succeeding" via
/// the (now-removed) bisection fallback.
/// </summary>
public sealed class R25_FinancialIterationConvergenceTests
{
    private readonly FormulaEvaluator _eval = new();

    private Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    // -------------------------------------------------------------------------
    // R25-financial-iteration-1: non-converged Newton exhaustion must return #NUM!,
    // never a fabricated/garbage rate.
    // -------------------------------------------------------------------------

    [Fact]
    public void Irr_NewtonNeverConvergesWithinIterationCap_ReturnsNumNotGarbageRate()
    {
        // IRR({-4995,4580,1666,-1994}, 0.1): Newton oscillates chaotically for the full
        // 100-iteration budget without ever satisfying |f|<1e-10 or |delta|<1e-10, and never
        // hits r<=-1 or df~0 either. Before the fix, the loop fell out with whatever finite r
        // the 100th iteration produced (~-0.2923, NPV residual ~-822.54 -- nowhere near a
        // root) and returned it directly as if it were the true IRR. Excel returns #NUM! when
        // it "can't find a result that works after 20 tries" -- it never fabricates a rate.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-4995)),
            (2, 1, new NumberValue(4580)),
            (3, 1, new NumberValue(1666)),
            (4, 1, new NumberValue(-1994)));

        _eval.Evaluate("=IRR(A1:A4,0.1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Irr_OrdinaryConvergingInvestment_StillReturnsRate_NoRegression()
    {
        // Sibling/opposite case: a normal investment pattern where Newton genuinely converges
        // within a handful of iterations must still return the correct rate, not #NUM!.
        // IRR({-1000,300,400,500}) ~ 0.0890 (existing FunctionLibraryTests expectation).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1000)),
            (2, 1, new NumberValue(300)),
            (3, 1, new NumberValue(400)),
            (4, 1, new NumberValue(500)));

        var result = (NumberValue)_eval.Evaluate("=IRR(A1:A4)", sheet);
        result.Value.Should().BeApproximately(0.0890, 0.001);
    }

    // -------------------------------------------------------------------------
    // R25-financial-iteration-2: Newton-diverge (r <= -1) must return #NUM!, never an
    // unrelated root silently substituted by a global, guess-blind bisection.
    // -------------------------------------------------------------------------

    [Fact]
    public void Irr_MultiRootCashFlow_GuessOvershootsPastMinusOne_ReturnsNumNotUnrelatedRoot()
    {
        // {-1000,3900,-5030,2145} has exact roots at 10%, 30%, and 50%. With guess=0.172, the
        // very first Newton step overshoots to r<=-1. Before the fix, the code fell back to an
        // unbounded bisection over (-0.99999,10) and returned 0.50 -- a root completely
        // unrelated to what a 0.172 guess was "aiming" at (the 10%-30% region). Real Excel's
        // Newton-only algorithm does not silently substitute a distant, unrelated root here.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1000)),
            (2, 1, new NumberValue(3900)),
            (3, 1, new NumberValue(-5030)),
            (4, 1, new NumberValue(2145)));

        _eval.Evaluate("=IRR(A1:A4,0.172)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Irr_MultiRootCashFlow_SecondOvershootGuess_AlsoReturnsNum_NoRegressionToOldBisectionRoot()
    {
        // Same cash flow, guess=0.405 (aimed near the 30%-40% region): the old bisection
        // fallback silently jumped to the unrelated 50% root here too. Must now be #NUM!.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1000)),
            (2, 1, new NumberValue(3900)),
            (3, 1, new NumberValue(-5030)),
            (4, 1, new NumberValue(2145)));

        _eval.Evaluate("=IRR(A1:A4,0.405)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Irr_MultiRootCashFlow_GuessThatConvergesCleanly_StillReturnsNearestRoot_NoRegression()
    {
        // Sibling/opposite case: same three-root cash flow, but guess=0.17 (very close to
        // 0.172) converges cleanly via pure Newton -- without ever needing any fallback -- to
        // the 10% root, exactly matching what Excel's guess-sensitive Newton solver would find.
        // This proves removing the bisection fallback does not regress legitimately converging
        // guesses on the very same pathological (multi-root) cash flow.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1000)),
            (2, 1, new NumberValue(3900)),
            (3, 1, new NumberValue(-5030)),
            (4, 1, new NumberValue(2145)));

        var result = (NumberValue)_eval.Evaluate("=IRR(A1:A4,0.17)", sheet);
        result.Value.Should().BeApproximately(0.10, 0.0001);
    }

    // -------------------------------------------------------------------------
    // R25-financial-iteration-3: XIRR must validate guess > -1, matching IRR's existing guard.
    // -------------------------------------------------------------------------

    [Fact]
    public void Xirr_GuessAtOrBelowMinusOne_ReturnsNumError()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1000)),
            (2, 1, new NumberValue(3900)),
            (3, 1, new NumberValue(-5030)),
            (4, 1, new NumberValue(2145)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(366)),
            (3, 2, new NumberValue(731)),
            (4, 2, new NumberValue(1096)));

        _eval.Evaluate("=XIRR(A1:A4,B1:B4,-2)", sheet).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=XIRR(A1:A4,B1:B4,-1)", sheet).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Xirr_OrdinaryConvergingGuess_StillReturnsRate_NoRegression()
    {
        // Sibling/opposite case: a valid, in-range guess on a normal (single-root) mixed
        // cash flow must still converge to a finite rate, not #NUM!.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-1000)),
            (2, 1, new NumberValue(1100)),
            (1, 2, new NumberValue(1)),
            (2, 2, new NumberValue(366)));

        var result = _eval.Evaluate("=XIRR(A1:A2,B1:B2,0.05)", sheet);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.1, 0.005);
    }
}
