using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression coverage for round-12 bucket Q7 findings.
/// </summary>
public sealed class FreeXR12Q7Tests
{
    private readonly FormulaEvaluator _eval = new();

    /// <summary>
    /// R12-formula-financial-stat-1: PERCENTILE.EXC/QUARTILE.EXC must return #NUM! when k falls
    /// outside the valid exclusive range [1/(n+1), n/(n+1)], instead of clamping to the max value.
    /// PERCENTILE.EXC({1,2,3,4}, 0.9): n=4, rank = 0.9*5-1 = 3.5, which exceeds n-1=3, so k=0.9
    /// exceeds n/(n+1)=0.8 and Excel returns #NUM!.
    /// </summary>
    [Fact]
    public void PercentileExc_KAboveUpperBound_ReturnsNumError()
    {
        var sheet = Values(1, 2, 3, 4);
        _eval.Evaluate("=PERCENTILE.EXC(A1:A4,0.9)", sheet).Should().Be(ErrorValue.Num);
    }

    /// <summary>
    /// R12-formula-financial-stat-1: QUARTILE.EXC({1,2}, 3) maps to k=0.75, which exceeds
    /// n/(n+1) = 2/3 for n=2, so Excel returns #NUM! instead of clamping to the max value (2).
    /// </summary>
    [Fact]
    public void QuartileExc_KAboveUpperBound_ReturnsNumError()
    {
        var sheet = Values(1, 2);
        _eval.Evaluate("=QUARTILE.EXC(A1:A2,3)", sheet).Should().Be(ErrorValue.Num);
    }

    private static Sheet Values(params double[] values)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        for (var i = 0; i < values.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i + 1, 1), new NumberValue(values[i]));
        return sheet;
    }
}
