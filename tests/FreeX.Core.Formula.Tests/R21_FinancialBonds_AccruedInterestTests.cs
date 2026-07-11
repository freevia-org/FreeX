using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for R21-financial-functions-deep-1/2/3:
///   (1) PRICEMAT never subtracted the accrued-interest-since-issue term, overstating
///       the maturity-value bond price whenever issue &lt; settlement.
///   (2) YIELDMAT's denominator omitted the same accrued-interest-since-issue term,
///       roughly doubling the computed yield whenever issue &lt; settlement.
///   (3) FinancialDays' basis-3 (Actual/365) coupon-period length (used by
///       ODDFPRICE/ODDFYIELD/ODDLPRICE/ODDLYIELD via CouponPeriodDays) used the actual
///       calendar days in the period instead of the fixed 365/frequency that COUPDAYS
///       and PRICE/YIELD/DURATION already use for basis 3.
/// All existing PRICEMAT/YIELDMAT tests only exercised issue == settlement, where the
/// missing accrued-interest term is algebraically zero, masking bugs (1) and (2).
/// </summary>
public sealed class R21_FinancialBonds_AccruedInterestTests
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

    // ── R21-financial-functions-deep-1: PRICEMAT accrued-interest subtraction ──

    [Fact]
    public void Pricemat_IssueBeforeSettlement_SubtractsAccruedInterestSinceIssue()
    {
        // settlement=2020-07-01 (44013), maturity=2021-01-01 (44197), issue=2020-01-01 (43831)
        // rate=10%, yld=10%, basis=0 (30/360).
        // Excel: result = 100*(1+rate*DIM)/(1+yld*DSM) - (A/B)*rate*100
        //   DIM (issue->maturity) = 360/360 = 1.0, DSM (settlement->maturity) = 180/360 = 0.5,
        //   A/B (issue->settlement) = 180/360 = 0.5
        //   = 100*1.1/1.05 - 0.5*0.1*100 = 104.76190476190476 - 5 = 99.76190476190476
        double result = Calc("PRICEMAT(44013,44197,43831,0.1,0.1,0)");

        result.Should().BeApproximately(2095.0 / 21.0, 1e-9); // 99.761904761904761904...

        // Guard against regressing to the pre-fix bug, which omitted the subtraction entirely
        // and returned the un-adjusted maturity-value price (104.7619047619).
        result.Should().BeLessThan(104.0);
    }

    [Fact]
    public void Pricemat_IssueEqualsSettlement_UnaffectedByFix()
    {
        // The accrued-interest term is algebraically zero when issue == settlement (A/B == 0),
        // so this pre-existing case (PhaseCFinancialTests.Bond.Pricemat_SimpleCase) must still
        // return the same result after the fix.
        double result = Calc("PRICEMAT(43831,44197,43831,0.05,0.05)");
        result.Should().BeApproximately(100.0, 1.0);
    }

    // ── R21-financial-functions-deep-2: YIELDMAT accrued-interest denominator ──

    [Fact]
    public void Yieldmat_IssueBeforeSettlement_IncludesAccruedInterestInDenominator()
    {
        // Same dates as above, price=100, rate=10%, basis=0.
        // Excel: yield = [(1+rate*DIM)/(price/100 + (A/B)*rate) - 1] * (1/DSM)
        //   = (1.10/(1.0+0.5*0.1) - 1) / 0.5 = (1.10/1.05 - 1) / 0.5 = 0.047619047619 / 0.5
        //   = 0.095238095238095...
        double result = Calc("YIELDMAT(44013,44197,43831,0.1,100,0)");

        result.Should().BeApproximately(2.0 / 21.0, 1e-9); // 0.0952380952380952...

        // Guard against regressing to the pre-fix bug (dividing by price/100 alone), which
        // produced 0.20 -- more than double the correct yield.
        result.Should().BeLessThan(0.15);
    }

    [Fact]
    public void Yieldmat_RoundTripsPricematAtPar_WithIssueBeforeSettlement()
    {
        // A price computed by PRICEMAT for issue < settlement must round-trip back through
        // YIELDMAT to the original yield, exercising both fixed accrued-interest terms together.
        double price = Calc("PRICEMAT(44013,44197,43831,0.1,0.1,0)");

        Calc($"YIELDMAT(44013,44197,43831,0.1,{price.ToString("R")},0)")
            .Should().BeApproximately(0.1, 1e-6);
    }

    // ── R21-financial-functions-deep-3: basis-3 fixed coupon-period length ──

    [Fact]
    public void Oddlyield_Basis3_UsesFixed365OverFrequencyPeriodLength_NotActualCalendarDays()
    {
        // last_interest=2020-08-01 (44044), settlement=2020-10-01 (44105),
        // maturity=2021-02-01 (44228), rate=10%, price=98, redemption=100, frequency=2, basis=3.
        // Excel fixes the coupon-period length E at 365/frequency=182.5 for basis 3 (matching
        // COUPDAYS and PRICE/YIELD/DURATION's own basis-3 convention), giving y ≈ 0.15987521.
        // The pre-fix bug used the actual Aug1->Feb1 span (184 days) as E, giving y ≈ 0.1603866
        // -- a reproducible, basis-dependent discrepancy.
        double result = Calc("ODDLYIELD(44105,44228,44044,0.1,98,100,2,3)");

        result.Should().BeApproximately(35770.0 / 223737.0, 1e-6); // ≈ 0.15987521

        // Guard against regressing to the pre-fix actual-calendar-days period length.
        result.Should().BeLessThan(0.16);
        result.Should().NotBeApproximately(361744.0 / 2255451.0, 1e-6); // pre-fix ≈ 0.1603866
    }

    [Fact]
    public void Oddlyield_Basis3_DiffersFromBasis1_ForOddCouponPeriodNotExactlyHalfYear()
    {
        // basis=1 (Actual/Actual) uses the true actual-day period length (184 days for this
        // Aug1->Feb1 span), while basis=3 must now use the fixed 182.5 -- so the two bases
        // must produce distinct results for an odd period that isn't exactly 182.5 days long.
        double basis3 = Calc("ODDLYIELD(44105,44228,44044,0.1,98,100,2,3)");
        double basis1 = Calc("ODDLYIELD(44105,44228,44044,0.1,98,100,2,1)");

        basis3.Should().NotBe(basis1);
    }
}
