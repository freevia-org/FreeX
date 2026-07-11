using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R26-statistical-distribution-precision-1: BINOM.DIST at the p=0/k=0 (and p=1/k=n) boundary
/// used to return #NUM! instead of 1. BinomPmf computed k*Math.Log(p) and (n-k)*Math.Log(1-p),
/// and at the degenerate boundary one of those terms is 0*Math.Log(0) = 0*(-Infinity) = NaN,
/// which NumberResult then turned into #NUM!. Real Excel treats p=0 and p=1 as degenerate
/// distributions (all mass on k=0 or k=n respectively) and returns 1/0 accordingly, matching
/// this codebase's own working ordinary-probability path. Fixed in
/// BuiltInFunctions.StatisticalDistributions.Discrete.cs by special-casing p==0 and p==1 in
/// BinomPmf before the log-based computation runs.
///
/// BinomCdf (the TRUE/cumulative form) was already correct at these boundaries because BetaInc
/// explicitly special-cases x==0 and x==1 before taking any logarithm -- those cases are covered
/// below purely to pin the no-regression contract, not because they needed a fix.
/// </summary>
public sealed class Round26BinomDistBoundaryTests
{
    private readonly FormulaEvaluator _eval = new();

    private double Calc(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        var result = _eval.Evaluate("=" + formula, sheet, wb);
        result.Should().BeOfType<NumberValue>($"formula {formula} should return a number");
        return ((NumberValue)result).Value;
    }

    [Fact]
    public void BinomDist_Pmf_ZeroProbability_ZeroSuccesses_ReturnsOne()
    {
        // Bug case: p=0, k=0 -- the only attainable outcome, so P(X=0)=1.
        Calc("BINOM.DIST(0,5,0,FALSE)").Should().Be(1.0);
    }

    [Fact]
    public void BinomDist_Pmf_FullProbability_AllSuccesses_ReturnsOne()
    {
        // Bug case: p=1, k=n -- the only attainable outcome, so P(X=n)=1.
        Calc("BINOM.DIST(5,5,1,FALSE)").Should().Be(1.0);
    }

    [Fact]
    public void BinomDist_Pmf_ZeroProbability_NonzeroSuccesses_ReturnsZero()
    {
        // Sibling boundary case: p=0 but k != 0 is unattainable, so P(X=k)=0 (not NaN/#NUM!).
        Calc("BINOM.DIST(3,5,0,FALSE)").Should().Be(0.0);
    }

    [Fact]
    public void BinomDist_Pmf_FullProbability_NotAllSuccesses_ReturnsZero()
    {
        // Sibling boundary case: p=1 but k != n is unattainable, so P(X=k)=0 (not NaN/#NUM!).
        Calc("BINOM.DIST(3,5,1,FALSE)").Should().Be(0.0);
    }

    [Fact]
    public void BinomDist_Pmf_OrdinaryProbability_StillWorks_NoRegression()
    {
        // Already-working ordinary case (0 < p < 1) must be unaffected by the boundary guard.
        // BINOM.DIST(6,10,0.5,FALSE) = C(10,6) * 0.5^10
        Calc("BINOM.DIST(6,10,0.5,FALSE)").Should().BeApproximately(0.2050781250, 1e-8);
    }

    [Fact]
    public void BinomDist_Cumulative_ZeroProbability_ReturnsOne_NoRegression()
    {
        // The cumulative form already handled this boundary correctly via BetaInc's explicit
        // x==1 special case; pin it so the PMF fix doesn't regress it.
        Calc("BINOM.DIST(0,5,0,TRUE)").Should().Be(1.0);
    }

    [Fact]
    public void BinomDist_Cumulative_FullProbability_ReturnsOne_NoRegression()
    {
        // BinomCdf short-circuits k>=n to 1 before touching BetaInc at all.
        Calc("BINOM.DIST(5,5,1,TRUE)").Should().Be(1.0);
    }

    [Fact]
    public void BinomDistRange_ZeroProbability_SumsToOneAtOnlyAttainableOutcome()
    {
        // BINOM.DIST.RANGE sums BinomPmf over a k-range; with p=0 the only nonzero term is k=0,
        // so a range spanning k=0..2 should sum to exactly 1 (previously NaN -> #NUM!).
        Calc("BINOM.DIST.RANGE(5,0,0,2)").Should().Be(1.0);
    }
}
