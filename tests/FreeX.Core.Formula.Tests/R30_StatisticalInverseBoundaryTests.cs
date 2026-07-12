using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R30-formula-statistical-inverse-1: F.INV and F.INV.RT wrongly rejected the valid boundary
/// probability (0 or 1). The F-distribution's support starts at 0, exactly analogous to
/// GAMMA.INV(0,a,b)=0 and CHISQ.INV.RT(1,df)=0, both of which already worked. FInvScalar's guard
/// `prob &lt;= 0` (excluding prob==0) and FInvRtScalar's guard `prob &gt;= 1` (excluding prob==1)
/// were stricter than the sibling ChiSqInv guards. Fixed in
/// BuiltInFunctions.StatisticalDistributions.FChiSquare.cs by relaxing both guards to match the
/// ChiSqInv/ChiSqInvRt boundary convention (prob &lt; 0 / prob &gt; 1 are the real errors).
///
/// R30-formula-statistical-inverse-2: TInv/FInv's bisection search windows were hard-fixed at
/// +-1e9 (T.INV) / [0, 1e9] (F.INV), silently clamping instead of erroring for heavy-tailed
/// low-df extreme quantiles (e.g. T.INV(1E-12,1) should be about -3.18e11, the exact Cauchy
/// closed form tan(pi*(p-0.5)), but the fixed window clamped the result to about -1e9). Fixed in
/// BuiltInFunctions.StatisticalDistributions.Numerical.cs (TInv/FInv) by expanding whichever
/// bound doesn't yet bracket the target probability before bisecting.
/// </summary>
public sealed class R30_StatisticalInverseBoundaryTests
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

    // ── R30-formula-statistical-inverse-1: F.INV / F.INV.RT boundary ─────────

    [Fact]
    public void FInv_AtProbabilityZero_ReturnsZero()
    {
        // Bug case: F.INV(0,5,10) should return 0 (F support starts at 0), previously #NUM!.
        Calc("F.INV(0,5,10)").Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void FInvRt_AtProbabilityOne_ReturnsZero()
    {
        // Bug case: F.INV.RT(1,5,10) should return 0 (100% right-tail maps to x=0), previously #NUM!.
        Calc("F.INV.RT(1,5,10)").Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void FInv_AtProbabilityOne_StillReturnsNumError_NoRegression()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        _eval.Evaluate("=F.INV(1,5,10)", sheet, wb).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void FInv_AtNegativeProbability_StillReturnsNumError_NoRegression()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        _eval.Evaluate("=F.INV(-0.1,5,10)", sheet, wb).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void FInvRt_AtProbabilityZero_StillReturnsNumError_NoRegression()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        _eval.Evaluate("=F.INV.RT(0,5,10)", sheet, wb).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void FInv_OrdinaryProbability_StillWorks_NoRegression()
    {
        // Already-working interior case: F.INV(0.5,5,10) should be close to the F-distribution
        // median for these degrees of freedom (comparable to F.DIST cumulative round-trip).
        double x = Calc("F.INV(0.5,5,10)");
        double roundTrip = Calc($"F.DIST({x},5,10,TRUE)");
        roundTrip.Should().BeApproximately(0.5, 1e-6);
    }

    // ── R30-formula-statistical-inverse-2: T.INV / F.INV extreme quantiles ───

    [Fact]
    public void TInv_ExtremeSmallProbability_LowDf_MatchesCauchyClosedForm()
    {
        // T.INV with df=1 is the standard Cauchy distribution: exact quantile is tan(pi*(p-0.5)).
        double p = 1e-12;
        double expected = Math.Tan(Math.PI * (p - 0.5));
        double actual = Calc($"T.INV({p},1)");
        // Bug case: pre-fix this clamped to approximately -1e9 (relative error ~99.7%) instead of
        // approximately -3.18e11. Post-fix relative error is on the order of 1e-5 (limited by the
        // numerical precision of the underlying incomplete-beta CDF at this extreme magnitude),
        // still a >10,000x improvement over the pre-fix clamped result.
        (Math.Abs(actual - expected) / Math.Abs(expected)).Should().BeLessThan(1e-4);
    }

    [Fact]
    public void TInv_OrdinaryProbability_StillWorks_NoRegression()
    {
        // Already-working normal-range case: T.INV(0.975,10) is the familiar two-tailed 95% CI
        // t-critical value for df=10, approximately 2.228.
        Calc("T.INV(0.975,10)").Should().BeApproximately(2.228138852, 1e-6);
    }

    [Fact]
    public void FInv_ExtremeHighProbability_SmallDenominatorDf_ExceedsOriginalWindowBound()
    {
        // F-distribution with a small denominator df (d2=1) has a heavy right tail that decays
        // like x^(-1/2), so a very-high-probability quantile lies far beyond the original fixed
        // hi=1e9 bisection bound. Verify the result round-trips through F.DIST accurately instead
        // of clamping at the old window edge.
        double p = 1.0 - 1e-9;
        double x = Calc($"F.INV({p},5,1)");
        x.Should().BeGreaterThan(1e9);
        double roundTrip = Calc($"F.DIST({x},5,1,TRUE)");
        roundTrip.Should().BeApproximately(p, 1e-6);
    }

    [Fact]
    public void FInv_OrdinaryHighProbability_StillWorks_NoRegression()
    {
        // Already-working case comfortably inside the original window: F.INV(0.95,5,10).
        double x = Calc("F.INV(0.95,5,10)");
        double roundTrip = Calc($"F.DIST({x},5,10,TRUE)");
        roundTrip.Should().BeApproximately(0.95, 1e-6);
    }
}
