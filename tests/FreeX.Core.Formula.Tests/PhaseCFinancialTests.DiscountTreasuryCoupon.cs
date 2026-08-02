using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseCFinancialTests
{
    // ── DISC ─────────────────────────────────────────────────────────────

    [Fact]
    public void Disc_SimpleCase()
    {
        // Settlement 2020-01-01 = 43831, Maturity 2021-01-01 = 44197 (366 days)
        // PR = 97, Redemption = 100, Basis=0
        // DCF = 360/360 = 1.0 (30/360), roughly
        // DISC = (100 - 97) / 100 / DCF ≈ 0.03
        double result = Calc("DISC(43831,44197,97,100,0)");
        result.Should().BeInRange(0.02, 0.04);
    }

    // ── INTRATE ──────────────────────────────────────────────────────────

    [Fact]
    public void DiscountSettlementFunctions_RangeValueArgument_SpillElementwise()
    {
        AssertApproxColumn(
            EvalWithData("DISC(43831,44197,A1:A2,100,0)", (1, 1, 97.0), (2, 1, 98.0)),
            Calc("DISC(43831,44197,97,100,0)"),
            Calc("DISC(43831,44197,98,100,0)"));
        AssertApproxColumn(
            EvalWithData("INTRATE(43831,44197,A1:A2,100,0)", (1, 1, 90.0), (2, 1, 95.0)),
            Calc("INTRATE(43831,44197,90,100,0)"),
            Calc("INTRATE(43831,44197,95,100,0)"));
        AssertApproxColumn(
            EvalWithData("RECEIVED(43831,44197,100,A1:A2,0)", (1, 1, 0.05), (2, 1, 0.04)),
            Calc("RECEIVED(43831,44197,100,0.05,0)"),
            Calc("RECEIVED(43831,44197,100,0.04,0)"));
    }

    [Fact]
    public void DiscountSettlementFunctions_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var cells = new[]
        {
            (1, 1, 43831.0), (2, 1, 43862.0),
            (1, 2, 44197.0), (2, 2, 44228.0),
            (1, 3, 97.0), (2, 3, 98.0),
            (1, 4, 100.0), (2, 4, 110.0),
            (1, 5, 0.0), (2, 5, 1.0),
            (1, 6, 90.0), (2, 6, 95.0),
            (1, 7, 0.05), (2, 7, 0.04)
        };

        AssertApproxColumn(EvalWithData("DISC(A1:A2,B1:B2,C1:C2,D1:D2,E1:E2)", cells), Calc("DISC(43831,44197,97,100,0)"), Calc("DISC(43862,44228,98,110,1)"));
        AssertApproxColumn(EvalWithData("INTRATE(A1:A2,B1:B2,F1:F2,D1:D2,E1:E2)", cells), Calc("INTRATE(43831,44197,90,100,0)"), Calc("INTRATE(43862,44228,95,110,1)"));
        AssertApproxColumn(EvalWithData("RECEIVED(A1:A2,B1:B2,D1:D2,G1:G2,E1:E2)", cells), Calc("RECEIVED(43831,44197,100,0.05,0)"), Calc("RECEIVED(43862,44228,110,0.04,1)"));

        EvalWithData("DISC(A1:A2,B1:B3,97,100,0)", cells).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Disc_InvalidBasis_ReturnsNumError()
    {
        CalcError("DISC(43831,44197,97,100,5)").Should().Be("#NUM!");
        CalcError("DISC(43831,44197,97,100,-1)").Should().Be("#NUM!");
        CalcError("DISC(43831,44197,97,100,1E309)").Should().Be("#NUM!");
    }

    [Fact]
    public void Intrate_SimpleCase()
    {
        // Settlement 43831, Maturity 44197, Invest 90, Redeem 100
        // Rate = (100-90)/90 / DCF
        double result = Calc("INTRATE(43831,44197,90,100,0)");
        result.Should().BeGreaterThan(0);
    }

    // ── RECEIVED ─────────────────────────────────────────────────────────

    [Fact]
    public void Intrate_InvalidBasis_ReturnsNumError()
    {
        CalcError("INTRATE(43831,44197,90,100,5)").Should().Be("#NUM!");
        CalcError("INTRATE(43831,44197,90,100,-1)").Should().Be("#NUM!");
        CalcError("INTRATE(43831,44197,90,100,1E309)").Should().Be("#NUM!");
    }

    [Fact]
    public void Received_SimpleCase()
    {
        // Investment = 100, discount = 0.05, DCF ≈ 1 year
        // Received = 100 / (1 - 0.05 * 1) = 100/0.95 ≈ 105.26
        double result = Calc("RECEIVED(43831,44197,100,0.05,0)");
        result.Should().BeApproximately(105.26, 0.5);
    }

    // ── TBILLEQ / TBILLPRICE / TBILLYIELD ────────────────────────────────

    [Fact]
    public void Received_InvalidBasis_ReturnsNumError()
    {
        CalcError("RECEIVED(43831,44197,100,0.05,5)").Should().Be("#NUM!");
        CalcError("RECEIVED(43831,44197,100,0.05,-1)").Should().Be("#NUM!");
        CalcError("RECEIVED(43831,44197,100,0.05,1E309)").Should().Be("#NUM!");
    }

    [Fact]
    public void Tbillprice_SimpleCase()
    {
        // TBILLPRICE(settlement, settlement+90days, 0.05)
        // DSM = 90, Price = 100*(1 - 0.05*90/360) = 100*(1-0.0125) = 98.75
        // settlement = 43831 (Jan 1 2020), maturity = 43921 (Apr 1 2020 approx)
        double result = Calc("TBILLPRICE(43831,43921,0.05)");
        result.Should().BeApproximately(98.75, 0.01);
    }

    [Fact]
    public void Tbillyield_SimpleCase()
    {
        // TBILLYIELD(settlement, settlement+90, 98.75)
        // = (100-98.75)/98.75 * 360/90 = 1.25/98.75 * 4 ≈ 0.05063
        double result = Calc("TBILLYIELD(43831,43921,98.75)");
        result.Should().BeApproximately(0.05063, 0.0001);
    }

    [Fact]
    public void Tbilleq_SimpleCase()
    {
        // TBILLEQ(settlement, settlement+90, 0.05)
        // = (365 * 0.05) / (360 - 0.05 * 90) ≈ 0.05097
        double result = Calc("TBILLEQ(43831,43921,0.05)");
        result.Should().BeApproximately(0.05097, 0.0005);
    }

    // ── COUPNUM ───────────────────────────────────────────────────────────

    [Fact]
    public void TreasuryBillFunctions_RangeValueArgument_SpillElementwise()
    {
        AssertApproxColumn(
            EvalWithData("TBILLEQ(43831,43921,A1:A2)", (1, 1, 0.05), (2, 1, 0.04)),
            Calc("TBILLEQ(43831,43921,0.05)"),
            Calc("TBILLEQ(43831,43921,0.04)"));
        AssertApproxColumn(
            EvalWithData("TBILLPRICE(43831,43921,A1:A2)", (1, 1, 0.05), (2, 1, 0.04)),
            Calc("TBILLPRICE(43831,43921,0.05)"),
            Calc("TBILLPRICE(43831,43921,0.04)"));
        AssertApproxColumn(
            EvalWithData("TBILLYIELD(43831,43921,A1:A2)", (1, 1, 98.75), (2, 1, 99.0)),
            Calc("TBILLYIELD(43831,43921,98.75)"),
            Calc("TBILLYIELD(43831,43921,99)"));
    }

    [Fact]
    public void TreasuryBillFunctions_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var cells = new[]
        {
            (1, 1, 43831.0), (2, 1, 43862.0),
            (1, 2, 43921.0), (2, 2, 43952.0),
            (1, 3, 0.05), (2, 3, 0.04),
            (1, 4, 98.75), (2, 4, 99.0)
        };

        AssertApproxColumn(EvalWithData("TBILLEQ(A1:A2,B1:B2,C1:C2)", cells), Calc("TBILLEQ(43831,43921,0.05)"), Calc("TBILLEQ(43862,43952,0.04)"));
        AssertApproxColumn(EvalWithData("TBILLPRICE(A1:A2,B1:B2,C1:C2)", cells), Calc("TBILLPRICE(43831,43921,0.05)"), Calc("TBILLPRICE(43862,43952,0.04)"));
        AssertApproxColumn(EvalWithData("TBILLYIELD(A1:A2,B1:B2,D1:D2)", cells), Calc("TBILLYIELD(43831,43921,98.75)"), Calc("TBILLYIELD(43862,43952,99)"));

        EvalWithData("TBILLEQ(A1:A2,B1:B3,0.05)", cells).Should().Be(ErrorValue.Value);
    }

    // ── TBILL >1-year maturity guard (J4) ────────────────────────────────

    [Fact]
    public void Tbillprice_MaturityBeyondOneYear_ReturnsNumError()
    {
        // Settlement = 43831 (Jan 1 2020), maturity = 44562 (≈Jan 1 2022, 731 days)
        // Excel: #NUM! when DSM > 365
        CalcError("TBILLPRICE(43831,44562,0.05)").Should().Be("#NUM!");
    }

    [Fact]
    public void Tbillyield_MaturityBeyondOneYear_ReturnsNumError()
    {
        // Same 2-year span, price=98
        CalcError("TBILLYIELD(43831,44562,98)").Should().Be("#NUM!");
    }

    [Fact]
    public void Tbillprice_ValidLessThanOneYear_StillComputes()
    {
        // 90-day bill (< 365 days) must not be rejected by the new guard
        // TBILLPRICE(43831, 43921, 0.05) ≈ 98.75 (verified by existing test)
        double result = Calc("TBILLPRICE(43831,43921,0.05)");
        result.Should().BeApproximately(98.75, 0.01);
    }

    [Fact]
    public void Tbillyield_ValidLessThanOneYear_StillComputes()
    {
        // TBILLYIELD(43831, 43921, 98.75) ≈ 0.05063
        double result = Calc("TBILLYIELD(43831,43921,98.75)");
        result.Should().BeApproximately(0.05063, 0.0001);
    }

    [Fact]
    public void Tbilleq_MaturityBeyondOneYear_ReturnsNumError()
    {
        // Excel TBILLEQ returns #NUM! for DSM > 365 (not 182).
        // Settlement = 43831, maturity = 44562 (731 days)
        CalcError("TBILLEQ(43831,44562,0.05)").Should().Be("#NUM!");
    }

    [Fact]
    public void Tbilleq_DsmAtOneEightyTwoBoundary_StillUsesLinearFormula()
    {
        // DSM == 182 stays on the <=182 linear branch: (365*disc)/(360-disc*dsm)
        // Settlement = 43831 (2020-01-01), maturity = 44013 (182 days later), discount = 0.05
        // = (365*0.05) / (360 - 0.05*182) = 18.25 / 350.9 ≈ 0.052008
        double result = Calc("TBILLEQ(43831,44013,0.05)");
        result.Should().BeApproximately(0.052008, 0.0001);
    }

    [Fact]
    public void Tbilleq_DsmBeyondOneEightyTwo_UsesBondEquivalentYieldQuadratic()
    {
        // Excel switches to the BEY quadratic formula for 182 < DSM <= 365:
        // BEY = (-2*(M/365) + 2*sqrt((M/365)^2 - (2*M/365-1)*(1-100/P))) / (2*M/365-1)
        // where P = TBILLPRICE(settlement, maturity, discount).
        // Settlement = 43831 (2020-01-01), maturity = 44105 (274 days later), discount = 0.06
        // Expected ≈ 6.31% (matches Excel's published bond-equivalent-yield example).
        double result = Calc("TBILLEQ(43831,44105,0.06)");
        result.Should().BeApproximately(0.0631, 0.0005);
    }

    [Fact]
    public void Coupnum_SemiAnnual_FiveYearBond()
    {
        // Settlement ~2020-01-15 (43845), Maturity ~2025-01-15 (45672), freq=2
        // Approx 10 coupons remaining (5 years * 2)
        double result = Calc("COUPNUM(43845,45672,2)");
        result.Should().BeInRange(9, 11);
    }

    [Fact]
    public void Coupnum_Annual_OneYearRemaining()
    {
        // Settlement 43831 (Jan 1 2020), Maturity 44197 (Jan 1 2021), freq=1
        // 1 coupon remaining
        double result = Calc("COUPNUM(43831,44197,1)");
        result.Should().BeApproximately(1.0, 0.01);
    }

    // ── COUPDAYBS / COUPDAYSNC ─────────────────────────────────────────────

    [Fact]
    public void Coupdaybs_PlusCoupdaysnc_EqualsCoupdays()
    {
        // COUPDAYBS + COUPDAYSNC should equal roughly COUPDAYS
        double bs  = Calc("COUPDAYBS(43831,44197,2)");
        double snc = Calc("COUPDAYSNC(43831,44197,2)");
        double days = Calc("COUPDAYS(43831,44197,2)");
        (bs + snc).Should().BeApproximately(days, 2.0);
    }

    // ── PRICE / YIELD round-trip ──────────────────────────────────────────

    [Fact]
    public void CouponFunctions_RangeSettlementArgument_SpillElementwise()
    {
        var cells = new[] { (1, 1, 43831.0), (2, 1, 43845.0) };

        AssertApproxColumn(EvalWithData("COUPDAYBS(A1:A2,44197,2)", cells), Calc("COUPDAYBS(43831,44197,2)"), Calc("COUPDAYBS(43845,44197,2)"));
        AssertApproxColumn(EvalWithData("COUPDAYS(A1:A2,44197,2)", cells), Calc("COUPDAYS(43831,44197,2)"), Calc("COUPDAYS(43845,44197,2)"));
        AssertApproxColumn(EvalWithData("COUPDAYSNC(A1:A2,44197,2)", cells), Calc("COUPDAYSNC(43831,44197,2)"), Calc("COUPDAYSNC(43845,44197,2)"));
        AssertApproxColumn(EvalWithData("COUPNCD(A1:A2,44197,2)", cells), Calc("COUPNCD(43831,44197,2)"), Calc("COUPNCD(43845,44197,2)"));
        AssertApproxColumn(EvalWithData("COUPNUM(A1:A2,44197,2)", cells), Calc("COUPNUM(43831,44197,2)"), Calc("COUPNUM(43845,44197,2)"));
        AssertApproxColumn(EvalWithData("COUPPCD(A1:A2,44197,2)", cells), Calc("COUPPCD(43831,44197,2)"), Calc("COUPPCD(43845,44197,2)"));
    }

    [Fact]
    public void CouponFunctions_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var cells = new[]
        {
            (1, 1, 43831.0), (2, 1, 43845.0),
            (1, 2, 44197.0), (2, 2, 44562.0),
            (1, 3, 2.0), (2, 3, 4.0),
            (1, 4, 0.0), (2, 4, 1.0)
        };

        AssertApproxColumn(EvalWithData("COUPDAYBS(A1:A2,B1:B2,C1:C2,D1:D2)", cells), Calc("COUPDAYBS(43831,44197,2,0)"), Calc("COUPDAYBS(43845,44562,4,1)"));
        AssertApproxColumn(EvalWithData("COUPDAYS(A1:A2,B1:B2,C1:C2,D1:D2)", cells), Calc("COUPDAYS(43831,44197,2,0)"), Calc("COUPDAYS(43845,44562,4,1)"));
        AssertApproxColumn(EvalWithData("COUPDAYSNC(A1:A2,B1:B2,C1:C2,D1:D2)", cells), Calc("COUPDAYSNC(43831,44197,2,0)"), Calc("COUPDAYSNC(43845,44562,4,1)"));
        AssertApproxColumn(EvalWithData("COUPNCD(A1:A2,B1:B2,C1:C2,D1:D2)", cells), Calc("COUPNCD(43831,44197,2,0)"), Calc("COUPNCD(43845,44562,4,1)"));
        AssertApproxColumn(EvalWithData("COUPNUM(A1:A2,B1:B2,C1:C2,D1:D2)", cells), Calc("COUPNUM(43831,44197,2,0)"), Calc("COUPNUM(43845,44562,4,1)"));
        AssertApproxColumn(EvalWithData("COUPPCD(A1:A2,B1:B2,C1:C2,D1:D2)", cells), Calc("COUPPCD(43831,44197,2,0)"), Calc("COUPPCD(43845,44562,4,1)"));

        EvalWithData("COUPDAYBS(A1:A2,B1:B3,2,0)", cells).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void CouponFunctions_InvalidBasis_ReturnNumError()
    {
        CalcError("COUPDAYBS(43831,44197,2,5)").Should().Be("#NUM!");
        CalcError("COUPDAYS(43831,44197,2,-1)").Should().Be("#NUM!");
        CalcError("COUPDAYSNC(43831,44197,2,1E309)").Should().Be("#NUM!");
        CalcError("COUPNCD(43831,44197,2,5)").Should().Be("#NUM!");
        CalcError("COUPNUM(43831,44197,2,-1)").Should().Be("#NUM!");
        CalcError("COUPPCD(43831,44197,2,1E309)").Should().Be("#NUM!");
    }

    [Fact]
    public void Coupncd_AndCouppcd_BracketSettlementDate()
    {
        double settlement = 43831;
        double maturity = 44197;

        Calc($"COUPPCD({settlement},{maturity},2)").Should().BeLessThanOrEqualTo(settlement);
        Calc($"COUPNCD({settlement},{maturity},2)").Should().BeGreaterThan(settlement);
    }
}
