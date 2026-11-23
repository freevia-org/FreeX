using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression test for R20-financial-functions-3: DDB returned exactly 0 for any
/// fractional period strictly between 0 and 1 because DdbScalar truncated the period
/// to an int before looping, so the loop body never ran for period &lt; 1.
/// </summary>
public class R20_financial_ddb_Tests
{
    private readonly FormulaEvaluator _eval = new();

    private ScalarValue Eval(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        return _eval.Evaluate("=" + formula, sheet, wb);
    }

    [Fact]
    public void Ddb_WithFractionalPeriodBetweenZeroAndOne_ReturnsProRatedFirstPeriodDepreciation()
    {
        // DDB(cost=2400, salvage=300, life=10, period=0.1, factor=2)
        // Measured against Excel 16.0 (en-US): a period below 1 is the whole first-period amount
        // (cost*factor/life = 480), not a pro-rated slice and not 0.
        var result = Eval("DDB(2400,300,10,0.1,2)");

        var number = result.Should().BeOfType<NumberValue>().Subject;
        number.Value.Should().BeApproximately(480.0, 1e-9);
        number.Value.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Ddb_WithIntegerPeriod_IsUnchangedByFractionalSupport()
    {
        // Sanity check: integer-period results must remain exactly as before the fix.
        // Period 1: cost*factor/life = 2400*2/10 = 480.
        var period1 = Eval("DDB(2400,300,10,1,2)");
        period1.Should().BeOfType<NumberValue>().Subject.Value.Should().BeApproximately(480.0, 1e-9);

        // Period 2: bookValue after period 1 is 2400-480=1920; dep = 1920*2/10 = 384.
        var period2 = Eval("DDB(2400,300,10,2,2)");
        period2.Should().BeOfType<NumberValue>().Subject.Value.Should().BeApproximately(384.0, 1e-9);
    }

    [Fact]
    public void Ddb_WithFractionalPeriodGreaterThanOne_ProratesTheTrailingFraction()
    {
        // Measured against Excel 16.0 (en-US): a fractional period follows the continuous curve
        // D(p) - D(p-1) with D(x) = cost * (1 - (1 - factor/life)^x), i.e.
        // 2400 * (0.8^1.5 - 0.8^2.5) = 343.46.
        var result = Eval("DDB(2400,300,10,2.5,2)");

        var number = result.Should().BeOfType<NumberValue>().Subject;
        number.Value.Should().BeApproximately(343.460041344, 1e-8);
    }
}
