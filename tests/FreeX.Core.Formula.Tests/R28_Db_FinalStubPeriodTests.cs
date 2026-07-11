using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression test for R28-financial-functions-deep-2-1: DbScalar's final (life+1) stub
/// period used (12-month+1)/12 instead of Excel's documented (12-month)/12, overstating
/// depreciation for the trailing partial-year period by roughly 20%.
/// </summary>
public class R28_Db_FinalStubPeriodTests
{
    private readonly FormulaEvaluator _eval = new();

    private ScalarValue Eval(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        return _eval.Evaluate("=" + formula, sheet, wb);
    }

    [Fact]
    public void Db_FinalStubPeriod_MatchesExcelDocumentedExample()
    {
        // DB(1000000, 100000, 6, 7, 7) is Microsoft's own published DB() example:
        // life=6 years, period=7 is the trailing stub period after the depreciable life,
        // month=7 (7 months used in the first year). Excel returns 15,845.10.
        var result = Eval("DB(1000000,100000,6,7,7)");

        var number = result.Should().BeOfType<NumberValue>().Subject;
        number.Value.Should().BeApproximately(15845.10, 0.01);
    }

    [Fact]
    public void Db_MidLifePeriod_IsUnchangedByStubPeriodFix()
    {
        // Sanity check: periods within the normal depreciable life (p <= life) use a
        // different code path and must remain exactly as before the fix.
        // Period 6 (the last normal, non-stub period) of the same Microsoft example ~55,841.76.
        var period6 = Eval("DB(1000000,100000,6,6,7)");
        period6.Should().BeOfType<NumberValue>().Subject.Value.Should().BeApproximately(55841.76, 0.01);

        // Period 1 (first-year, half-year-convention start) ~186,083.33 — already covered
        // elsewhere but re-asserted here as the sibling "already working" case for this fix.
        var period1 = Eval("DB(1000000,100000,6,1,7)");
        period1.Should().BeOfType<NumberValue>().Subject.Value.Should().BeApproximately(186083.33, 0.01);
    }

    [Fact]
    public void Db_FinalStubPeriod_WithDefaultTwelveMonth_IsZero()
    {
        // When month defaults to 12 (a full first year), the trailing stub period after
        // the depreciable life has zero remaining months to prorate: (12-12)/12 = 0.
        // The pre-fix formula ((12-month+1)/12 = 1/12) would have produced a spurious
        // non-zero residual here.
        var result = Eval("DB(1000000,100000,6,7)");

        var number = result.Should().BeOfType<NumberValue>().Subject;
        number.Value.Should().BeApproximately(0.0, 1e-9);
    }
}
