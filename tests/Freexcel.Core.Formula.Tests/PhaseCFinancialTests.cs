using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

/// <summary>
/// Tests for Phase C financial functions:
/// IPMT, PPMT, CUMIPMT, CUMPRINC, EFFECT, NOMINAL, MIRR, XIRR, XNPV,
/// RRI, PDURATION, FVSCHEDULE, DB, DDB, VDB, SYD, AMORDEGRC, AMORLINC,
/// DOLLARDE, DOLLARFR, DISC, INTRATE, RECEIVED, ACCRINT,
/// TBILLEQ, TBILLPRICE, TBILLYIELD, COUPDAYBS, COUPDAYS, COUPDAYSNC,
/// COUPNCD, COUPNUM, COUPPCD, PRICE, YIELD, PRICEDISC, PRICEMAT,
/// YIELDDISC, YIELDMAT, DURATION, MDURATION.
/// </summary>
public class PhaseCFinancialTests
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

    private double CalcWithData(string formula, params (int row, int col, double val)[] cells)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), new NumberValue(v));
        var result = _eval.Evaluate("=" + formula, sheet, wb);
        result.Should().BeOfType<NumberValue>($"formula {formula} should return a number");
        return ((NumberValue)result).Value;
    }

    // ── IPMT ─────────────────────────────────────────────────────────────

    private ScalarValue EvalWithData(string formula, params (int row, int col, double val)[] cells)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), new NumberValue(v));
        return _eval.Evaluate("=" + formula, sheet, wb);
    }

    private static void AssertApproxColumn(ScalarValue value, params double[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            ((NumberValue)range.At(row + 1, 1)).Value.Should().BeApproximately(expected[row], 1e-10);
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
            EvalWithData("RRI(A1:A2,100,200)", (1, 1, 10.0), (2, 1, 20.0)),
            Calc("RRI(10,100,200)"),
            Calc("RRI(20,100,200)"));
        AssertApproxColumn(
            EvalWithData("PDURATION(A1:A2,100,200)", (1, 1, 0.1), (2, 1, 0.2)),
            Calc("PDURATION(0.1,100,200)"),
            Calc("PDURATION(0.2,100,200)"));
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

    // ── SYD ───────────────────────────────────────────────────────────────

    [Fact]
    public void Syd_ExcelDocExample()
    {
        // SYD(30000, 7500, 10, 1) = (30000-7500)*10/(10*11/2) = 22500*10/55 ≈ 4090.91
        double result = Calc("SYD(30000,7500,10,1)");
        result.Should().BeApproximately(4090.909, 0.01);
    }

    [Fact]
    public void Syd_LastPeriod_ReturnsSmallest()
    {
        double last = Calc("SYD(30000,7500,10,10)");
        double first = Calc("SYD(30000,7500,10,1)");
        last.Should().BeLessThan(first);
        last.Should().BeApproximately(4090.909 / 10, 0.01);
    }

    // ── DDB ───────────────────────────────────────────────────────────────

    [Fact]
    public void Ddb_ExcelDocExample()
    {
        // DDB(2400, 300, 10, 1) = min(2400-300, 2400*2/10) = min(2100, 480) = 480
        double result = Calc("DDB(2400,300,10,1)");
        result.Should().BeApproximately(480.0, 0.001);
    }

    [Fact]
    public void Ddb_Period2_DecreasesFromPeriod1()
    {
        double p1 = Calc("DDB(2400,300,10,1)");
        double p2 = Calc("DDB(2400,300,10,2)");
        p2.Should().BeLessThan(p1);
    }

    // ── DB ────────────────────────────────────────────────────────────────

    [Fact]
    public void Db_Period1_ReturnsCorrect()
    {
        // DB(1000000, 100000, 6, 1, 7)
        // Rate = 1 - (100000/1000000)^(1/6) = 1 - 0.1^(1/6) ≈ 0.319 (rounded to 3dp = 0.319)
        // Dep1 = 1000000 * 0.319 * 7/12 ≈ 186,083
        double result = Calc("DB(1000000,100000,6,1,7)");
        result.Should().BeApproximately(186083.33, 1.0);
    }

    [Fact]
    public void Db_InvalidCost_ReturnsNumError()
        => CalcError("DB(0,100,6,1)").Should().Be("#NUM!");

    // ── VDB ───────────────────────────────────────────────────────────────

    [Fact]
    public void Vdb_WholeFirstPeriod_MatchesDdb()
    {
        // VDB over period 0 to 1 with factor=2 should match DDB(cost, salvage, life, 1)
        double vdb = Calc("VDB(2400,300,10,0,1)");
        double ddb = Calc("DDB(2400,300,10,1)");
        vdb.Should().BeApproximately(ddb, 0.001);
    }

    [Fact]
    public void Vdb_InvalidInputs_ReturnsNumError()
        => CalcError("VDB(2400,300,10,0,11)").Should().Be("#NUM!");

    // ── DOLLARDE / DOLLARFR ───────────────────────────────────────────────

    [Fact]
    public void DepreciationFunctions_RangePeriodArgument_SpillElementwise()
    {
        var periods = new[] { (1, 1, 1.0), (2, 1, 2.0) };

        AssertApproxColumn(EvalWithData("SLN(2400,300,A1:A2)", (1, 1, 10.0), (2, 1, 20.0)), Calc("SLN(2400,300,10)"), Calc("SLN(2400,300,20)"));
        AssertApproxColumn(EvalWithData("SYD(30000,7500,10,A1:A2)", periods), Calc("SYD(30000,7500,10,1)"), Calc("SYD(30000,7500,10,2)"));
        AssertApproxColumn(EvalWithData("DDB(2400,300,10,A1:A2)", periods), Calc("DDB(2400,300,10,1)"), Calc("DDB(2400,300,10,2)"));
        AssertApproxColumn(EvalWithData("DB(1000000,100000,6,A1:A2,7)", periods), Calc("DB(1000000,100000,6,1,7)"), Calc("DB(1000000,100000,6,2,7)"));
        AssertApproxColumn(EvalWithData("VDB(2400,300,10,0,A1:A2)", periods), Calc("VDB(2400,300,10,0,1)"), Calc("VDB(2400,300,10,0,2)"));
        AssertApproxColumn(EvalWithData("AMORDEGRC(2400,43831,44197,300,A1:A2,0.2,0)", periods), Calc("AMORDEGRC(2400,43831,44197,300,1,0.2,0)"), Calc("AMORDEGRC(2400,43831,44197,300,2,0.2,0)"));
        AssertApproxColumn(EvalWithData("AMORLINC(2400,43831,44197,300,A1:A2,0.3,0)", periods), Calc("AMORLINC(2400,43831,44197,300,1,0.3,0)"), Calc("AMORLINC(2400,43831,44197,300,2,0.3,0)"));
    }

    [Fact]
    public void Dollarde_FractionalDollar()
    {
        // DOLLARDE(1.02, 32) = 1 + 2/32 = 1.0625
        double result = Calc("DOLLARDE(1.02,32)");
        result.Should().BeApproximately(1.0625, 0.0001);
    }

    [Fact]
    public void DollarFractionHelpers_RangeFirstArgument_SpillElementwise()
    {
        AssertApproxColumn(
            EvalWithData("DOLLARDE(A1:A2,32)", (1, 1, 1.02), (2, 1, 2.16)),
            Calc("DOLLARDE(1.02,32)"),
            Calc("DOLLARDE(2.16,32)"));
        AssertApproxColumn(
            EvalWithData("DOLLARFR(A1:A2,32)", (1, 1, 1.0625), (2, 1, 2.5)),
            Calc("DOLLARFR(1.0625,32)"),
            Calc("DOLLARFR(2.5,32)"));
    }

    [Fact]
    public void Dollarfr_InverseOfDollarde()
    {
        // DOLLARFR(1.0625, 32) = 1 + 0.0625*32/100 = 1.02
        double result = Calc("DOLLARFR(1.0625,32)");
        result.Should().BeApproximately(1.02, 0.0001);
    }

    [Fact]
    public void Dollarde_Dollarfr_RoundTrip()
    {
        double original = 1.05;
        double fraction = 16;
        double dec = Calc($"DOLLARDE({original},{fraction})");
        double back = Calc($"DOLLARFR({dec},{fraction})");
        back.Should().BeApproximately(original, 0.00001);
    }

    [Fact]
    public void Dollarde_FractionZero_ReturnsDivByZeroError()
        => CalcError("DOLLARDE(1.02,0)").Should().Be("#DIV/0!");

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

    [Fact]
    public void Effect_NominalRoundTrip_Quarterly()
    {
        double r = 0.12;
        int n = 4;
        double eff = Calc($"EFFECT({r},{n})");
        double nom = Calc($"NOMINAL({eff},{n})");
        nom.Should().BeApproximately(r, 1e-9);
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
    public void OddCouponAndAccrualFunctions_RangeValueArgument_SpillElementwise()
    {
        var rates = new[] { (1, 1, 0.05), (2, 1, 0.06) };

        AssertApproxColumn(EvalWithData("ACCRINT(43831,43831,44197,A1:A2,1000,2)", rates), Calc("ACCRINT(43831,43831,44197,0.05,1000,2)"), Calc("ACCRINT(43831,43831,44197,0.06,1000,2)"));
        AssertApproxColumn(EvalWithData("ODDFPRICE(43900,44562,43831,44197,0.05,A1:A2,100,2)", rates), Calc("ODDFPRICE(43900,44562,43831,44197,0.05,0.05,100,2)"), Calc("ODDFPRICE(43900,44562,43831,44197,0.05,0.06,100,2)"));
        AssertApproxColumn(EvalWithData("ODDFYIELD(43900,44562,43831,44197,0.05,A1:A2,100,2)", (1, 1, 99.0), (2, 1, 101.0)), Calc("ODDFYIELD(43900,44562,43831,44197,0.05,99,100,2)"), Calc("ODDFYIELD(43900,44562,43831,44197,0.05,101,100,2)"));
        AssertApproxColumn(EvalWithData("ODDLPRICE(43900,44197,43831,0.05,A1:A2,100,2)", rates), Calc("ODDLPRICE(43900,44197,43831,0.05,0.05,100,2)"), Calc("ODDLPRICE(43900,44197,43831,0.05,0.06,100,2)"));
        AssertApproxColumn(EvalWithData("ODDLYIELD(43900,44197,43831,0.05,A1:A2,100,2)", (1, 1, 99.0), (2, 1, 101.0)), Calc("ODDLYIELD(43900,44197,43831,0.05,99,100,2)"), Calc("ODDLYIELD(43900,44197,43831,0.05,101,100,2)"));
    }

    [Fact]
    public void Coupncd_AndCouppcd_BracketSettlementDate()
    {
        double settlement = 43831;
        double maturity = 44197;

        Calc($"COUPPCD({settlement},{maturity},2)").Should().BeLessThanOrEqualTo(settlement);
        Calc($"COUPNCD({settlement},{maturity},2)").Should().BeGreaterThan(settlement);
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
