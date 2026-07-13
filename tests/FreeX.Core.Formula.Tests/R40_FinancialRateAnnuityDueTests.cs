using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R40-formula-financial-rate-3-1: IPMT/PPMT/CUMIPMT/CUMPRINC used a wrong annuity-due
/// (type=1) recursion for per >= 2 - they discounted the *type=0* interest of the preceding
/// period by (1+rate), which mixes an unrelated (type=0) payment amount into a type=1
/// schedule and produces materially wrong interest/principal splits.
///
/// The correct Excel type=1 (payments at period START) behavior:
///   - per == 1: interest is always 0 (the first payment happens before any interest accrues).
///   - per >= 2: interest is charged on the balance outstanding after the previous (type=1)
///     payment, following the annuity-due recursion Owed[1] = pv + pmt, Owed[j] =
///     Owed[j-1]*(1+rate) + pmt for j >= 2, where `pmt` is the (already correct) type=1
///     payment amount from CalcPmt.
///
/// Expected values below were independently verified against a first-principles,
/// period-by-period amortization simulation (payment-then-accrue-interest), not just derived
/// algebraically from the same closed form being tested - see the PowerShell cross-check used
/// while diagnosing this finding, which also confirmed self-consistency (ending balance
/// amortizes exactly to -fv, and PMT = IPMT + PPMT for every period, for both fv=0 and fv!=0).
/// </summary>
public sealed class R40_FinancialRateAnnuityDueTests
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

    // ── IPMT ──────────────────────────────────────────────────────────────

    [Fact]
    public void Ipmt_Type1_Period1_IsZero()
    {
        // Excel: =IPMT(0.1/12,1,12,1000,0,1) = 0 - first payment has no accrued interest yet.
        Calc("IPMT(0.1/12,1,12,1000,0,1)").Should().Be(0.0);
    }

    [Fact]
    public void Ipmt_Type1_Period2_MatchesExcel()
    {
        // Excel: =IPMT(0.1/12,2,12,1000,0,1) ≈ -7.60675575292003
        // (verified via first-principles annuity-due amortization simulation, NOT the old
        // buggy "type0-per1 / (1+rate)" identity, which would have given ≈ -8.264 instead).
        Calc("IPMT(0.1/12,2,12,1000,0,1)").Should().BeApproximately(-7.60675575292003, 1e-6);
    }

    [Fact]
    public void Ipmt_Type1_LaterPeriod_MatchesExcel()
    {
        // A later period, different rate/nper/pv, to exercise the general per>=2 recursion.
        // Excel: =IPMT(0.005,24,60,50000,0,1) ≈ -162.080551113526
        Calc("IPMT(0.005,24,60,50000,0,1)").Should().BeApproximately(-162.080551113526, 1e-6);
    }

    [Fact]
    public void Ipmt_Type0_Period1AndPeriod2_Unchanged()
    {
        // No-regression: the type=0 (ordinary annuity) path must be untouched by the fix.
        Calc("IPMT(0.1/12,1,12,1000,0,0)").Should().BeApproximately(-8.33333333333333, 1e-9);
        Calc("IPMT(0.1/12,2,12,1000,0,0)").Should().BeApproximately(-7.67014538419436, 1e-6);
    }

    // ── PPMT ──────────────────────────────────────────────────────────────

    [Fact]
    public void Ppmt_Type1_Period1_EqualsFullPayment()
    {
        // Excel: =PPMT(0.1/12,1,12,1000,0,1) = PMT(0.1/12,12,1000,0,1) since IPMT=0.
        double ppmt = Calc("PPMT(0.1/12,1,12,1000,0,1)");
        double pmt = Calc("PMT(0.1/12,12,1000,0,1)");
        ppmt.Should().BeApproximately(pmt, 1e-9);
        ppmt.Should().BeApproximately(-87.1893096495966, 1e-6);
    }

    [Fact]
    public void Ppmt_Type1_Period2_MatchesExcelAndEqualsPmtMinusIpmt()
    {
        // Excel: =PPMT(0.1/12,2,12,1000,0,1) ≈ -79.5825538966766
        double ppmt = Calc("PPMT(0.1/12,2,12,1000,0,1)");
        double pmt = Calc("PMT(0.1/12,12,1000,0,1)");
        double ipmt = Calc("IPMT(0.1/12,2,12,1000,0,1)");
        ppmt.Should().BeApproximately(-79.5825538966766, 1e-6);
        ppmt.Should().BeApproximately(pmt - ipmt, 1e-9);
    }

    [Fact]
    public void Ppmt_Type0_NoRegression()
    {
        // No-regression: type=0 PPMT must equal PMT - IPMT (type=0) unchanged.
        double pmt = Calc("PMT(0.1/12,12,1000,0,0)");
        double ipmt = Calc("IPMT(0.1/12,2,12,1000,0,0)");
        double ppmt = Calc("PPMT(0.1/12,2,12,1000,0,0)");
        ppmt.Should().BeApproximately(pmt - ipmt, 1e-9);
    }

    // ── CUMIPMT ───────────────────────────────────────────────────────────

    [Fact]
    public void Cumipmt_Type1_Periods1To3_MatchesExcel()
    {
        // Excel: =CUMIPMT(0.1/12,12,1000,1,3,1) ≈ -14.5503235567011
        // (sum of the per-period IPMT(1), IPMT(2), IPMT(3) verified above/via simulation).
        Calc("CUMIPMT(0.1/12,12,1000,1,3,1)").Should().BeApproximately(-14.5503235567011, 1e-6);
    }

    [Fact]
    public void Cumipmt_Type0_NoRegression()
    {
        // No-regression: type=0 total interest = nper*PMT - PV for a fully-amortizing loan.
        double rate = 0.1 / 12;
        double nper = 12;
        double pv = 1000;
        double cumipmt = Calc($"CUMIPMT({rate},{nper},{pv},1,12,0)");
        double pmt = Calc($"PMT({rate},{nper},{pv})");
        double expectedInterest = pmt * nper + pv;
        cumipmt.Should().BeApproximately(expectedInterest, 0.01);
    }

    // ── CUMPRINC ──────────────────────────────────────────────────────────

    [Fact]
    public void Cumprinc_Type1_Periods1To3_MatchesExcel()
    {
        // Excel: =CUMPRINC(0.1/12,12,1000,1,3,1) ≈ -247.017605392089
        Calc("CUMPRINC(0.1/12,12,1000,1,3,1)").Should().BeApproximately(-247.017605392089, 1e-6);
    }

    [Fact]
    public void Cumprinc_Type1_AllPeriods_SumsToNegativePv()
    {
        // Over the full term, total principal repaid must still equal -PV, regardless of type.
        Calc("CUMPRINC(0.1/12,12,1000,1,12,1)").Should().BeApproximately(-1000, 0.01);
    }

    [Fact]
    public void Cumprinc_Type0_NoRegression()
    {
        // No-regression: type=0 total principal repaid = -PV, unchanged.
        double rate = 0.1 / 12;
        double nper = 12;
        double pv = 10000;
        double cumprinc = Calc($"CUMPRINC({rate},{nper},{pv},1,12,0)");
        cumprinc.Should().BeApproximately(-pv, 0.01);
    }

    [Fact]
    public void Cumipmt_InvalidArgs_StillReturnsNumError()
        => CalcError("CUMIPMT(-0.1,12,10000,1,12,0)").Should().Be("#NUM!");
}
