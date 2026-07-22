using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression test for R68-formula-financial-depreciation-6-2: DbScalar computed the
/// declining-balance rate as ROUND(1-(salvage/cost)^(1/ilife),3) using the truncated
/// integer life in the exponent. Excel uses the raw (untruncated) life for the rate
/// exponent -- only the period-count/loop bookkeeping should use the truncated value.
/// </summary>
public class R68_DbFractionalLifeRateTests
{
    private readonly FormulaEvaluator _eval = new();

    private ScalarValue Eval(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        return _eval.Evaluate("=" + formula, sheet, wb);
    }

    [Fact]
    public void Db_FractionalLife_Period1_UsesRawLifeInRateExponent()
    {
        // rate = ROUND(1-(1000/10000)^(1/5.9),3) = ROUND(1-0.1^(1/5.9),3) = 0.323
        // period-1 depreciation = cost*rate*month/12 = 10000*0.323*12/12 = 3230.
        // The pre-fix code truncated life to 5 for the exponent, giving rate=0.369 -> 3690.
        var result = Eval("DB(10000,1000,5.9,1,12)");
        result.Should().BeOfType<NumberValue>().Subject.Value.Should().BeApproximately(3230.0, 1e-6);
    }

    [Fact]
    public void Db_IntegerLife_IsUnchangedByFractionalLifeFix()
    {
        // No-regression: when life is already a whole number, truncation is a no-op, so the
        // documented Microsoft example must still match exactly.
        var result = Eval("DB(1000000,100000,6,1,7)");
        result.Should().BeOfType<NumberValue>().Subject.Value.Should().BeApproximately(186083.33, 0.01);
    }
}
