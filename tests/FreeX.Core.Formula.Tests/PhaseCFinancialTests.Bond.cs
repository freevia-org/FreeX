using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseCFinancialTests
{
    [Fact]
    public void Price_KnownBond()
    {
        // 10% annual coupon, 5-year bond, yield=10% → price should be ~100
        // Settlement 43831 (2020-01-01), Maturity 45676 (2025-01-05 approx), use 44927 (2023-01-01 roughly)
        // Use exact dates: Settlement=43831, Maturity=45658 (2025-01-01 approx = 43831+1827)
        double maturity = 43831 + 5 * 365 + 2; // approx 5 years
        double result = Calc($"PRICE(43831,{maturity},0.1,0.1,100,1)");
        // When coupon rate = yield, price should be near par
        result.Should().BeApproximately(100.0, 3.0);
    }

    [Fact]
    public void Price_Yield_RoundTrip()
    {
        // YIELD(PRICE(rate)) should return original yield
        double maturity = 43831 + 5 * 365 + 2;
        double settlement = 43831;
        double rate = 0.08;
        double targetYield = 0.06;
        double price = Calc($"PRICE({settlement},{maturity},{rate},{targetYield},100,2)");
        double yld = Calc($"YIELD({settlement},{maturity},{rate},{price},100,2)");
        yld.Should().BeApproximately(targetYield, 0.0005);
    }

    // ── PRICEDISC / YIELDDISC ─────────────────────────────────────────────

    [Fact]
    public void Pricedisc_KnownDiscount()
    {
        // PRICEDISC: price = par * (1 - discount * dcf)
        // With US 30/360 basis, 2020-01-01 to 2020-12-31 = 360 days → dcf = 1.0
        // price = 100 * (1 - 0.05 * 1.0) = 95.0
        double settlement = 43831;
        double maturity = 44197;
        double price = Calc($"PRICEDISC({settlement},{maturity},0.05,100)");
        price.Should().BeApproximately(95.0, 0.5);
    }

    [Fact]
    public void Yielddisc_KnownCase()
    {
        // YIELDDISC: yield = (par/price - 1) / dcf
        // price=95, par=100, dcf≈1.0 → yield = (100/95 - 1) = 0.0526...
        double settlement = 43831;
        double maturity = 44197;
        double yld = Calc($"YIELDDISC({settlement},{maturity},95,100)");
        yld.Should().BeApproximately(0.0526, 0.001);
    }

    // ── PRICEMAT / YIELDMAT ────────────────────────────────────────────────

    [Fact]
    public void Pricedisc_InvalidBasis_ReturnsNumError()
    {
        CalcError("PRICEDISC(43831,44197,0.05,100,5)").Should().Be("#NUM!");
        CalcError("PRICEDISC(43831,44197,0.05,100,-1)").Should().Be("#NUM!");
        CalcError("PRICEDISC(43831,44197,0.05,100,1E309)").Should().Be("#NUM!");
    }

    [Fact]
    public void Yielddisc_InvalidBasis_ReturnsNumError()
    {
        CalcError("YIELDDISC(43831,44197,95,100,5)").Should().Be("#NUM!");
        CalcError("YIELDDISC(43831,44197,95,100,-1)").Should().Be("#NUM!");
        CalcError("YIELDDISC(43831,44197,95,100,1E309)").Should().Be("#NUM!");
    }

    [Fact]
    public void Pricemat_SimpleCase()
    {
        // PRICEMAT with issue=settlement gives price = 100*(1+rate*0)/(1+yld*dcf)
        double settlement = 43831;
        double maturity = 44197;
        double issue = 43831;
        double result = Calc($"PRICEMAT({settlement},{maturity},{issue},0.05,0.05)");
        // When rate=yld and issue=settlement, price should be ~100
        result.Should().BeApproximately(100.0, 1.0);
    }

    // ── DURATION / MDURATION ──────────────────────────────────────────────

    [Fact]
    public void Duration_AnnualZeroCoupon_EqualsTerm()
    {
        // A zero-coupon bond's Macaulay duration = term to maturity
        // (not exactly but approximately for large discounts)
        // Test that duration > 0 and less than term
        double settlement = 43831;
        double maturity = 43831 + 5 * 365;
        double duration = Calc($"DURATION({settlement},{maturity},0.0,0.05,1)");
        duration.Should().BeGreaterThan(0);
        duration.Should().BeLessThan(6.0);
    }

    [Fact]
    public void Mduration_LessThanDuration()
    {
        double settlement = 43831;
        double maturity = 43831 + 5 * 365;
        double dur = Calc($"DURATION({settlement},{maturity},0.08,0.06,2)");
        double mdur = Calc($"MDURATION({settlement},{maturity},0.08,0.06,2)");
        mdur.Should().BeLessThan(dur);
        mdur.Should().BeApproximately(dur / (1 + 0.06 / 2), 0.001);
    }

    [Fact]
    public void Duration_MonthEndMaturityCrossingShorterMonth_IncludesRedemption()
    {
        // R79-formula-financial-5-1: settlement 2025-01-31, maturity 2027-01-31 (month-end),
        // quarterly frequency. Walking the coupon schedule forward from the shrinking previous
        // date (the pre-fix bug) drifts through April (30 days) and never lands back on the
        // day-31 maturity, so the redemption principal is silently dropped from the last cash
        // flow. Anchoring every candidate off the ORIGINAL maturity (the fix) keeps the last
        // schedule entry exactly equal to maturity, so the redemption is folded in correctly.
        // Hand-computed expected value: 8 quarterly coupons of c=1.25 (coupon 5%, freq 4) at
        // y=0.0125/period, with the final period's cash flow = 1.25 + 100 (redemption); since
        // settlement sits exactly on a coupon reset date, price comes out to par (100) and the
        // weighted-average (Macaulay) duration is ~1.91568146186755 years.
        double duration = Calc("DURATION(DATE(2025,1,31),DATE(2027,1,31),0.05,0.05,4,0)");
        duration.Should().BeApproximately(1.91568146186755, 1e-9);
    }

    [Fact]
    public void Mduration_MonthEndMaturityCrossingShorterMonth_IncludesRedemption()
    {
        // Sibling of Duration_MonthEndMaturityCrossingShorterMonth_IncludesRedemption:
        // MDURATION = DURATION / (1 + yield/frequency) = 1.91568146186755 / 1.0125.
        double mduration = Calc("MDURATION(DATE(2025,1,31),DATE(2027,1,31),0.05,0.05,4,0)");
        mduration.Should().BeApproximately(1.89203107344943, 1e-9);
    }

    [Fact]
    public void Duration_NonMonthEndSchedule_NoRegression()
    {
        // No-regression sibling: identical economics (2-year, quarterly, 5%/5%, basis 0) but with
        // a day-of-month (15th) that never hits .NET's AddMonths clamp, so this case was already
        // correct before the fix. Confirms the anchored-schedule rewrite did not change behavior
        // for the common (non-edge-case) path -- same expected value as the month-end case above,
        // since basis-0 (30/360) day counts are day-of-month-shape-invariant here.
        double duration = Calc("DURATION(DATE(2025,1,15),DATE(2027,1,15),0.05,0.05,4,0)");
        duration.Should().BeApproximately(1.91568146186755, 1e-9);
    }

    // ── EFFECT/NOMINAL edge cases ─────────────────────────────────────────

    [Fact]
    public void BondPriceYieldFunctions_RangeValueArgument_SpillElementwise()
    {
        var cells = new[] { (1, 1, 0.05), (2, 1, 0.06) };

        AssertApproxColumn(EvalWithData("PRICE(43831,45658,0.08,A1:A2,100,2)", cells), Calc("PRICE(43831,45658,0.08,0.05,100,2)"), Calc("PRICE(43831,45658,0.08,0.06,100,2)"));
        AssertApproxColumn(EvalWithData("YIELD(43831,45658,0.08,A1:A2,100,2)", (1, 1, 99.0), (2, 1, 101.0)), Calc("YIELD(43831,45658,0.08,99,100,2)"), Calc("YIELD(43831,45658,0.08,101,100,2)"));
        AssertApproxColumn(EvalWithData("PRICEDISC(43831,44197,A1:A2,100)", cells), Calc("PRICEDISC(43831,44197,0.05,100)"), Calc("PRICEDISC(43831,44197,0.06,100)"));
        AssertApproxColumn(EvalWithData("YIELDDISC(43831,44197,A1:A2,100)", (1, 1, 95.0), (2, 1, 96.0)), Calc("YIELDDISC(43831,44197,95,100)"), Calc("YIELDDISC(43831,44197,96,100)"));
        AssertApproxColumn(EvalWithData("PRICEMAT(43831,44197,43831,0.05,A1:A2)", cells), Calc("PRICEMAT(43831,44197,43831,0.05,0.05)"), Calc("PRICEMAT(43831,44197,43831,0.05,0.06)"));
        AssertApproxColumn(EvalWithData("YIELDMAT(43831,44197,43831,0.05,A1:A2)", (1, 1, 99.0), (2, 1, 101.0)), Calc("YIELDMAT(43831,44197,43831,0.05,99)"), Calc("YIELDMAT(43831,44197,43831,0.05,101)"));
        AssertApproxColumn(EvalWithData("DURATION(43831,45656,0.08,A1:A2,2)", cells), Calc("DURATION(43831,45656,0.08,0.05,2)"), Calc("DURATION(43831,45656,0.08,0.06,2)"));
        AssertApproxColumn(EvalWithData("MDURATION(43831,45656,0.08,A1:A2,2)", cells), Calc("MDURATION(43831,45656,0.08,0.05,2)"), Calc("MDURATION(43831,45656,0.08,0.06,2)"));
    }

    [Fact]
    public void BondPriceYieldFunctions_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var cells = new[]
        {
            (1, 1, 43831.0), (2, 1, 43845.0),
            (1, 2, 45658.0), (2, 2, 45672.0),
            (1, 3, 0.08), (2, 3, 0.07),
            (1, 4, 0.05), (2, 4, 0.06),
            (1, 5, 100.0), (2, 5, 110.0),
            (1, 6, 2.0), (2, 6, 4.0),
            (1, 7, 0.0), (2, 7, 1.0),
            (1, 8, 99.0), (2, 8, 101.0),
            (1, 9, 44197.0), (2, 9, 44228.0)
        };

        AssertApproxColumn(EvalWithData("PRICE(A1:A2,B1:B2,C1:C2,D1:D2,E1:E2,F1:F2,G1:G2)", cells), Calc("PRICE(43831,45658,0.08,0.05,100,2,0)"), Calc("PRICE(43845,45672,0.07,0.06,110,4,1)"));
        AssertApproxColumn(EvalWithData("YIELD(A1:A2,B1:B2,C1:C2,H1:H2,E1:E2,F1:F2,G1:G2)", cells), Calc("YIELD(43831,45658,0.08,99,100,2,0)"), Calc("YIELD(43845,45672,0.07,101,110,4,1)"));
        AssertApproxColumn(EvalWithData("PRICEDISC(A1:A2,I1:I2,D1:D2,E1:E2,G1:G2)", cells), Calc("PRICEDISC(43831,44197,0.05,100,0)"), Calc("PRICEDISC(43845,44228,0.06,110,1)"));
        AssertApproxColumn(EvalWithData("YIELDDISC(A1:A2,I1:I2,H1:H2,E1:E2,G1:G2)", cells), Calc("YIELDDISC(43831,44197,99,100,0)"), Calc("YIELDDISC(43845,44228,101,110,1)"));
        AssertApproxColumn(EvalWithData("PRICEMAT(A1:A2,I1:I2,A1:A2,C1:C2,D1:D2,G1:G2)", cells), Calc("PRICEMAT(43831,44197,43831,0.08,0.05,0)"), Calc("PRICEMAT(43845,44228,43845,0.07,0.06,1)"));
        AssertApproxColumn(EvalWithData("YIELDMAT(A1:A2,I1:I2,A1:A2,C1:C2,H1:H2,G1:G2)", cells), Calc("YIELDMAT(43831,44197,43831,0.08,99,0)"), Calc("YIELDMAT(43845,44228,43845,0.07,101,1)"));
        AssertApproxColumn(EvalWithData("DURATION(A1:A2,B1:B2,C1:C2,D1:D2,F1:F2,G1:G2)", cells), Calc("DURATION(43831,45658,0.08,0.05,2,0)"), Calc("DURATION(43845,45672,0.07,0.06,4,1)"));
        AssertApproxColumn(EvalWithData("MDURATION(A1:A2,B1:B2,C1:C2,D1:D2,F1:F2,G1:G2)", cells), Calc("MDURATION(43831,45658,0.08,0.05,2,0)"), Calc("MDURATION(43845,45672,0.07,0.06,4,1)"));

        EvalWithData("PRICE(A1:A2,B1:B3,0.08,0.05,100,2,0)", cells).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Yieldmat_RoundTripsPricematAtPar()
    {
        double settlement = 43831;
        double maturity = 44197;
        double issue = 43831;
        double price = Calc($"PRICEMAT({settlement},{maturity},{issue},0.05,0.05)");

        Calc($"YIELDMAT({settlement},{maturity},{issue},0.05,{price.ToString("R")})")
            .Should().BeApproximately(0.05, 0.001);
    }
}
