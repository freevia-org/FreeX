using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseCFinancialTests
{
    [Fact]
    public void Ispmt_ReturnsDocumentedEvenPrincipalInterest()
    {
        Calc("ISPMT(10%/12,1,3*12,8000000)").Should().BeApproximately(-64814.8148148, 1e-6);
        Calc("ISPMT(10%,1,3,8000000)").Should().BeApproximately(-533333.333333, 1e-6);
    }

    [Fact]
    public void Ispmt_CountsPeriodsFromZeroAndSpillsRangeArguments()
    {
        Calc("ISPMT(10%,0,3,8000000)").Should().BeApproximately(-800000, 1e-9);
        AssertApproxColumn(
            EvalWithData("ISPMT(10%,A1:A2,3,8000000)", (1, 1, 0.0), (2, 1, 1.0)),
            Calc("ISPMT(10%,0,3,8000000)"),
            Calc("ISPMT(10%,1,3,8000000)"));
    }

    [Fact]
    public void Ispmt_InvalidPeriodOrNper_ReturnsNumError()
    {
        CalcError("ISPMT(10%,-1,3,8000000)").Should().Be("#NUM!");
        CalcError("ISPMT(10%,4,3,8000000)").Should().Be("#NUM!");
        CalcError("ISPMT(10%,1,0,8000000)").Should().Be("#NUM!");
    }

    [Fact]
    public void Ipmt_Period1_ReturnsExpectedInterest()
    {
        // Monthly rate 0.1/12, 12 periods, PV 10000
        // PMT = -879.159..., IPMT period 1 = 10000 * 0.1/12 = -83.333...
        double ipmt = Calc("IPMT(0.1/12,1,12,10000)");
        ipmt.Should().BeApproximately(-83.333333, 0.001);
    }

    [Fact]
    public void PaymentFinancialFunctions_RangePeriodAndNperArguments_SpillElementwise()
    {
        AssertApproxColumn(
            EvalWithData("IPMT(0.1/12,A1:A2,12,10000)", (1, 1, 1.0), (2, 1, 2.0)),
            Calc("IPMT(0.1/12,1,12,10000)"),
            Calc("IPMT(0.1/12,2,12,10000)"));
        AssertApproxColumn(
            EvalWithData("PPMT(0.1/12,A1:A2,12,10000)", (1, 1, 1.0), (2, 1, 2.0)),
            Calc("PPMT(0.1/12,1,12,10000)"),
            Calc("PPMT(0.1/12,2,12,10000)"));
        AssertApproxColumn(
            EvalWithData("RATE(A1:A2,-188.71,10000)", (1, 1, 60.0), (2, 1, 72.0)),
            Calc("RATE(60,-188.71,10000)"),
            Calc("RATE(72,-188.71,10000)"));
    }

    [Fact]
    public void CorePaymentFunctions_TrailingRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        AssertApproxColumn(
            EvalWithData("PMT(0.05/12,A1:A2,10000)", (1, 1, 60.0), (2, 1, 72.0)),
            Calc("PMT(0.05/12,60,10000)"),
            Calc("PMT(0.05/12,72,10000)"));
        AssertApproxColumn(
            EvalWithData("PV(0.05/12,A1:A2,188.71)", (1, 1, 60.0), (2, 1, 72.0)),
            Calc("PV(0.05/12,60,188.71)"),
            Calc("PV(0.05/12,72,188.71)"));
        AssertApproxColumn(
            EvalWithData("FV(0.05/12,A1:A2,-100)", (1, 1, 60.0), (2, 1, 72.0)),
            Calc("FV(0.05/12,60,-100)"),
            Calc("FV(0.05/12,72,-100)"));
        AssertApproxColumn(
            EvalWithData("NPER(0.05/12,A1:A2,10000)", (1, 1, -188.71), (2, 1, -200.0)),
            Calc("NPER(0.05/12,-188.71,10000)"),
            Calc("NPER(0.05/12,-200,10000)"));
        AssertApproxColumn(
            EvalWithData("RATE(A1:A2,B1:B2,10000)", (1, 1, 60.0), (2, 1, 72.0), (1, 2, -188.71), (2, 2, -200.0)),
            Calc("RATE(60,-188.71,10000)"),
            Calc("RATE(72,-200,10000)"));
        AssertApproxColumn(
            EvalWithData("PMT(0.05/12,60,10000,A1:A2,B1:B2)", (1, 1, 0.0), (2, 1, 500.0), (1, 2, 0.0), (2, 2, 1.0)),
            Calc("PMT(0.05/12,60,10000,0,0)"),
            Calc("PMT(0.05/12,60,10000,500,1)"));
        AssertApproxColumn(
            EvalWithData("RATE(60,-188.71,10000,0,0,A1:A2)", (1, 1, 0.01), (2, 1, 0.05)),
            Calc("RATE(60,-188.71,10000,0,0,0.01)"),
            Calc("RATE(60,-188.71,10000,0,0,0.05)"));

        EvalWithData("PMT(0.05/12,A1:A2,B1:C1)", (1, 1, 60.0), (2, 1, 72.0), (1, 2, 10000.0), (1, 3, 12000.0)).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void IpmtAndPpmt_TrailingRangeArguments_SpillElementwiseOrReturnValueForShapeMismatch()
    {
        AssertApproxColumn(
            EvalWithData("IPMT(0.05/12,1,A1:A2,B1:B2)", (1, 1, 60.0), (2, 1, 72.0), (1, 2, 10000.0), (2, 2, 12000.0)),
            Calc("IPMT(0.05/12,1,60,10000)"),
            Calc("IPMT(0.05/12,1,72,12000)"));
        AssertApproxColumn(
            EvalWithData("PPMT(0.05/12,1,A1:A2,B1:B2)", (1, 1, 60.0), (2, 1, 72.0), (1, 2, 10000.0), (2, 2, 12000.0)),
            Calc("PPMT(0.05/12,1,60,10000)"),
            Calc("PPMT(0.05/12,1,72,12000)"));
        AssertApproxColumn(
            EvalWithData("IPMT(0.05/12,1,60,10000,A1:A2,B1:B2)", (1, 1, 0.0), (2, 1, 500.0), (1, 2, 0.0), (2, 2, 1.0)),
            Calc("IPMT(0.05/12,1,60,10000,0,0)"),
            Calc("IPMT(0.05/12,1,60,10000,500,1)"));
        AssertApproxColumn(
            EvalWithData("PPMT(0.05/12,1,60,10000,A1:A2,B1:B2)", (1, 1, 0.0), (2, 1, 500.0), (1, 2, 0.0), (2, 2, 1.0)),
            Calc("PPMT(0.05/12,1,60,10000,0,0)"),
            Calc("PPMT(0.05/12,1,60,10000,500,1)"));

        EvalWithData("IPMT(0.05/12,1,A1:A2,B1:C1)", (1, 1, 60.0), (2, 1, 72.0), (1, 2, 10000.0), (1, 3, 12000.0)).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Ipmt_PmtEqualsIpmtPlusPpmt_AllPeriods()
    {
        // For a standard loan, PMT = IPMT + PPMT for every period
        double rate = 0.1 / 12;
        double nper = 12;
        double pv = 10000;
        double pmt = Calc($"PMT({rate},{nper},{pv})");
        for (int per = 1; per <= 12; per++)
        {
            double ipmt = Calc($"IPMT({rate},{per},{nper},{pv})");
            double ppmt = Calc($"PPMT({rate},{per},{nper},{pv})");
            (ipmt + ppmt).Should().BeApproximately(pmt, 1e-6,
                $"PMT = IPMT + PPMT should hold for period {per}");
        }
    }

    [Fact]
    public void Ipmt_InvalidPeriod_ReturnsNumError()
        => CalcError("IPMT(0.1,0,12,10000)").Should().Be("#NUM!");

    [Fact]
    public void Ipmt_PeriodExceedsNper_ReturnsNumError()
        => CalcError("IPMT(0.1,13,12,10000)").Should().Be("#NUM!");

    // ── PPMT ─────────────────────────────────────────────────────────────

    [Fact]
    public void Ppmt_Period1_ReturnsExpectedPrincipal()
    {
        // PMT - IPMT
        double pmt  = Calc("PMT(0.1/12,12,10000)");
        double ipmt = Calc("IPMT(0.1/12,1,12,10000)");
        double ppmt = Calc("PPMT(0.1/12,1,12,10000)");
        ppmt.Should().BeApproximately(pmt - ipmt, 1e-9);
    }

    // ── CUMIPMT ───────────────────────────────────────────────────────────

    [Fact]
    public void Cumipmt_AllPeriods_SumEqualsNperTimesPmtMinusPrincipal()
    {
        // Total interest = nper * PMT - PV (for FV=0)
        double rate = 0.1 / 12;
        double nper = 12;
        double pv = 10000;
        double cumipmt = Calc($"CUMIPMT({rate},{nper},{pv},1,12,0)");
        double pmt = Calc($"PMT({rate},{nper},{pv})");
        double expectedInterest = pmt * nper + pv; // pmt is negative, so pmt*nper + pv = total interest paid
        cumipmt.Should().BeApproximately(expectedInterest, 0.01);
    }

    [Fact]
    public void Cumipmt_InvalidArgs_ReturnsNumError()
        => CalcError("CUMIPMT(-0.1,12,10000,1,12,0)").Should().Be("#NUM!");

    // ── CUMPRINC ──────────────────────────────────────────────────────────

    [Fact]
    public void CumulativePaymentFunctions_RangeStartPeriodArgument_SpillElementwise()
    {
        AssertApproxColumn(
            EvalWithData("CUMIPMT(0.1/12,12,10000,A1:A2,12,0)", (1, 1, 1.0), (2, 1, 2.0)),
            Calc("CUMIPMT(0.1/12,12,10000,1,12,0)"),
            Calc("CUMIPMT(0.1/12,12,10000,2,12,0)"));
        AssertApproxColumn(
            EvalWithData("CUMPRINC(0.1/12,12,10000,A1:A2,12,0)", (1, 1, 1.0), (2, 1, 2.0)),
            Calc("CUMPRINC(0.1/12,12,10000,1,12,0)"),
            Calc("CUMPRINC(0.1/12,12,10000,2,12,0)"));
    }

    [Fact]
    public void Cumprinc_AllPeriods_SumApproxNegativePV()
    {
        // Over all periods, total principal repaid = -PV
        double rate = 0.1 / 12;
        double nper = 12;
        double pv = 10000;
        double cumprinc = Calc($"CUMPRINC({rate},{nper},{pv},1,12,0)");
        cumprinc.Should().BeApproximately(-pv, 0.01);
    }
}
