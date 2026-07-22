using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression test for R68-formula-financial-depreciation-6-1 (AMORDEGRC/AMORLINC basis
/// scope): Excel's AMORDEGRC and AMORLINC only accept basis 0, 1, 3 or 4 -- unlike
/// YEARFRAC/ACCRINT/PRICE (which also accept basis 2, Actual/360), AMORDEGRC/AMORLINC
/// reject basis 2 with #NUM!. The narrower check lives in the two depreciation functions
/// themselves so the generic TryGetFinancialBasis helper stays unchanged for its other
/// callers.
/// </summary>
public class R68_AmordegrcAmorlincBasisTwoTests
{
    private readonly FormulaEvaluator _eval = new();

    private ScalarValue Eval(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        return _eval.Evaluate("=" + formula, sheet, wb);
    }

    [Theory]
    [InlineData("AMORDEGRC(2400,DATE(1998,8,19),DATE(1998,12,31),300,1,0.15,2)")]
    [InlineData("AMORLINC(2400,DATE(1998,8,19),DATE(1998,12,31),300,1,0.15,2)")]
    public void AmordegrcAndAmorlinc_Basis2_ReturnsNumError(string formula)
    {
        Eval(formula).Should().Be(ErrorValue.Num);
    }

    [Theory]
    [InlineData("AMORDEGRC(2400,DATE(1998,8,19),DATE(1998,12,31),300,1,0.15,0)")]
    [InlineData("AMORDEGRC(2400,DATE(1998,8,19),DATE(1998,12,31),300,1,0.15,1)")]
    [InlineData("AMORDEGRC(2400,DATE(1998,8,19),DATE(1998,12,31),300,1,0.15,3)")]
    [InlineData("AMORDEGRC(2400,DATE(1998,8,19),DATE(1998,12,31),300,1,0.15,4)")]
    public void Amordegrc_OtherAllowedBases_StillCompute(string formula)
    {
        // No-regression: bases 0, 1, 3 and 4 must still be accepted and compute a normal
        // (non-error) numeric result.
        Eval(formula).Should().BeOfType<NumberValue>();
    }

    [Fact]
    public void Yearfrac_Basis2_StillWorks()
    {
        // No-regression: the generic TryGetFinancialBasis helper (shared with YEARFRAC,
        // ACCRINT, PRICE, ...) must still accept basis 2 -- only AMORDEGRC/AMORLINC narrow it.
        var result = Eval("YEARFRAC(DATE(2024,1,1),DATE(2024,7,1),2)");
        result.Should().BeOfType<NumberValue>();
        var number = ((NumberValue)result).Value;
        number.Should().BeApproximately(182.0 / 360.0, 1e-9);
    }
}
