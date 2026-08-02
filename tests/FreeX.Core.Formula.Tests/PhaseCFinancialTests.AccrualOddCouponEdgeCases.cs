using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseCFinancialTests
{
    [Fact]
    public void BondAndAccrualFunctions_InvalidBasis_ReturnNumError()
    {
        CalcError("PRICE(43831,45658,0.1,0.1,100,1,5)").Should().Be("#NUM!");
        CalcError("YIELD(43831,45658,0.1,100,100,1,-1)").Should().Be("#NUM!");
        CalcError("PRICEMAT(43831,44197,43831,0.05,0.05,1E309)").Should().Be("#NUM!");
        CalcError("YIELDMAT(43831,44197,43831,0.05,100,5)").Should().Be("#NUM!");
        CalcError("DURATION(43831,45656,0.08,0.06,2,-1)").Should().Be("#NUM!");
        CalcError("MDURATION(43831,45656,0.08,0.06,2,1E309)").Should().Be("#NUM!");
        CalcError("ACCRINT(43831,43831,44197,0.05,1000,2,5)").Should().Be("#NUM!");
    }

    [Fact]
    public void BondPriceYieldFunctions_DateSerialOutsideExcelRange_ReturnNumError()
    {
        CalcError("PRICE(2958466,2958467,0.1,0.1,100,1)").Should().Be("#NUM!");
        CalcError("YIELD(2958466,2958467,0.1,100,100,1)").Should().Be("#NUM!");
        CalcError("PRICEDISC(2958466,2958467,0.05,100)").Should().Be("#NUM!");
        CalcError("YIELDDISC(2958466,2958467,95,100)").Should().Be("#NUM!");
        CalcError("PRICEMAT(2958466,2958467,2958466,0.05,0.05)").Should().Be("#NUM!");
        CalcError("YIELDMAT(2958466,2958467,2958466,0.05,100)").Should().Be("#NUM!");
    }

    [Fact]
    public void BondPriceYieldFunctions_NegativeDateSerial_ReturnNumError()
    {
        CalcError("PRICE(-1,45658,0.1,0.1,100,1)").Should().Be("#NUM!");
        CalcError("YIELD(-1,45658,0.1,100,100,1)").Should().Be("#NUM!");
        CalcError("PRICEDISC(-1,44197,0.05,100)").Should().Be("#NUM!");
        CalcError("YIELDDISC(-1,44197,95,100)").Should().Be("#NUM!");
        CalcError("PRICEMAT(-1,44197,-1,0.05,0.05)").Should().Be("#NUM!");
        CalcError("YIELDMAT(-1,44197,-1,0.05,100)").Should().Be("#NUM!");
    }

    [Fact]
    public void DiscountSettlementFunctions_NegativeDateSerial_ReturnNumError()
    {
        CalcError("DISC(-1,44197,95,100)").Should().Be("#NUM!");
        CalcError("INTRATE(-1,44197,95,100)").Should().Be("#NUM!");
        CalcError("RECEIVED(-1,44197,100,0.05)").Should().Be("#NUM!");
        CalcError("TBILLEQ(-1,44197,0.05)").Should().Be("#NUM!");
        CalcError("TBILLPRICE(-1,44197,0.05)").Should().Be("#NUM!");
        CalcError("TBILLYIELD(-1,44197,95)").Should().Be("#NUM!");
    }

    [Fact]
    public void CouponDateFunctions_NegativeDateSerial_ReturnNumError()
    {
        CalcError("COUPDAYBS(-1,44197,2)").Should().Be("#NUM!");
        CalcError("COUPDAYS(-1,44197,2)").Should().Be("#NUM!");
        CalcError("COUPDAYSNC(-1,44197,2)").Should().Be("#NUM!");
        CalcError("COUPNCD(-1,44197,2)").Should().Be("#NUM!");
        CalcError("COUPNUM(-1,44197,2)").Should().Be("#NUM!");
        CalcError("COUPPCD(-1,44197,2)").Should().Be("#NUM!");
    }

    [Fact]
    public void DepreciationAccrualDurationFunctions_NegativeDateSerial_ReturnNumError()
    {
        CalcError("AMORDEGRC(1000,-1,44197,100,1,0.1)").Should().Be("#NUM!");
        CalcError("AMORLINC(1000,-1,44197,100,1,0.1)").Should().Be("#NUM!");
        CalcError("ACCRINT(-1,43831,44197,0.05,1000,2)").Should().Be("#NUM!");
        CalcError("DURATION(-1,44197,0.05,0.06,2)").Should().Be("#NUM!");
        CalcError("MDURATION(-1,44197,0.05,0.06,2)").Should().Be("#NUM!");
    }

    [Fact]
    public void OddCouponFunctions_NegativeDateSerial_ReturnNumError()
    {
        CalcError("ODDFPRICE(-1,10,-2,5,0.05,0.06,100,2)").Should().Be("#NUM!");
        CalcError("ODDFYIELD(-1,10,-2,5,0.05,100,100,2)").Should().Be("#NUM!");
        CalcError("ODDLPRICE(-1,10,-2,0.05,0.06,100,2)").Should().Be("#NUM!");
        CalcError("ODDLYIELD(-1,10,-2,0.05,100,100,2)").Should().Be("#NUM!");
    }

    // ── AMORDEGRC / AMORLINC ─────────────────────────────────────────────

    [Fact]
    public void Amorlinc_Period0_FirstYearProrated()
    {
        // Cost=2400, rate=0.3, purchased mid-year (period 0 = first year proration)
        // If purchase date = issue date = same, frac ≈ full year, dep ≈ 2400*0.3
        // date_purchased = 43831, first_period = 44197 (1 year later)
        double result = Calc("AMORLINC(2400,43831,44197,300,1,0.3,0)");
        result.Should().BeApproximately(720.0, 10.0);
    }

    [Fact]
    public void Amordegrc_ReturnsPositiveValue()
    {
        // Basic sanity: depreciation should be positive for valid inputs
        double result = Calc("AMORDEGRC(2400,43831,44197,300,1,0.2,0)");
        result.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AmorFunctions_InvalidBasis_ReturnNumError()
    {
        CalcError("AMORDEGRC(2400,43831,44197,300,1,0.2,5)").Should().Be("#NUM!");
        CalcError("AMORLINC(2400,43831,44197,300,1,0.3,-1)").Should().Be("#NUM!");
        CalcError("AMORLINC(2400,43831,44197,300,1,0.3,1E309)").Should().Be("#NUM!");
    }

    [Fact]
    public void Accrint_SettlementAfterIssue_ReturnsAccruedInterest()
    {
        double result = Calc("ACCRINT(43831,43831,44197,0.05,1000,2)");

        result.Should().BeGreaterThan(0);
        result.Should().BeLessThanOrEqualTo(1000 * 0.05);
    }

    [Fact]
    public void Accrintm_KnownMaturityAccrual_MatchesExcelBasisRules()
    {
        Calc("ACCRINTM(39539,39614,0.1,1000,3)").Should().BeApproximately(20.54794521, 1e-8);
        Calc("ACCRINTM(39539,39614,0.1,,3)").Should().BeApproximately(20.54794521, 1e-8);
        Calc("ACCRINTM(43831,44197,0.05,1000)").Should().BeApproximately(50.0, 1e-10);
        Calc("ACCRINTM(43831,44197,0.05)").Should().BeApproximately(50.0, 1e-10);
        Calc("ACCRINTM(43831,44197,0.05,1000,3)").Should().BeApproximately(50.13698630136986, 1e-10);
    }

    [Fact]
    public void Accrintm_InvalidInputs_ReturnExcelErrors()
    {
        CalcError("ACCRINTM(44197,43831,0.05,1000)").Should().Be("#NUM!");
        CalcError("ACCRINTM(43831,44197,0,1000)").Should().Be("#NUM!");
        CalcError("ACCRINTM(43831,44197,0.05,0)").Should().Be("#NUM!");
        CalcError("ACCRINTM(43831,44197,0.05,1000,5)").Should().Be("#NUM!");
        CalcError("ACCRINTM(-1,44197,0.05,1000)").Should().Be("#NUM!");
    }

    [Fact]
    public void OddCouponAndAccrualFunctions_RangeValueArgument_SpillElementwise()
    {
        var rates = new[] { (1, 1, 0.05), (2, 1, 0.06) };

        AssertApproxColumn(EvalWithData("ACCRINT(43831,43831,44197,A1:A2,1000,2)", rates), Calc("ACCRINT(43831,43831,44197,0.05,1000,2)"), Calc("ACCRINT(43831,43831,44197,0.06,1000,2)"));
        AssertApproxColumn(EvalWithData("ACCRINTM(43831,44197,A1:A2,1000)", rates), Calc("ACCRINTM(43831,44197,0.05,1000)"), Calc("ACCRINTM(43831,44197,0.06,1000)"));
        AssertApproxColumn(EvalWithData("ODDFPRICE(43900,44562,43831,44197,0.05,A1:A2,100,2)", rates), Calc("ODDFPRICE(43900,44562,43831,44197,0.05,0.05,100,2)"), Calc("ODDFPRICE(43900,44562,43831,44197,0.05,0.06,100,2)"));
        AssertApproxColumn(EvalWithData("ODDFYIELD(43900,44562,43831,44197,0.05,A1:A2,100,2)", (1, 1, 99.0), (2, 1, 101.0)), Calc("ODDFYIELD(43900,44562,43831,44197,0.05,99,100,2)"), Calc("ODDFYIELD(43900,44562,43831,44197,0.05,101,100,2)"));
        AssertApproxColumn(EvalWithData("ODDLPRICE(43900,44197,43831,0.05,A1:A2,100,2)", rates), Calc("ODDLPRICE(43900,44197,43831,0.05,0.05,100,2)"), Calc("ODDLPRICE(43900,44197,43831,0.05,0.06,100,2)"));
        AssertApproxColumn(EvalWithData("ODDLYIELD(43900,44197,43831,0.05,A1:A2,100,2)", (1, 1, 99.0), (2, 1, 101.0)), Calc("ODDLYIELD(43900,44197,43831,0.05,99,100,2)"), Calc("ODDLYIELD(43900,44197,43831,0.05,101,100,2)"));
    }

    [Fact]
    public void OddCouponFunctions_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var cells = new[]
        {
            (1, 1, 43900.0), (2, 1, 43910.0),
            (1, 2, 44562.0), (2, 2, 44592.0),
            (1, 3, 43831.0), (2, 3, 43840.0),
            (1, 4, 44197.0), (2, 4, 44228.0),
            (1, 5, 0.05), (2, 5, 0.06),
            (1, 6, 0.05), (2, 6, 0.07),
            (1, 7, 100.0), (2, 7, 110.0),
            (1, 8, 2.0), (2, 8, 4.0),
            (1, 9, 0.0), (2, 9, 1.0),
            (1, 10, 44197.0), (2, 10, 44228.0),
            (1, 11, 99.0), (2, 11, 101.0)
        };

        AssertApproxColumn(EvalWithData("ODDFPRICE(A1:A2,B1:B2,C1:C2,D1:D2,E1:E2,F1:F2,G1:G2,H1:H2,I1:I2)", cells), Calc("ODDFPRICE(43900,44562,43831,44197,0.05,0.05,100,2,0)"), Calc("ODDFPRICE(43910,44592,43840,44228,0.06,0.07,110,4,1)"));
        AssertApproxColumn(EvalWithData("ODDFYIELD(A1:A2,B1:B2,C1:C2,D1:D2,E1:E2,K1:K2,G1:G2,H1:H2,I1:I2)", cells), Calc("ODDFYIELD(43900,44562,43831,44197,0.05,99,100,2,0)"), Calc("ODDFYIELD(43910,44592,43840,44228,0.06,101,110,4,1)"));
        AssertApproxColumn(EvalWithData("ODDLPRICE(A1:A2,J1:J2,C1:C2,E1:E2,F1:F2,G1:G2,H1:H2,I1:I2)", cells), Calc("ODDLPRICE(43900,44197,43831,0.05,0.05,100,2,0)"), Calc("ODDLPRICE(43910,44228,43840,0.06,0.07,110,4,1)"));
        AssertApproxColumn(EvalWithData("ODDLYIELD(A1:A2,J1:J2,C1:C2,E1:E2,K1:K2,G1:G2,H1:H2,I1:I2)", cells), Calc("ODDLYIELD(43900,44197,43831,0.05,99,100,2,0)"), Calc("ODDLYIELD(43910,44228,43840,0.06,101,110,4,1)"));

        // Sibling no-regression: ranges that conflict on the SAME axis (neither equal nor size-1)
        // must still be a genuine #VALUE! shape mismatch -- a row-vector (B1:C1) crossed with a
        // column-vector (A1:A2) is now a valid cross-broadcast (R118-formula-arity3plus-cross-broadcast),
        // so this uses B1:B3 (a same-axis, differently-sized column) instead.
        EvalWithData("ODDFPRICE(A1:A2,B1:B3,43831,44197,0.05,0.05,100,2,0)", cells).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Accrint_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var cells = new[]
        {
            (1, 1, 43831.0), (2, 1, 43862.0),
            (1, 2, 43831.0), (2, 2, 43862.0),
            (1, 3, 44197.0), (2, 3, 44228.0),
            (1, 4, 0.05), (2, 4, 0.06),
            (1, 5, 1000.0), (2, 5, 1200.0),
            (1, 6, 2.0), (2, 6, 4.0),
            (1, 7, 0.0), (2, 7, 1.0)
        };

        AssertApproxColumn(
            EvalWithData("ACCRINT(A1:A2,B1:B2,C1:C2,D1:D2,E1:E2,F1:F2,G1:G2)", cells),
            Calc("ACCRINT(43831,43831,44197,0.05,1000,2,0)"),
            Calc("ACCRINT(43862,43862,44228,0.06,1200,4,1)"));

        EvalWithData("ACCRINT(A1:A2,B1:B3,44197,0.05,1000,2,0)", cells).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Accrintm_ParameterRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        var cells = new[]
        {
            (1, 1, 43831.0), (2, 1, 43862.0),
            (1, 2, 44197.0), (2, 2, 44228.0),
            (1, 3, 0.05), (2, 3, 0.06),
            (1, 4, 1000.0), (2, 4, 1200.0),
            (1, 5, 0.0), (2, 5, 3.0)
        };

        AssertApproxColumn(
            EvalWithData("ACCRINTM(A1:A2,B1:B2,C1:C2,D1:D2,E1:E2)", cells),
            Calc("ACCRINTM(43831,44197,0.05,1000,0)"),
            Calc("ACCRINTM(43862,44228,0.06,1200,3)"));

        EvalWithData("ACCRINTM(A1:A2,B1:B3,0.05,1000)", cells).Should().Be(ErrorValue.Value);
    }
}
