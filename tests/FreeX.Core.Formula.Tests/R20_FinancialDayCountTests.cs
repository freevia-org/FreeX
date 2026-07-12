using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for R20-financial-functions-1 and R20-financial-functions-2:
///   (1) DayCountFraction basis=1 (Actual/Actual) used to collapse to the bare integer
///       calendar-year difference for any cross-year span, because
///       days / ActualYearLength(d1,d2) == days / (days/years) == years algebraically.
///       This corrupted DISC/INTRATE/RECEIVED/ACCRINT/ACCRINTM/PRICEDISC/YIELDDISC/
///       PRICEMAT/YIELDMAT whenever basis=1 spanned a Dec31-&gt;Jan1 boundary.
///   (2) CalcBondPrice (PRICE/YIELD) and DurationScalar (DURATION/MDURATION) never read
///       the basis parameter for the coupon-period fraction, always using actual-calendar
///       days regardless of basis 0/2/3/4, so PRICE/YIELD/DURATION/MDURATION returned
///       identical numbers for every basis value.
/// </summary>
public sealed class R20_financial_daycount_Tests
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

    // ── R20-financial-functions-1: DISC basis=1 across a year boundary ─────────

    [Fact]
    public void Disc_Basis1_AcrossYearBoundary_UsesTrueActualActualFraction_NotBareYearDiff()
    {
        // 31-day T-bill spanning New Year's: settlement 2023-12-15, maturity 2024-01-15.
        // True Actual/Actual day-count fraction splits at the calendar-year boundary:
        //   17 days of 2023 (365-day year) + 14 days of 2024 (366-day leap year)
        //   = 17/365 + 14/366 ≈ 0.08482670859
        // DISC = (redemption - price) / redemption / dcf = (100-99)/100/dcf ≈ 0.11788740
        //
        // The pre-fix bug computed ActualYearLength = days/years = 31/1 = 31, so
        // DayCountFraction = 31/31 = 1.0 exactly (the day span cancels out), giving
        // DISC = 0.01/1.0 = 0.01 -- off by more than 11x.
        double disc = Calc("DISC(DATE(2023,12,15),DATE(2024,1,15),99,100,1)");

        disc.Should().BeApproximately(0.1178873985, 1e-6);
        // Guard against regressing to the old bare-year-difference bug (which yields exactly 0.01).
        disc.Should().BeGreaterThan(0.05);
    }

    [Fact]
    public void Disc_Basis1_AcrossYearBoundary_DiffersFromBasis3Actual365()
    {
        // Same dates, basis=3 (Actual/365) always divides by a flat 365, so it must
        // differ from the true Actual/Actual (basis=1) result, which weights the 14
        // days that fall in leap-year 2024 by 366 instead of 365.
        double discActualActual = Calc("DISC(DATE(2023,12,15),DATE(2024,1,15),99,100,1)");
        double discActual365 = Calc("DISC(DATE(2023,12,15),DATE(2024,1,15),99,100,3)");

        // basis=3: dcf = 31/365 = 0.0849315068... -> DISC = 0.01/dcf
        discActual365.Should().BeApproximately(0.01 / (31.0 / 365.0), 1e-6);
        discActualActual.Should().NotBe(discActual365);
    }

    [Fact]
    public void Accrint_Basis1_AcrossYearBoundary_UsesTrueActualActualFraction()
    {
        // ACCRINT = par * rate * DayCountFraction(issue, settlement, basis).
        // issue=2023-12-15, settlement=2024-01-15 (same cross-year span as above).
        double accrued = Calc("ACCRINT(DATE(2023,12,15),DATE(2024,7,15),DATE(2024,1,15),0.05,1000,2,1)");
        double expectedDcf = 17.0 / 365.0 + 14.0 / 366.0;
        double expected = 1000 * 0.05 * expectedDcf;

        accrued.Should().BeApproximately(expected, 1e-6);
    }

    // ── R20-financial-functions-2: PRICE/DURATION basis-aware coupon fraction ──

    [Fact]
    public void Price_Basis0Vs1_ProduceDifferentResults_ForSamePeriodMidSettlement()
    {
        // Single remaining coupon period Jan1-Jul1 2023 (semiannual), settlement mid-period
        // on Apr 1 2023, bond matures exactly at the next coupon date (Jul 1 2023).
        //   basis=0 (US 30/360): E = Days30360Us(Jan1,Jul1) = 180, DSC = Days30360Us(Apr1,Jul1) = 90
        //                        -> a = 90/180 = 0.5 exactly
        //   basis=1 (Actual/Actual): E = actual days Jan1->Jul1 = 181, DSC = actual days Apr1->Jul1 = 91
        //                        -> a = 91/181 ≈ 0.50276243
        // Dirty price = (coupon + redemption) / (1+y)^a for a single-period bond; PRICE()
        // then subtracts the accrued interest for the stub period (coupon * (1-a)) to quote
        // Excel's clean price, so the two bases must yield distinct, computable clean prices.
        // (R30-formula-financial-coupon-2: values below are clean, not dirty, prices.)
        double priceBasis0 = Calc("PRICE(DATE(2023,4,1),DATE(2023,7,1),0.05,0.06,100,2,0)");
        double priceBasis1 = Calc("PRICE(DATE(2023,4,1),DATE(2023,7,1),0.05,0.06,100,2,1)");

        priceBasis0.Should().BeApproximately(99.74625101184004, 1e-9);
        priceBasis1.Should().BeApproximately(99.7449106628569, 1e-9);
        priceBasis0.Should().NotBe(priceBasis1);
    }

    [Fact]
    public void Duration_Basis0Vs2_ProduceDifferentResults_ForSamePeriodMidSettlement()
    {
        // Same coupon-period setup as above, but exercised through DURATION so the fix to
        // DurationScalar's basis-aware fraction is independently covered (not just CalcBondPrice).
        // basis=0 and basis=2 (Actual/360) share the same fixed period length (E=360/frequency)
        // but differ in DSC (30/360-counted vs actual-days-counted), so the coupon-period
        // fraction 'a' -- and therefore the resulting duration -- must differ between them.
        double durationBasis0 = Calc("DURATION(DATE(2023,4,1),DATE(2023,7,1),0.05,0.06,2,0)");
        double durationBasis2 = Calc("DURATION(DATE(2023,4,1),DATE(2023,7,1),0.05,0.06,2,2)");

        durationBasis0.Should().NotBe(durationBasis2);
    }
}
