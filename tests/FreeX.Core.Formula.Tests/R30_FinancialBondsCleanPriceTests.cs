using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R30-formula-financial-coupon-2: PRICE() must return Excel's quoted clean price
/// (dirty price minus accrued interest for the stub period), not the dirty price.
/// </summary>
public partial class PhaseCFinancialTests
{
    [Fact]
    public void Price_MsDocsExample_ReturnsCleanPrice()
    {
        // Microsoft's documented PRICE example:
        // PRICE(DATE(2008,2,15), DATE(2017,11,15), 5.75%, 6.5%, 100, 2, 0) = 94.63
        double price = Calc("PRICE(DATE(2008,2,15),DATE(2017,11,15),0.0575,0.065,100,2,0)");
        price.Should().BeApproximately(94.63, 0.01);
    }

    [Fact]
    public void Price_ParBond_CouponRateEqualsYield_StillReturnsPar()
    {
        // Sibling already-working case: when coupon rate == yield and settlement falls
        // exactly on a coupon date (no accrued stub to subtract), the clean price must
        // remain exactly par (100) — this must keep working after the accrued-interest fix.
        double price = Calc("PRICE(DATE(2020,1,1),DATE(2025,1,1),0.1,0.1,100,1,0)");
        price.Should().BeApproximately(100.0, 0.05);
    }
}
