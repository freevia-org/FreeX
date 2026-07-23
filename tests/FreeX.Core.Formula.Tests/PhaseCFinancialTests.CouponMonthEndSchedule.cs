using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for R30-formula-financial-coupon-1 / -3: the coupon-schedule walk
/// (COUPPCD/COUPNCD/COUPNUM and friends) must derive every schedule candidate as an
/// offset from the ORIGINAL maturity date, not by repeatedly calling AddMonths on the
/// previous (possibly clamped) candidate. The latter drops the day-of-month for
/// month-end maturities whose backward schedule crosses a Feb-29/28 short month.
/// </summary>
public partial class PhaseCFinancialTests
{
    // ── Month-end maturity crossing February (bug case) ────────────────────

    [Fact]
    public void Couppcd_MonthEndMaturityCrossingFebruary_DoesNotDriftDayOfMonth()
    {
        // Settlement 2023-08-30, Maturity 2024-08-31, semiannual (freq=2), basis=0.
        // True backward schedule from maturity: 2024-08-31, 2024-02-29, 2023-08-31, 2023-02-28, ...
        // Settlement (2023-08-30) falls just before 2023-08-31, so the previous coupon
        // date is 2023-02-28 -- NOT the drifted 2023-08-29 that a naive
        // prev.AddMonths(-6) walk (compounding the Feb-29 clamp) would produce.
        double pcd = Calc("COUPPCD(DATE(2023,8,30),DATE(2024,8,31),2,0)");
        double expected = Calc("DATE(2023,2,28)");
        pcd.Should().Be(expected);
    }

    [Fact]
    public void Coupncd_MonthEndMaturityCrossingFebruary_DoesNotDriftDayOfMonth()
    {
        // Same schedule as above: the next coupon date after settlement 2023-08-30
        // is 2023-08-31 (one step back from maturity), not a day-drifted value.
        double ncd = Calc("COUPNCD(DATE(2023,8,30),DATE(2024,8,31),2,0)");
        double expected = Calc("DATE(2023,8,31)");
        ncd.Should().Be(expected);
    }

    [Fact]
    public void Coupnum_MonthEndMaturityCrossingFebruary_CountsAllRemainingCoupons()
    {
        // Remaining coupons after settlement 2023-08-30 up to maturity 2024-08-31:
        // 2023-08-31, 2024-02-29, 2024-08-31 => 3 coupons. A drifted walk (see
        // CoupnumScalar bug) undercounts this as 2.
        double count = Calc("COUPNUM(DATE(2023,8,30),DATE(2024,8,31),2,0)");
        count.Should().Be(3);
    }

    [Fact]
    public void Coupdaybs_MonthEndMaturityCrossingFebruary_UsesCorrectedPcd()
    {
        // With the corrected PCD (2023-02-28), COUPDAYBS (30/360 basis 0) between
        // 2023-02-28 and settlement 2023-08-30 should be 182 days, not the 1-day
        // result the drifted PCD (2023-08-29) would produce.
        double daysBs = Calc("COUPDAYBS(DATE(2023,8,30),DATE(2024,8,31),2,0)");
        daysBs.Should().Be(182);
    }

    // ── Non-month-end maturity (sibling case that already worked) ──────────

    [Fact]
    public void Couppcd_Coupncd_NonMonthEndMaturity_StillCorrect()
    {
        // Settlement 2023-09-01, Maturity 2024-01-15, semiannual (freq=2).
        // Schedule: 2024-01-15, 2023-07-15, 2023-01-15, ... none of these candidates
        // are clamped (day 15 always exists), so this must be unaffected by the fix.
        double pcd = Calc("COUPPCD(DATE(2023,9,1),DATE(2024,1,15),2,0)");
        double ncd = Calc("COUPNCD(DATE(2023,9,1),DATE(2024,1,15),2,0)");

        pcd.Should().Be(Calc("DATE(2023,7,15)"));
        ncd.Should().Be(Calc("DATE(2024,1,15)"));
    }

    [Fact]
    public void Coupnum_NonMonthEndMaturity_StillCorrect()
    {
        // Only one coupon (2024-01-15) remains after settlement 2023-09-01.
        double count = Calc("COUPNUM(DATE(2023,9,1),DATE(2024,1,15),2,0)");
        count.Should().Be(1);
    }

    // ── PRICE coupon-count parity for month-end maturities (CalcBondPrice) ──
    //
    // CalcBondPrice (BuiltInFunctions.Financial.Bonds.cs) counts the coupons between
    // settlement and maturity to build the discounted cash-flow sum. It used to walk the
    // schedule forward via d = d.AddMonths(months) starting from the next coupon date, which
    // drifts the day-of-month for a month-end maturity crossing a shorter month (the same class
    // of bug fixed in COUPNUM/COUPPCD above). Unlike DURATION/ODDFPRICE -- which gate the
    // redemption cash flow on a date-equality test and therefore silently drop the principal when
    // the schedule drifts -- CalcBondPrice adds the redemption unconditionally and only uses the
    // *count* n, which stays correct because each drifted date remains in the same calendar month
    // as the true schedule date. So there is no observable PRICE/YIELD defect; the count walk was
    // still hardened to anchor off the original maturity to keep n exact against future changes.
    //
    // These tests pin that invariant: PRICE's implied coupon count must agree with the
    // independently-anchored COUPNUM for a drift-prone month-end maturity (and for a plain one).

