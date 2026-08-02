using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class PhaseCFinancialTests
{
    // ── EFFECT ────────────────────────────────────────────────────────────

    [Fact]
    public void Effect_AnnualRate10Pct_Monthly_ReturnsCorrect()
    {
        // EFFECT(0.1, 12) = (1 + 0.1/12)^12 - 1 ≈ 0.10471
        double result = Calc("EFFECT(0.1,12)");
        result.Should().BeApproximately(0.104713, 0.0001);
    }

    [Fact]
    public void Effect_InvalidRate_ReturnsNumError()
        => CalcError("EFFECT(0,12)").Should().Be("#NUM!");

    [Fact]
    public void Effect_InvalidNpery_ReturnsNumError()
        => CalcError("EFFECT(0.1,0)").Should().Be("#NUM!");

    // ── NOMINAL ───────────────────────────────────────────────────────────

    [Fact]
    public void RateFinancialHelpers_RangeFirstArgument_SpillElementwise()
    {
        AssertApproxColumn(
            EvalWithData("EFFECT(A1:A2,12)", (1, 1, 0.1), (2, 1, 0.2)),
            Calc("EFFECT(0.1,12)"),
            Calc("EFFECT(0.2,12)"));
        AssertApproxColumn(
            EvalWithData("NOMINAL(A1:A2,4)", (1, 1, 0.1), (2, 1, 0.2)),
            Calc("NOMINAL(0.1,4)"),
            Calc("NOMINAL(0.2,4)"));
        AssertApproxColumn(
            EvalWithData("EFFECT(A1:A2,B1:B2)", (1, 1, 0.1), (2, 1, 0.2), (1, 2, 12.0), (2, 2, 4.0)),
            Calc("EFFECT(0.1,12)"),
            Calc("EFFECT(0.2,4)"));
        AssertApproxColumn(
            EvalWithData("NOMINAL(A1:A2,B1:B2)", (1, 1, 0.1), (2, 1, 0.2), (1, 2, 4.0), (2, 2, 12.0)),
            Calc("NOMINAL(0.1,4)"),
            Calc("NOMINAL(0.2,12)"));
        AssertApproxColumn(
            EvalWithData("RRI(A1:A2,100,200)", (1, 1, 10.0), (2, 1, 20.0)),
            Calc("RRI(10,100,200)"),
            Calc("RRI(20,100,200)"));
        AssertApproxColumn(
            EvalWithData("RRI(10,A1:A2,200)", (1, 1, 100.0), (2, 1, 125.0)),
            Calc("RRI(10,100,200)"),
            Calc("RRI(10,125,200)"));
        AssertApproxColumn(
            EvalWithData("RRI(10,100,A1:A2)", (1, 1, 200.0), (2, 1, 250.0)),
            Calc("RRI(10,100,200)"),
            Calc("RRI(10,100,250)"));
        AssertApproxColumn(
            EvalWithData("PDURATION(A1:A2,100,200)", (1, 1, 0.1), (2, 1, 0.2)),
            Calc("PDURATION(0.1,100,200)"),
            Calc("PDURATION(0.2,100,200)"));
        AssertApproxColumn(
            EvalWithData("PDURATION(0.1,A1:A2,200)", (1, 1, 100.0), (2, 1, 125.0)),
            Calc("PDURATION(0.1,100,200)"),
            Calc("PDURATION(0.1,125,200)"));
        AssertApproxColumn(
            EvalWithData("PDURATION(0.1,100,A1:A2)", (1, 1, 200.0), (2, 1, 250.0)),
            Calc("PDURATION(0.1,100,200)"),
            Calc("PDURATION(0.1,100,250)"));

        EvalWithData("RRI(A1:A2,B1:B3,200)", (1, 1, 10.0), (2, 1, 20.0), (1, 2, 100.0)).Should().Be(ErrorValue.Value);
        EvalWithData("PDURATION(A1:A2,B1:B3,200)", (1, 1, 0.1), (2, 1, 0.2), (1, 2, 100.0)).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Nominal_RoundTrip()
    {
        // NOMINAL(EFFECT(r, n), n) ≈ r
        double r = 0.08;
        int n = 4;
        double effective = Calc($"EFFECT({r},{n})");
        double nominal = Calc($"NOMINAL({effective},{n})");
        nominal.Should().BeApproximately(r, 1e-9);
    }

    [Fact]
    public void Nominal_KnownValue()
    {
        // NOMINAL(0.1, 4) = ((1.1)^(1/4) - 1) * 4 ≈ 0.09645
        double result = Calc("NOMINAL(0.1,4)");
        result.Should().BeApproximately(0.096455, 0.0001);
    }

    // ── RATE convergence check (J5) ──────────────────────────────────────

    [Fact]
    public void Rate_ValidInput_ConvergesAndReturnsCorrectRate()
    {
        // RATE(60, -200, 10000) → monthly rate ≈ 0.006183
        double result = Calc("RATE(60,-200,10000)");
        result.Should().BeApproximately(0.006183, 0.0001);
    }

    [Fact]
    public void Rate_PathologicalNonConvergingInput_ReturnsNumError()
    {
        // RATE(10, 100, 100): positive payment + positive PV has no real solution.
        // Newton's method diverges and the residual |f| >> 1e-7, so Excel returns #NUM!.
        CalcError("RATE(10,100,100)").Should().Be("#NUM!");
    }

    // ── P2 regression: RATE convergence guard must be scale-relative ─────────

    [Fact]
    public void Rate_LargeLoan_ConvergesToCorrectRate()
    {
        // RATE(360, -60000, 10000000): 30-year monthly mortgage at $10M.
        // Excel returns ≈ 0.00500583 (about 0.5% per month = ~6% annual).
        // The absolute residual at the true root is ~4.77e-7 > 1e-7 in absolute terms,
        // but is tiny relative to the problem scale (~1e7).  The guard must be scale-relative
        // to avoid wrongly returning #NUM! for this legitimately converged solution.
        double result = Calc("RATE(360,-60000,10000000)");
        result.Should().BeApproximately(0.00500583, 1e-6);
    }

    [Fact]
    public void Rate_PathologicalInput_StillReturnsNumError_AfterScaledGuard()
    {
        // Confirm the scaled guard still rejects a genuine non-converger.
        // RATE(10, 100, 100): same-sign pmt and pv, no real solution.
        CalcError("RATE(10,100,100)").Should().Be("#NUM!");
    }

    // ── MIRR ─────────────────────────────────────────────────────────────

    [Fact]
    public void Mirr_ExcelDocExample()
    {
        // From Excel docs: MIRR({-120000, 39000, 30000, 21000, 37000, 46000}, 0.1, 0.12) ≈ 0.1260
        double result = CalcWithData(
            "MIRR(A1:A6,0.1,0.12)",
            (1, 1, -120000), (2, 1, 39000), (3, 1, 30000),
            (4, 1, 21000), (5, 1, 37000), (6, 1, 46000));
        result.Should().BeApproximately(0.1260, 0.0005);
    }

    // ── XIRR ─────────────────────────────────────────────────────────────

    [Fact]
    public void Xirr_SimpleOneYearInvestment_ReturnsApprox10Pct()
    {
        // Invest -100 at Jan 1 2020, receive 110 at Jan 1 2021 → XIRR ≈ 0.1
        // Date serials: Jan 1 2020 = 43831, Jan 1 2021 = 44197
        double result = CalcWithData(
            "XIRR(A1:A2,B1:B2)",
            (1, 1, -100), (2, 1, 110),
            (1, 2, 43831), (2, 2, 44197));
        result.Should().BeApproximately(0.1, 0.005);
    }

    // ── XNPV ─────────────────────────────────────────────────────────────

    [Fact]
    public void Xnpv_SimpleCase_ReturnsCorrect()
    {
        // XNPV(0.1, {-100, 110}, {43831, 44197})
        // = -100/(1.1)^0 + 110/(1.1)^1 = -100 + 100 = 0
        double result = CalcWithData(
            "XNPV(0.1,A1:A2,B1:B2)",
            (1, 1, -100), (2, 1, 110),
            (1, 2, 43831), (2, 2, 44197));
        result.Should().BeApproximately(0.0, 0.5);
    }

    [Fact]
    public void Xnpv_RateZero_ReturnsSumOfCashflows()
    {
        // At rate=0, XNPV = sum of all cashflows
        double result = CalcWithData(
            "XNPV(0,A1:A3,B1:B3)",
            (1, 1, -100), (2, 1, 60), (3, 1, 60),
            (1, 2, 43831), (2, 2, 44016), (3, 2, 44197));
        result.Should().BeApproximately(20.0, 0.01);
    }

    // ── RRI ───────────────────────────────────────────────────────────────

    [Fact]
    public void Rri_KnownValue()
    {
        // RRI(10, 100, 200) = (200/100)^(1/10) - 1 = 2^0.1 - 1 ≈ 0.07177
        double result = Calc("RRI(10,100,200)");
        result.Should().BeApproximately(0.071773, 0.00001);
    }

    [Fact]
    public void Rri_RoundTrip()
    {
        // FV = PV * (1 + RRI(nper, pv, fv))^nper
        double nper = 5, pv = 1000, fv = 1500;
        double rate = Calc($"RRI({nper},{pv},{fv})");
        double recovered = pv * Math.Pow(1 + rate, nper);
        recovered.Should().BeApproximately(fv, 0.001);
    }

    [Fact]
    public void Rri_PvZero_ReturnsNumError()
        => CalcError("RRI(10,0,200)").Should().Be("#NUM!");

    // ── PDURATION ─────────────────────────────────────────────────────────

    [Fact]
    public void Pduration_KnownValue()
    {
        // PDURATION(0.1, 100, 200) = LN(200/100)/LN(1.1) ≈ 7.273
        double result = Calc("PDURATION(0.1,100,200)");
        result.Should().BeApproximately(7.2725, 0.001);
    }

    [Fact]
    public void Pduration_InvalidInputs_ReturnsNumError()
        => CalcError("PDURATION(0,100,200)").Should().Be("#NUM!");

    // ── FVSCHEDULE ────────────────────────────────────────────────────────

    [Fact]
    public void Fvschedule_ThreeRates()
    {
        // FVSCHEDULE(100, {0.1, 0.05, 0.08}) = 100 * 1.1 * 1.05 * 1.08 = 124.74
        double result = CalcWithData(
            "FVSCHEDULE(100,A1:A3)",
            (1, 1, 0.10), (2, 1, 0.05), (3, 1, 0.08));
        result.Should().BeApproximately(124.74, 0.01);
    }

    [Fact]
    public void Effect_NominalRoundTrip_Quarterly()
    {
        double r = 0.12;
        int n = 4;
        double eff = Calc($"EFFECT({r},{n})");
        double nom = Calc($"NOMINAL({eff},{n})");
        nom.Should().BeApproximately(r, 1e-9);
    }
}
