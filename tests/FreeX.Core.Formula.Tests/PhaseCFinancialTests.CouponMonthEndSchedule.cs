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
}
