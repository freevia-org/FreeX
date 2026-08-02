using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for round-114 finding:
///
/// R114-formula-stat-distribution-discrete-int-cast: R53 fixed the unguarded (int)-cast
/// saturation bug (a plain (int) cast on a double outside Int32's range SATURATES to
/// Int32.MaxValue in .NET rather than throwing) for BINOM.DIST/BINOM.INV's trials parameter and
/// NEGBINOM.DIST's successes parameter via the TryTruncateToInt32 helper (see
/// R53_FormulaFixesTests.cs). Two sibling call sites of the identical parameter class were left
/// on the old unguarded cast: BinomDistRangeScalar's trialsValue (BINOM.DIST.RANGE's "trials"
/// argument) and NegbinomDistScalar's failuresValue (NEGBINOM.DIST's "number_f" argument).
/// Neither argument has a documented Excel upper bound, so a legitimate huge finite double (e.g.
/// 3e9) silently saturated to Int32.MaxValue and the function computed a confidently wrong
/// answer for a completely different (smaller) parameter instead of erroring. Fixed by routing
/// both call sites through the same TryTruncateToInt32 helper already used by their siblings.
/// </summary>
public class R114_FormulaFixesTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void BinomDistRange_TrialsAboveInt32Max_ReturnsNum_InsteadOfSilentlyWrongOne()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        // Before the fix, 3,000,000,000 trials saturated to Int32.MaxValue (2,147,483,647), and
        // k1=k2=1,000,000,000 passed all the range checks against the saturated n — the function
        // then computed BinomCdf with ~2.1bn trials instead of the requested 3bn, a silently
        // wrong non-error result. It must now be #NUM! instead.
        _eval.Evaluate("=BINOM.DIST.RANGE(3000000000,0.5,1000000000,1000000000)", sheet, workbook)
            .Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void BinomDistRange_NormalSmallTrials_StillComputesCorrectly_NoRegression()
    {
        // Sibling no-regression: an ordinary, well within-range trials count must be unaffected.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = _eval.Evaluate("=BINOM.DIST.RANGE(4,0.5,2,2)", sheet, workbook)
            .Should().BeOfType<NumberValue>().Subject;
        // P(X=2 | n=4, p=0.5) = C(4,2)*0.5^4 = 6/16 = 0.375
        result.Value.Should().BeApproximately(0.375, 1e-9);
    }

    [Fact]
    public void NegbinomDist_FailuresAboveInt32Max_ReturnsNum_InsteadOfSilentlyWrongOne()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        // Before the fix, 3,000,000,000 failures saturated to Int32.MaxValue (2,147,483,647) — a
        // silently wrong, non-error result computed against a much smaller failures count. It
        // must now be #NUM! instead.
        _eval.Evaluate("=NEGBINOM.DIST(3000000000,5,0.5,TRUE)", sheet, workbook)
            .Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void NegbinomDist_NormalSmallFailures_StillComputesCorrectly_NoRegression()
    {
        // Sibling no-regression: an ordinary, well within-range failures count must be unaffected.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = _eval.Evaluate("=NEGBINOM.DIST(2,3,0.5,FALSE)", sheet, workbook)
            .Should().BeOfType<NumberValue>().Subject;
        // PMF: C(f+r-1,f) * p^r * (1-p)^f = C(4,2) * 0.5^3 * 0.5^2 = 6 * 0.03125 = 0.1875
        result.Value.Should().BeApproximately(0.1875, 1e-9);
    }
}
