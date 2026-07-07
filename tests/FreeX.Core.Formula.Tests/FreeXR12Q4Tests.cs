using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-12 fix bucket Q4: COUPDAYS/COUPDAYBS/COUPDAYSNC day-count-basis
/// handling for the default (US 30/360, basis 0), Actual/360 (basis 2), and
/// European 30/360 (basis 4) bases.
/// </summary>
public sealed class FreeXR12Q4Tests
{
    private readonly FormulaEvaluator _eval = new();

    // R12-formula-financial-stat-2: COUPDAYS with default basis 0 (30/360)
    // must return 360/frequency, not 365/frequency. Excel returns 180 for
    // COUPDAYS(DATE(2020,1,15), DATE(2025,1,15), 2) with the default basis.
    [Fact]
    public void Coupdays_DefaultBasisUses360DayYear()
    {
        Number("COUPDAYS(DATE(2020,1,15),DATE(2025,1,15),2)").Should().Be(180.0);
    }

    [Theory]
    [InlineData("COUPDAYS(DATE(2020,1,15),DATE(2025,1,15),2,0)", 180.0)] // US 30/360
    [InlineData("COUPDAYS(DATE(2020,1,15),DATE(2025,1,15),2,2)", 180.0)] // Actual/360
    [InlineData("COUPDAYS(DATE(2020,1,15),DATE(2025,1,15),2,4)", 180.0)] // European 30/360
    [InlineData("COUPDAYS(DATE(2020,1,15),DATE(2025,1,15),2,3)", 182.5)] // Actual/365
    public void Coupdays_UsesExcelDayCountPerBasis(string formula, double expected)
    {
        Number(formula).Should().Be(expected);
    }

    // R12-formula-financial-stat-3: COUPDAYBS must use 30/360 day counting
    // (not actual calendar days) for basis 0 and basis 4. For settlement
    // 2020-03-31 / maturity 2025-08-31 / frequency 2, the previous coupon
    // date is 2020-02-28, so actual calendar days = 32 but Excel's 30/360
    // day count = 33.
    [Fact]
    public void Coupdaybs_DefaultBasisUses30360DayCount()
    {
        Number("COUPDAYBS(DATE(2020,3,31),DATE(2025,8,31),2)").Should().Be(33.0);
    }

    [Fact]
    public void Coupdaybs_Basis1UsesActualCalendarDays()
    {
        Number("COUPDAYBS(DATE(2020,3,31),DATE(2025,8,31),2,1)").Should().Be(32.0);
    }

    [Fact]
    public void Coupdaybs_Basis4Uses30360EuropeanDayCount()
    {
        // European 30/360 clamps both day-31 endpoints unconditionally (dd1=31->30
        // here), giving 32 versus the US-basis 33 for the same settlement/pcd pair.
        Number("COUPDAYBS(DATE(2020,3,31),DATE(2025,8,31),2,4)").Should().Be(32.0);
    }

    // COUPDAYSNC has the same defect: basis 0/4 must use 30/360 day counting
    // for the settlement-to-next-coupon span, not actual calendar days.
    [Fact]
    public void Coupdaysnc_DefaultBasisUses30360DayCount()
    {
        // Next coupon after 2020-03-31 (freq=2, maturity 2025-08-31) is 2020-08-28.
        // Actual days = 150; 30/360 US day count (31st clamped to 30th) = 148.
        Number("COUPDAYSNC(DATE(2020,3,31),DATE(2025,8,31),2)").Should().Be(148.0);
    }

    [Fact]
    public void Coupdaysnc_Basis1UsesActualCalendarDays()
    {
        Number("COUPDAYSNC(DATE(2020,3,31),DATE(2025,8,31),2,1)").Should().Be(150.0);
    }

    private double Number(string formula)
    {
        var result = _eval.Evaluate("=" + formula, Sheet());
        result.Should().BeOfType<NumberValue>();
        return ((NumberValue)result).Value;
    }

    private static Sheet Sheet() => new(SheetId.New(), "S");
}
