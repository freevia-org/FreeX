using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for R28-financial-functions-deep-2-2/2-3:
///   (2-2) ACCRINT never validated frequency ∈ {1,2,4} (every sibling bond/coupon function
///         does), so a bogus frequency silently produced a plausible-looking number instead
///         of the #NUM! error Excel returns.
///   (2-3) ACCRINT with basis=1 (Actual/Actual) computed a single calendar-year-split
///         day-count fraction over the whole span instead of Excel's documented
///         Sum(Ai/NLi) over the bond's own quasi-coupon periods, so a leap day inside the
///         span threw off an accrual that should land on an exact whole number of coupon
///         periods. The fix only applies the coupon-period fraction for the regular
///         (non-odd) first-coupon case -- an odd first coupon (first_interest not exactly
///         one period after issue, e.g. the existing R20 cross-year-boundary regression)
///         keeps the pre-existing whole-span fraction unchanged.
/// </summary>
public sealed class R28_AccrintFrequencyAndActualActualTests
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

    private string CalcError(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        var result = _eval.Evaluate("=" + formula, sheet, wb);
        result.Should().BeOfType<ErrorValue>($"formula {formula} should return an error");
        return ((ErrorValue)result).Code;
    }

    // ── R28-financial-functions-deep-2-2: frequency must be 1, 2 or 4 ──────────

    [Fact]
    public void Accrint_InvalidFrequency_ReturnsNum()
    {
        // frequency=3 is not a valid Excel coupon frequency -- must be #NUM!, not a silently
        // computed (and meaningless) accrual.
        CalcError("ACCRINT(DATE(2023,1,1),DATE(2023,7,1),DATE(2023,9,1),0.05,1000,3,0)")
            .Should().Be("#NUM!");
    }

    [Fact]
    public void Accrint_ValidFrequencies_StillComputeNormally()
    {
        // Sibling already-working case: 1, 2 and 4 remain valid and unaffected by the new guard.
        Calc("ACCRINT(DATE(2023,1,1),DATE(2023,7,1),DATE(2023,9,1),0.05,1000,1,0)")
            .Should().BeApproximately(1000 * 0.05 * (240.0 / 360.0), 1e-9);
        Calc("ACCRINT(DATE(2023,1,1),DATE(2023,7,1),DATE(2023,9,1),0.05,1000,2,0)")
            .Should().BeApproximately(1000 * 0.05 * (240.0 / 360.0), 1e-9);
        Calc("ACCRINT(DATE(2023,1,1),DATE(2023,7,1),DATE(2023,9,1),0.05,1000,4,0)")
            .Should().BeApproximately(1000 * 0.05 * (240.0 / 360.0), 1e-9);
    }

    // ── R28-financial-functions-deep-2-3: basis=1 coupon-period accrual ────────

    [Fact]
    public void Accrint_Basis1_RegularSemiannualSpan_AccruesExactWholeCouponPeriods()
    {
        // Semiannual bond, basis=1, issue 2015-08-31 -> first_interest 2016-02-29 (exactly one
        // regular 6-month period later) -> settlement 2016-08-31 (exactly the next coupon date),
        // spanning the 2016 leap day. Two full coupon periods have elapsed, so
        // ACCRINT = par*(rate/frequency)*(1+1) = 1000*0.04*2 = 80 exactly.
        double accrued = Calc("ACCRINT(DATE(2015,8,31),DATE(2016,2,29),DATE(2016,8,31),0.08,1000,2,1,TRUE)");

        accrued.Should().BeApproximately(80.0, 1e-9);

        // Guard against regressing to the pre-fix whole-span calendar-year-split fraction, which
        // gave approximately 80.07 (off by the leap-day-boundary split error).
        accrued.Should().NotBeApproximately(80.07, 0.01);
    }

    [Fact]
    public void Accrint_Basis1_OddFirstCoupon_CrossYearBoundary_UnaffectedByFix()
    {
        // Sibling no-regression case: same scenario as R20_FinancialDayCountTests'
        // Accrint_Basis1_AcrossYearBoundary_UsesTrueActualActualFraction. first_interest
        // (2024-07-15) is NOT exactly one 6-month period after issue (2023-12-15), so this is an
        // odd first coupon; the coupon-period fraction is deliberately not applied here and the
        // existing whole-span Actual/Actual fraction must still hold.
        double accrued = Calc("ACCRINT(DATE(2023,12,15),DATE(2024,7,15),DATE(2024,1,15),0.05,1000,2,1)");
        double expectedDcf = 17.0 / 365.0 + 14.0 / 366.0;

        accrued.Should().BeApproximately(1000 * 0.05 * expectedDcf, 1e-6);
    }

    [Fact]
    public void Accrint_CalcMethodFalse_StillAccruesFromFirstInterest_NotIssue()
    {
        // Sibling no-regression test for the R21 ACCRINT calc_method fix: with calc_method=FALSE
        // and settlement past first_interest, accrual must start at first_interest, not issue.
        // basis=0 (30/360): issue 2020-01-01, first_interest 2020-07-01 (regular period),
        // settlement 2021-01-01, rate=6%, frequency=2.
        //   From first_interest: 30/360 days(2020-07-01,2021-01-01) = 180 -> dcf=0.5 -> 1000*0.06*0.5=30.
        //   From issue (the pre-R21-fix bug): 30/360 days(2020-01-01,2021-01-01) = 360 -> dcf=1 -> 60.
        double accrued = Calc("ACCRINT(DATE(2020,1,1),DATE(2020,7,1),DATE(2021,1,1),0.06,1000,2,0,FALSE)");

        accrued.Should().BeApproximately(30.0, 1e-9);
        accrued.Should().NotBeApproximately(60.0, 1e-9);
    }
}
