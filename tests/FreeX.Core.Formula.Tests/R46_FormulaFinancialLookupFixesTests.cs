using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for round-46 findings:
///
/// R46-formula-financial-depreciation-2-1: VDB reset book value to full cost at start_period
/// instead of simulating depreciation sequentially from period 0, overstating depreciation for
/// any start_period > 0. Fixed by simulating from period 0 and only accumulating the portion of
/// each period's depreciation that overlaps [start_period, end_period).
///
/// R46-formula-financial-depreciation-2-2: DB forced a hard-coded 0 whenever salvage >= cost
/// instead of evaluating Excel's actual declining-balance rate formula (which yields a small
/// negative rate, not zero, when salvage > cost). Fixed by removing the special-case guard.
///
/// R46-formula-xmatch-xlookup-3-1: XMATCH/XLOOKUP's approximate match_mode (-1/1) linear search
/// ignored type-class, so a text/bool candidate could wrongly satisfy a numeric "next
/// larger/smaller" match. Fixed by filtering candidates by ApproxLookupTypeClass, mirroring
/// MATCH/VLOOKUP/HLOOKUP/LOOKUP's existing behavior.
/// </summary>
public class R46_FormulaFinancialLookupFixesTests
{
    private readonly FormulaEvaluator _eval = new();

    private double Calc(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        var result = _eval.Evaluate("=" + formula, sheet, wb);
        result.Should().BeOfType<NumberValue>($"formula {formula} should return a number");
        return ((NumberValue)result).Value;
    }

    // ── R46-formula-financial-depreciation-2-1 (VDB) ────────────────────────────────────────

    [Fact]
    public void Vdb_StartPeriodGreaterThanZero_CarriesForwardDepreciationFromPeriodZero()
    {
        // Hand-verified against real Excel: VDB(2400,300,10,1,3) = 691.2.
        // period0->1 DDB dep = 2400*2/10 = 480 (book 2400 -> 1920)
        // period1->2 dep = 1920*2/10 = 384 (book 1920 -> 1536)
        // period2->3 dep = 1536*2/10 = 307.2
        // Sum of the requested window [1,3) = 384 + 307.2 = 691.2.
        // Before the fix, VDB reset bookValue=cost=2400 at start_period=1, overstating this to
        // 864 (384 + 480 using the wrong, undepreciated book value for the second period).
        double result = Calc("VDB(2400,300,10,1,3)");
        result.Should().BeApproximately(691.2, 0.001);
    }

    [Fact]
    public void Vdb_StartPeriodZero_UnchangedByFix()
    {
        // Sibling no-regression case: start_period=0 must still equal the sum of DDB(period=1)
        // and DDB(period=2), exactly as before the fix (bookValue starts at cost either way when
        // start_period is 0, since there is nothing to carry forward).
        double vdb = Calc("VDB(2400,300,10,0,2)");
        double expected = Calc("DDB(2400,300,10,1)") + Calc("DDB(2400,300,10,2)");
        vdb.Should().BeApproximately(expected, 0.001);
    }

    // ── R46-formula-financial-depreciation-2-2 (DB) ─────────────────────────────────────────

    [Fact]
    public void Db_SalvageGreaterThanCost_MatchesExcelNegativeRateNotZero()
    {
        // Hand-verified against Excel's actual DB algorithm (no special-case for salvage>cost):
        // rate = ROUND(1 - (1200/1000)^(1/5), 3) = ROUND(1 - 1.2^0.2, 3) ~= -0.037
        // Dep(period 1) = cost * rate * month/12 = 1000 * -0.037 * 12/12 = -37.
        double result = Calc("DB(1000,1200,5,1)");
        result.Should().BeApproximately(-37.0, 0.001);
    }

    [Fact]
    public void Db_SalvageEqualsCost_StillReturnsZero()
    {
        // Sibling no-regression case: when salvage == cost the rate formula itself naturally
        // evaluates to 0 (1 - (cost/cost)^(1/life) = 1 - 1 = 0), so removing the special-case
        // guard must not change this legitimately-zero result.
        double result = Calc("DB(1000,1000,5,1)");
        result.Should().BeApproximately(0.0, 1e-9);
    }

    // ── R46-formula-xmatch-xlookup-3-1 (XMATCH/XLOOKUP approximate type-class filter) ──────

    [Fact]
    public void Xmatch_ApproximateNextLarger_SkipsTextCandidatesNotOfLookupTypeClass()
    {
        // A1:A3 = {5, "Banana", "Apple"} (mixed number/text, unsorted). No numeric candidate >=
        // 15 exists, so real Excel returns #N/A -- text values never participate in a numeric
        // approximate match. Before the fix, the cross-type ordering (number < text < bool)
        // meant "Banana"/"Apple" both satisfied "candidate >= lookup" once no genuine numeric
        // candidate qualified, wrongly returning index 3 ("Apple").
        // The lookup range is wrapped in IF(TRUE, ...) to route through the slow-path
        // TryFindApproximateMatchIndexLinear (BuiltInFunctions.Lookup.Modern.cs) rather than the
        // direct-range fast path in FormulaEvaluator.LookupFastPaths.cs (a separate, out-of-scope
        // duplicate of this bug not covered by this fix).
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Apple"));

        var result = _eval.Evaluate("=XMATCH(15,IF(TRUE,A1:A3),1,1)", sheet);

        result.Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xmatch_ApproximateNextLarger_StillMatchesQualifyingNumericCandidate()
    {
        // Sibling no-regression case: with a genuinely qualifying numeric candidate present, the
        // approximate next-larger-or-equal match must still work exactly as before the fix
        // (same slow-path wrapper as the sibling failing case above).
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Apple"));

        var result = _eval.Evaluate("=XMATCH(15,IF(TRUE,A1:A3),1,1)", sheet);

        result.Should().Be(new NumberValue(2));
    }
}
