using System.Diagnostics;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R71-formula-statistical-dist-4-1/2/3: three discrete-distribution helpers in
/// BuiltInFunctions.StatisticalDistributions.Discrete.cs did an O(n) (or worse) term-by-term
/// walk that freezes the calc thread for huge trials/population parameters, even though Excel
/// itself returns instantly. Fixed by routing all three through the already-present O(1)
/// closed-form BinomCdf (via BetaInc), plus a normal-approximation fallback for HYPGEOM.DIST's
/// cumulative branch when the actual support span is astronomically large:
///  - BINOM.INV: linear accumulation over k=0..n replaced with a binary search over
///    BinomCdf(k,n,p) (monotone in k), so BINOM.INV(2000000000,0.5,0.9) is O(log n).
///  - BINOM.DIST.RANGE: sum over k=k1..k2 replaced with BinomCdf(k2)-BinomCdf(k1-1).
///  - HYPGEOM.DIST(...,TRUE): term-by-term PMF sum over the actual support is kept for normal
///    inputs, but capped at BuiltInFunctions.MaxHypergeomCdfTerms terms; beyond that, falls back
///    to the continuity-corrected normal approximation to the hypergeometric CDF.
/// </summary>
public sealed class Round71FmlStatDistributionTests
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

    // ── BINOM.INV (R71-formula-statistical-dist-4-1) ────────────────────────────

    [Fact]
    public void BinomInv_SmallN_MatchesExcel()
    {
        // BINOM.INV(20,0.5,0.75) = 12: exact fractions (out of C(20,k) summing to 2^20=1048576)
        // give CDF(11)=784626/1048576=0.748... < 0.75 and CDF(12)=910596/1048576=0.868... >= 0.75,
        // so 12 is the smallest k with CDF(k) >= 0.75 -- matches Excel.
        Calc("BINOM.INV(20,0.5,0.75)").Should().Be(12.0);
    }

    [Fact]
    public void BinomInv_SmallN_MatchesOldAccumulation_NoRegression()
    {
        // Pin the pre-existing small-n case (already covered elsewhere) so the binary-search
        // rewrite agrees with the original term-by-term accumulation exactly.
        Calc("BINOM.INV(10,0.5,0.75)").Should().Be(6.0);
    }

    [Fact]
    public void BinomInv_AlphaZero_ReturnsZero()
        => Calc("BINOM.INV(20,0.5,0)").Should().Be(0.0);

    [Fact]
    public void BinomInv_AlphaOne_ReturnsN()
        => Calc("BINOM.INV(20,0.5,1)").Should().Be(20.0);

    [Fact]
    public void BinomInv_HugeN_ReturnsPromptlyWithCorrectQuantile()
    {
        // Before the fix this walked ~1e9 PMF terms one at a time (each a LogGamma-based
        // Math.Exp/Math.Log computation) and would not return in any reasonable test timeout.
        // The binary search over the O(1) BinomCdf must return near-instantly.
        var sw = Stopwatch.StartNew();
        double result = Calc("BINOM.INV(2000000000,0.5,0.9)");
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(3000, "BINOM.INV must be O(log n), not O(n), for huge n");

        // Verify it's the correct quantile: smallest k with CDF(k) >= 0.9 and CDF(k-1) < 0.9.
        double cdfAtK = Calc($"BINOM.DIST({result},2000000000,0.5,TRUE)");
        double cdfBelowK = Calc($"BINOM.DIST({result - 1},2000000000,0.5,TRUE)");
        cdfAtK.Should().BeGreaterThanOrEqualTo(0.9);
        cdfBelowK.Should().BeLessThan(0.9);
    }

    // ── BINOM.DIST.RANGE (R71-formula-statistical-dist-4-3) ─────────────────────

    [Fact]
    public void BinomDistRange_WideRange_MatchesOldTermByTermSum()
    {
        // Independently recompute the old O(range) sum in the test (via BINOM.DIST PMF calls)
        // and confirm the new CDF-difference formula agrees to high precision.
        double expected = 0;
        for (int k = 20; k <= 30; k++) expected += Calc($"BINOM.DIST({k},60,0.5,FALSE)");

        Calc("BINOM.DIST.RANGE(60,0.5,20,30)").Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void BinomDistRange_SinglePoint_MatchesBinomDistPmf_NoRegression()
    {
        double point = Calc("BINOM.DIST(3,10,0.5,FALSE)");
        Calc("BINOM.DIST.RANGE(10,0.5,3,3)").Should().BeApproximately(point, 1e-10);
    }

    [Fact]
    public void BinomDistRange_HugeN_WholeRange_ReturnsPromptly()
    {
        var sw = Stopwatch.StartNew();
        double result = Calc("BINOM.DIST.RANGE(2000000000,0.5,0,2000000000)");
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(3000, "BINOM.DIST.RANGE must be O(1) via BinomCdf, not O(range)");
        result.Should().BeApproximately(1.0, 1e-9);
    }

    // ── HYPGEOM.DIST cumulative (R71-formula-statistical-dist-4-2) ──────────────

    [Fact]
    public void HypergeomDist_Cumulative_SmallCase_MatchesExcel()
    {
        // HYPGEOM.DIST(1,4,8,20,TRUE): P(X<=1) drawing 4 from a population of 20 with 8
        // successes = [C(8,0)C(12,4) + C(8,1)C(12,3)] / C(20,4) = (495+1760)/4845 = 451/969.
        Calc("HYPGEOM.DIST(1,4,8,20,TRUE)").Should().BeApproximately(451.0 / 969.0, 1e-9);
    }

    [Fact]
    public void HypergeomDist_Pmf_Unaffected_NoRegression()
    {
        // The cumulative=FALSE path must be completely untouched by the cumulative-branch fix.
        Calc("HYPGEOM.DIST(1,4,2,10,FALSE)").Should().BeApproximately(0.5333333333, 1e-6);
    }

    [Fact]
    public void HypergeomDist_HugeParameters_ReturnsPromptlyViaNormalApproximation()
    {
        // Population/sample sizes large enough that the actual support span (~5e8 terms) would
        // freeze the calc thread if summed term-by-term. s is set at the distribution's mean, so
        // the (continuity-corrected) normal-approximation CDF should land very close to 0.5.
        var sw = Stopwatch.StartNew();
        double result = Calc("HYPGEOM.DIST(500000000,1000000000,1000000000,2000000000,TRUE)");
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(3000, "the huge-support case must not sum every PMF term");
        result.Should().BeApproximately(0.5, 0.01);
    }
}
