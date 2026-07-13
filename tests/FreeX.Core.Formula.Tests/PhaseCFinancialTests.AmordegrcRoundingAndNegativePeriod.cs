using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for round-38 findings:
///   R38-formula-financial-depreciation-1: AMORDEGRC must ROUND(dep,0) each period before
///     subtracting from the running book value, and apply the near-end-of-life "50% of book
///     value" tail rule instead of the previous naive clamp-to-salvage approach.
///   R38-formula-financial-depreciation-2: AMORDEGRC / AMORLINC must return #NUM! for a
///     negative period instead of silently returning 0.
/// </summary>
public partial class PhaseCFinancialTests
{
    // ── R38-formula-financial-depreciation-1 ────────────────────────────────

    [Fact]
    public void Amordegrc_DocumentedExample_MatchesExcelExactSchedule()
    {
        // AMORDEGRC(2400, DATE(1998,8,19), DATE(1998,12,31), 300, 1, 0.15, 1) = 776
        // This is Excel's own documented example; it only reproduces exactly when each
        // period's depreciation is ROUND(...,0) before being subtracted from book value.
        double result = Calc("AMORDEGRC(2400,DATE(1998,8,19),DATE(1998,12,31),300,1,0.15,1)");
        result.Should().Be(776.0);
    }

    [Fact]
    public void Amordegrc_Period0_ReturnsRoundedFirstPeriodAmount()
    {
        // Period 0 is the initial (pro-rated) period, before the main declining-balance loop
        // runs at all; it must still be rounded to a whole currency unit.
        double result = Calc("AMORDEGRC(2400,DATE(1998,8,19),DATE(1998,12,31),300,0,0.15,1)");
        result.Should().Be(330.0);
        (result % 1).Should().Be(0, "each AMORDEGRC period must be rounded to a whole currency unit");
    }

    // ── R38-formula-financial-depreciation-2 ────────────────────────────────

    [Theory]
    [InlineData("AMORDEGRC(2400,DATE(1998,8,19),DATE(1998,12,31),300,-1,0.15,1)")]
    [InlineData("AMORLINC(2400,DATE(1998,8,19),DATE(1998,12,31),300,-1,0.15,1)")]
    public void AmordegrcAndAmorlinc_NegativePeriod_ReturnsNumError(string formula)
        => CalcError(formula).Should().Be("#NUM!");

    // ── No-regression: normal schedules still match Excel ───────────────────

    [Fact]
    public void Amorlinc_DocumentedExample_StillMatchesExcel()
    {
        // AMORLINC(2400, DATE(1998,8,19), DATE(1998,12,31), 300, 1, 0.15, 1)
        // = ROUND(300*15%*130/365,0) accrued for first partial period, then a full annual
        // straight-line slice of 2400*0.15=360 for period 1 (clamped to remaining basis).
        double period0 = Calc("AMORLINC(2400,DATE(1998,8,19),DATE(1998,12,31),300,0,0.15,1)");
        double period1 = Calc("AMORLINC(2400,DATE(1998,8,19),DATE(1998,12,31),300,1,0.15,1)");
        period1.Should().BeApproximately(360.0, 0.5);
        period0.Should().BeGreaterThan(0).And.BeLessThan(period1);
    }

    [Fact]
    public void Amordegrc_NonNegativePeriod_StillProducesDecreasingSchedule()
    {
        // A normal, well within-life schedule should still behave sensibly: period 2's
        // depreciation should be less than or equal to period 1's (declining balance).
        double p1 = Calc("AMORDEGRC(2400,DATE(1998,8,19),DATE(1998,12,31),300,1,0.15,1)");
        double p2 = Calc("AMORDEGRC(2400,DATE(1998,8,19),DATE(1998,12,31),300,2,0.15,1)");
        p2.Should().BeLessThanOrEqualTo(p1);
    }
}
