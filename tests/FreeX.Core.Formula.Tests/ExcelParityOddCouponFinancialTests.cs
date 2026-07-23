using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public sealed class ExcelParityOddCouponFinancialTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Oddfprice_MatchesMicrosoftExcelDocumentedExample()
    {
        Number("ODDFPRICE(DATE(2008,11,11),DATE(2021,3,1),DATE(2008,10,15),DATE(2009,3,1),7.85%,6.25%,100,2,1)")
            .Should().BeApproximately(113.60, 0.005);
    }

    [Fact]
    public void Oddfyield_MatchesMicrosoftExcelDocumentedExample()
    {
        Number("ODDFYIELD(DATE(2008,11,11),DATE(2021,3,1),DATE(2008,10,15),DATE(2009,3,1),5.75%,84.50,100,2,0)")
            .Should().BeApproximately(0.0772, 0.00005);
    }

    [Fact]
    public void Oddfprice_MonthEndMaturityCrossingShorterMonth_IncludesRedemption()
    {
        // R79-formula-financial-5-2: issue 2025-01-01, first_coupon 2025-08-31 (odd first
        // period), maturity 2027-08-31 (month-end), semiannual (months=6). Walking the coupon
        // schedule forward from the shrinking previous date (the pre-fix bug) drifts through
        // February (28 days) and never lands back on the day-31 maturity, so the redemption
        // principal is silently dropped from the price. Anchoring every candidate off the
        // ORIGINAL maturity (the fix) keeps the last schedule entry exactly equal to maturity.
        // Hand-computed expected price (rate=yld=5%, redemption=100, basis 0/30-360, settlement
        // 2025-03-01): ~99.9796748 -- versus ~11.6 if the redemption is dropped, so this is a
        // wide, unambiguous margin between the pre-fix and post-fix results.
        Number("ODDFPRICE(DATE(2025,3,1),DATE(2027,8,31),DATE(2025,1,1),DATE(2025,8,31),5%,5%,100,2,0)")
            .Should().BeApproximately(99.979674796748, 1e-6);
    }

    [Fact]
    public void Oddfyield_MonthEndMaturityCrossingShorterMonth_IncludesRedemption()
    {
        // Sibling of Oddfprice_MonthEndMaturityCrossingShorterMonth_IncludesRedemption: solving
        // ODDFYIELD against the price produced by that same (correct) 5% yield must recover 5%.
        // Before the fix, OddFirstPrice's dropped redemption meant no rate could reproduce this
        // price, so Newton-Raphson would converge to a materially different (wrong) yield.
        Number("ODDFYIELD(DATE(2025,3,1),DATE(2027,8,31),DATE(2025,1,1),DATE(2025,8,31),5%,99.979674796748,100,2,0)")
            .Should().BeApproximately(0.05, 1e-6);
    }

    [Fact]
    public void Oddfprice_NonMonthEndSchedule_NoRegression()
    {
        // No-regression sibling: reuses the Microsoft-documented ODDFPRICE example (settlement
        // 2008-11-11, maturity 2021-3-1, none of which are month-end dates that ever trigger
        // .NET's AddMonths day-of-month clamp), confirming the anchored-schedule rewrite left
        // the already-correct non-edge-case path unchanged.
        Number("ODDFPRICE(DATE(2008,11,11),DATE(2021,3,1),DATE(2008,10,15),DATE(2009,3,1),7.85%,6.25%,100,2,1)")
            .Should().BeApproximately(113.60, 0.005);
    }

    [Fact]
    public void Oddlprice_MatchesMicrosoftExcelDocumentedExample()
    {
        Number("ODDLPRICE(DATE(2008,2,7),DATE(2008,6,15),DATE(2007,10,15),3.75%,4.05%,100,2,0)")
            .Should().BeApproximately(99.88, 0.005);
    }

    [Fact]
    public void Oddlyield_MatchesMicrosoftExcelDocumentedExample()
    {
        Number("ODDLYIELD(DATE(2008,4,20),DATE(2008,6,15),DATE(2007,12,24),3.75%,99.875,100,2,0)")
            .Should().BeApproximately(0.04519, 0.00005);
    }

    [Theory]
    [InlineData("ODDFPRICE(DATE(2008,11,11),DATE(2021,3,1),DATE(2008,10,15),DATE(2009,3,1),-1%,6.25%,100,2,1)")]
    [InlineData("ODDFYIELD(DATE(2008,11,11),DATE(2021,3,1),DATE(2008,10,15),DATE(2009,3,1),5.75%,0,100,2,0)")]
    [InlineData("ODDLPRICE(DATE(2008,2,7),DATE(2008,6,15),DATE(2007,10,15),-1%,4.05%,100,2,0)")]
    [InlineData("ODDLYIELD(DATE(2008,4,20),DATE(2008,6,15),DATE(2007,12,24),3.75%,0,100,2,0)")]
    public void OddCouponFunctions_ReturnNumForExcelNumericDomainErrors(string formula)
        => Error(formula).Should().Be("#NUM!");

    [Theory]
    [InlineData("ODDFPRICE(DATE(2008,11,11),DATE(2021,3,1),DATE(2008,10,15),DATE(2009,3,1),7.85%,6.25%,100,3,1)")]
    [InlineData("ODDFYIELD(DATE(2008,11,11),DATE(2021,3,1),DATE(2008,10,15),DATE(2009,3,1),5.75%,84.50,100,3,0)")]
    [InlineData("ODDLPRICE(DATE(2008,2,7),DATE(2008,6,15),DATE(2007,10,15),3.75%,4.05%,100,3,0)")]
    [InlineData("ODDLYIELD(DATE(2008,4,20),DATE(2008,6,15),DATE(2007,12,24),3.75%,99.875,100,3,0)")]
    public void OddCouponFunctions_ReturnNumForInvalidFrequency(string formula)
        => Error(formula).Should().Be("#NUM!");

    [Theory]
    [InlineData("ODDFPRICE(DATE(2008,11,11),DATE(2021,3,1),DATE(2008,10,15),DATE(2008,11,1),7.85%,6.25%,100,2,1)")]
    [InlineData("ODDFYIELD(DATE(2008,11,11),DATE(2021,3,1),DATE(2008,10,15),DATE(2008,11,1),5.75%,84.50,100,2,0)")]
    [InlineData("ODDLPRICE(DATE(2008,2,7),DATE(2008,1,15),DATE(2007,10,15),3.75%,4.05%,100,2,0)")]
    [InlineData("ODDLYIELD(DATE(2008,4,20),DATE(2008,3,15),DATE(2007,12,24),3.75%,99.875,100,2,0)")]
    public void OddCouponFunctions_ReturnNumWhenExcelDateOrderingRulesAreViolated(string formula)
        => Error(formula).Should().Be("#NUM!");

    private double Number(string formula)
    {
        var result = _eval.Evaluate("=" + formula, Sheet());
        result.Should().BeOfType<NumberValue>();
        return ((NumberValue)result).Value;
    }

    private string Error(string formula)
    {
        var result = _eval.Evaluate("=" + formula, Sheet());
        result.Should().BeOfType<ErrorValue>();
        return ((ErrorValue)result).Code;
    }

    private static Sheet Sheet() => new(SheetId.New(), "S");
}
