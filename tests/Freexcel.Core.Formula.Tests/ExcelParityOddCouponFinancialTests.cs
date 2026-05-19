using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

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
