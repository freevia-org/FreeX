using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R32-formula-financial-remaining-1 / -2: Excel's documented XNPV/XIRR contract states that
/// if any date in the dates argument precedes the first (anchor) date, the function must
/// return #NUM! rather than silently computing a (negative-year-fraction) result.
///
/// These use inline array-constant arguments (not cell-range references) so the scalar
/// BuiltInFunctions.XnpvScalar/XirrScalar path is exercised directly rather than the
/// direct-range fast path in FormulaEvaluator.FinancialFastPaths.cs.
/// </summary>
public class R32_XnpvXirrAnchorDateTests
{
    private readonly FormulaEvaluator _eval = new();

    private ScalarValue Eval(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        return _eval.Evaluate("=" + formula, sheet, wb);
    }

    // Date serials: Jan 1 2024 = 45292, Jun 1 2024 = 45444, Dec 31 2024 = 45657.

    [Fact]
    public void Xnpv_DatePrecedingAnchor_ReturnsNumError()
    {
        // Anchor (first date) is Jun 1 2024 (45444); the third date, Jan 1 2024 (45292),
        // precedes it by 152 days.
        var result = Eval("XNPV(0.1,{-1000,600,600},{45444,45657,45292})");
        result.Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Xnpv_AllDatesAfterAnchor_StillComputesNormally()
    {
        // Sibling case: dates are in order and all >= the anchor date - must keep working.
        // XNPV(0.1, {-100, 110}, {43831, 44197}) ~= 0 (Jan 1 2020 -> Jan 1 2021, ~10% return).
        var result = Eval("XNPV(0.1,{-100,110},{43831,44197})");
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.0, 0.5);
    }

    [Fact]
    public void Xirr_DatePrecedingAnchor_ReturnsNumError()
    {
        // Anchor (first date) is Jun 1 2024 (45444); the third date, Jan 1 2024 (45292),
        // precedes it by 152 days.
        var result = Eval("XIRR({-1000,600,600},{45444,45657,45292})");
        result.Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Xirr_AllDatesAfterAnchor_StillComputesNormally()
    {
        // Sibling case: ordinary ordered-dates input must keep converging to ~10%.
        var result = Eval("XIRR({-100,110},{43831,44197})");
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(0.1, 0.005);
    }
}
