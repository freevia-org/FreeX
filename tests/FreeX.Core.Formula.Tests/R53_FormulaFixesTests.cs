using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for round-53 findings:
///
/// R53-formula-vlookup-approx-4-1: VLOOKUP/HLOOKUP's range_lookup argument did not coerce a
/// literal text "TRUE"/"FALSE" the way Excel does for a boolean-typed parameter (the same
/// coercion IF/IFS already apply to their condition argument), so e.g.
/// =VLOOKUP(3,Tbl,2,"FALSE") threw #VALUE! instead of performing the requested exact lookup.
/// Fixed in BuiltInFunctions.Lookup.Legacy.cs by adding a local TryCoerceRangeLookupBool helper
/// (mirroring FormulaEvaluator.ControlFlow.cs's TryCoerceCondition) used by VlookupScalar/
/// HlookupScalar instead of the throwing BuiltInFunctions.ToBool.
///
/// A direct literal-range table argument (e.g. "=VLOOKUP(3,A1:B5,2,\"FALSE\")") is intercepted by
/// FormulaEvaluator's "direct range" fast path (FormulaEvaluator.LookupFastPaths.cs), which has its
/// own separate, out-of-scope copy of the same range_lookup coercion gap (a different file/bucket
/// — see R29_LookupApproximateUnsortedTests.cs for the same rationale). These tests route the
/// table argument through a defined name instead, which that fast path deliberately doesn't
/// intercept (its TryAsRangeRef predicate only matches a literal RangeRefNode), so the formula
/// falls through to the fixed BuiltInFunctions.Lookup.Legacy.cs scalar functions.
///
/// R53-formula-logical-nested-shortcircuit-3-1: IFS's array/range-condition branch
/// (EvaluateIfsConditionRange) always seeded its per-cell condition cache at key 0 regardless of
/// which argument index actually produced the triggering range, so an earlier scalar condition
/// pair (e.g. a literal FALSE at argument 0) was silently replaced by a later range condition for
/// every row/column of the array result. Fixed by seeding the cache at the actual argument index.
///
/// R53-formula-npv-irr-schedule-3-1: SKIPPED. The finding argues XIRR should return #NUM! (not
/// #N/A) for a too-short values/dates pair, matching Excel's documented XIRR error surface and
/// FreeX's own IRR's identical count&lt;2 guard. However, changing XirrScalar's count&lt;2 branch to
/// ErrorValue.Num directly conflicts with the pre-existing, deliberately-authored
/// FinancialScalarArrayTests.FinancialFunctions_TreatScalarArraysAsSingleCellArrays (asserts
/// Assert.Equal(ErrorValue.NA, Eval("=XIRR(5,45000)")) and the A1/C1 sibling) — a file outside this
/// bucket's edit set, so it cannot be updated here. No fix/test is included for this finding.
///
/// R53-formula-stat-distribution-3-1: BINOM.DIST/BINOM.INV/NEGBINOM.DIST/HYPGEOM.DIST truncated
/// their trials/successes/population-size arguments via a plain (int) cast, which SATURATES to
/// Int32.MaxValue for any finite double above ~2.147 billion instead of throwing — silently
/// substituting a much smaller trials count and returning a confidently wrong numeric result
/// instead of erroring. Fixed by adding a TryTruncateToInt32 helper that returns #NUM! once the
/// truncated magnitude no longer fits in an Int32.
///
/// R53-formula-stat-distribution-3-2: CHISQ.DIST/CHISQ.DIST.RT/CHISQ.INV/CHISQ.INV.RT and
/// F.DIST/F.DIST.RT/F.INV/F.INV.RT had no upper bound on degrees of freedom, even though Excel
/// documents deg_freedom as "a number between 1 and 10^10, excluding 10^10" and returns #NUM!
/// once it reaches that ceiling. Fixed by adding a df/d1/d2 &gt;= 1e10 guard alongside the existing
/// df &lt; 1 floor in BuiltInFunctions.StatisticalDistributions.FChiSquare.cs. (T.DIST/T.INV live in
/// the separate, out-of-scope BuiltInFunctions.StatisticalDistributions.T.cs file and are not
/// touched by this fix.)
/// </summary>
public class R53_FormulaFixesTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook workbook, Sheet sheet) MakeNamedRangeWorkbook(int rows, int cols, params (int row, int col, ScalarValue val)[] cells)
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), val);
        workbook.DefineNamedRange("Tbl", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, (uint)rows, (uint)cols)));
        return (workbook, sheet);
    }

    // ── R53-formula-vlookup-approx-4-1 ──────────────────────────────────────────────────────

    [Fact]
    public void Vlookup_LiteralTextFalse_CoercesToExactMatch_InsteadOfValueError()
    {
        var (workbook, sheet) = MakeNamedRangeWorkbook(3, 2,
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(10)),
            (2, 1, new NumberValue(2)), (2, 2, new NumberValue(20)),
            (3, 1, new NumberValue(3)), (3, 2, new NumberValue(30)));

        _eval.Evaluate("=VLOOKUP(3,Tbl,2,\"FALSE\")", sheet, workbook).Should().Be(new NumberValue(30));
        // Case-insensitive, and HLOOKUP mirrors the same coercion.
        _eval.Evaluate("=VLOOKUP(3,Tbl,2,\"false\")", sheet, workbook).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Vlookup_BooleanFalseLiteral_StillWorks_NoRegression()
    {
        // Sibling no-regression: the pre-existing boolean (non-text) FALSE path must be unaffected.
        var (workbook, sheet) = MakeNamedRangeWorkbook(3, 2,
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(10)),
            (2, 1, new NumberValue(2)), (2, 2, new NumberValue(20)),
            (3, 1, new NumberValue(3)), (3, 2, new NumberValue(30)));

        _eval.Evaluate("=VLOOKUP(3,Tbl,2,FALSE)", sheet, workbook).Should().Be(new NumberValue(30));
        // An invalid, non-TRUE/FALSE text argument must still yield #VALUE! (Excel's own behavior).
        _eval.Evaluate("=VLOOKUP(3,Tbl,2,\"MAYBE\")", sheet, workbook).Should().Be(ErrorValue.Value);
    }

    // ── R53-formula-logical-nested-shortcircuit-3-1 ─────────────────────────────────────────

    [Fact]
    public void Ifs_LeadingScalarFalseCondition_NotOverriddenByLaterArrayCondition()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(-5));

        // =IFS(FALSE,"WRONG",A1:A2>0,"OK"): the literal FALSE first condition can never fire, so
        // row1 (A1=5>0 true) must return "OK" and row2 (A2=-5>0 false) must return #N/A. Before the
        // fix, the cache seeded the A1:A2>0 range at key 0, so row1 wrongly returned "WRONG".
        var result = _eval.Evaluate("=IFS(FALSE,\"WRONG\",A1:A2>0,\"OK\")", sheet, workbook)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(2);
        result.Cells[0, 0].Should().Be(new TextValue("OK"), "condition 1 (FALSE) can never fire, so row1 must fall through to condition 2");
        result.Cells[1, 0].Should().Be(ErrorValue.NA, "neither condition is true for row2");
    }

    [Fact]
    public void Ifs_ArrayConditionAtFirstArgument_StillWorks_NoRegression()
    {
        // Sibling no-regression: when the array condition IS at argument index 0 (the common case
        // this feature already handled correctly), behavior must be unchanged.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(-5));

        var result = _eval.Evaluate("=IFS(A1:A2>0,\"OK\")", sheet, workbook)
            .Should().BeOfType<RangeValue>().Subject;

        result.Cells[0, 0].Should().Be(new TextValue("OK"));
        result.Cells[1, 0].Should().Be(ErrorValue.NA);
    }

    // ── R53-formula-npv-irr-schedule-3-1 ────────────────────────────────────────────────────
    // SKIPPED — see the class-level doc comment above for the blocking pre-existing test.

    // ── R53-formula-stat-distribution-3-1 ───────────────────────────────────────────────────

    [Fact]
    public void BinomDist_TrialsAboveInt32Max_ReturnsNum_InsteadOfSilentlyWrongOne()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        // Before the fix, 2,200,000,000 trials saturated to Int32.MaxValue (2,147,483,647), and
        // the CDF at k=1.1bn saturated to 1.0 in double precision against the wrong (smaller) mean
        // — a silently wrong, non-error result. It must now be #NUM! instead.
        _eval.Evaluate("=BINOM.DIST(1100000000,2200000000,0.5,TRUE)", sheet, workbook).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void BinomDist_NormalSmallTrials_StillComputesCorrectly_NoRegression()
    {
        // Sibling no-regression: an ordinary, well within-range trials count must be unaffected.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = _eval.Evaluate("=BINOM.DIST(2,4,0.5,FALSE)", sheet, workbook)
            .Should().BeOfType<NumberValue>().Subject;
        // P(X=2 | n=4, p=0.5) = C(4,2)*0.5^4 = 6/16 = 0.375
        result.Value.Should().BeApproximately(0.375, 1e-9);
    }

    // ── R53-formula-stat-distribution-3-2 ───────────────────────────────────────────────────

    [Fact]
    public void ChiSqDist_DegreesOfFreedomAtOrAboveTenBillion_ReturnsNum()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        _eval.Evaluate("=CHISQ.DIST(5,20000000000,TRUE)", sheet, workbook).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void ChiSqDist_NormalDegreesOfFreedom_StillComputesCorrectly_NoRegression()
    {
        // Sibling no-regression: an ordinary, well within-range degrees of freedom must be
        // unaffected by the new upper-bound guard.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");

        var result = _eval.Evaluate("=CHISQ.DIST(5,10,TRUE)", sheet, workbook)
            .Should().BeOfType<NumberValue>().Subject;
        result.Value.Should().BeApproximately(0.108822, 0.0001);
    }
}