    // Reconstruct CalcBondPrice's clean-price formula sourcing the coupon COUNT from COUPNUM and
    // the partial-first-period fraction from COUPDAYSNC/COUPDAYS -- all anchored functions that
    // are correct independently of CalcBondPrice's internal schedule walk. If PRICE ever miscounts
    // coupons, it diverges from this reconstruction.
    private double ReconstructCleanPrice(double settlement, double maturity, double rate, double yld,
        double redemption, int frequency, int basis)
    {
        int n = (int)Calc($"COUPNUM({settlement},{maturity},{frequency},{basis})");
        double daysToNext = Calc($"COUPDAYSNC({settlement},{maturity},{frequency},{basis})");
        double daysInPeriod = Calc($"COUPDAYS({settlement},{maturity},{frequency},{basis})");
        double a = daysInPeriod > 0 ? daysToNext / daysInPeriod : 1.0;
        double c = rate / frequency * redemption;
        double y = yld / frequency;
        double price = 0;
        for (int k = 1; k <= n; k++)
            price += c / Math.Pow(1 + y, k - 1 + a);
        price += redemption / Math.Pow(1 + y, n - 1 + a);
        price -= c * (1 - a);
        return price;
    }

    [Fact]
    public void Price_MonthEndMaturityCrossingFebruary_UsesCoupnumAnchoredCount()
    {
        // Settlement 2023-08-30, Maturity 2025-08-31, semiannual (freq=2), US 30/360 (basis 0).
        // The forward schedule from the next coupon (2023-08-31) drifts the intermediate dates
        // (2024-02-29 -> 2024-08-29 -> 2025-02-28 -> 2025-08-28) because months are re-added to a
        // clamped date. COUPNUM, anchored off maturity, reports 5 remaining coupons; PRICE must
        // use the same count. A drifted miscount would make PRICE diverge from the reconstruction.
        double settlement = Calc("DATE(2023,8,30)");
        double maturity = Calc("DATE(2025,8,31)");
        Calc($"COUPNUM({settlement},{maturity},2,0)").Should().Be(5);

        double price = Calc($"PRICE({settlement},{maturity},0.05,0.06,100,2,0)");
        price.Should().BeApproximately(
            ReconstructCleanPrice(settlement, maturity, 0.05, 0.06, 100, 2, 0), 1e-9);

        // Human-readable anchor: coupon (5%) < yield (6%) => below par, and the closed-form value
        // for n=5, a=0 (30/360 makes Aug-30->Aug-31 a zero-day partial period) is ~98.14.
        // An off-by-one count (n=4 -> ~98.59, n=6 -> ~97.71) would fall outside this band.
        price.Should().BeApproximately(98.14, 0.05);
        price.Should().BeLessThan(100.0);
    }

    [Fact]
    public void Price_LeapDayMaturity_UsesCoupnumAnchoredCount()
    {
        // A Feb-29 maturity is the mirror drift case: its own anchored schedule already lands on
        // day 29 (e.g. 2024-02-29 -> 2023-08-29), and a forward walk can drift further. PRICE's
        // count must still match the anchored COUPNUM across the whole schedule.
        double settlement = Calc("DATE(2021,5,15)");
        double maturity = Calc("DATE(2024,2,29)");
        int expectedCoupons = (int)Calc($"COUPNUM({settlement},{maturity},2,1)");

        double price = Calc($"PRICE({settlement},{maturity},0.07,0.05,100,2,1)");
        price.Should().BeApproximately(
            ReconstructCleanPrice(settlement, maturity, 0.07, 0.05, 100, 2, 1), 1e-9);
        expectedCoupons.Should().Be(6);
    }

    [Fact]
    public void Price_NonMonthEndMaturity_MatchesCoupnumReconstruction()
    {
        // Sibling no-regression case: maturity 2024-01-15 has no clamped schedule candidates
        // (day 15 exists every month), so the forward and anchored walks were always identical.
        // PRICE must still agree with the COUPNUM-anchored reconstruction.
        double settlement = Calc("DATE(2020,9,1)");
        double maturity = Calc("DATE(2024,1,15)");

        double price = Calc($"PRICE({settlement},{maturity},0.08,0.05,100,2,0)");
        price.Should().BeApproximately(
            ReconstructCleanPrice(settlement, maturity, 0.08, 0.05, 100, 2, 0), 1e-9);
    }
}
