using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R25-aggregate-subtotal-deep-2: AGGREGATE (unlike its sibling SUBTOTAL, see
/// R19_SubtotalComputedArrayTests.cs / R19-formula-functions-edge-1) never received the guard that
/// distinguishes a genuine worksheet reference (real SheetName, or a start row/col other than the
/// (1,1) virtual-array default) from a computed/virtual array (e.g. FILTER's result, which has no
/// real position on the sheet and so defaults RangeValue.StartRow/StartCol to 1 with SheetName
/// null). Without that guard, AGGREGATE's hidden-row/nested-formula exclusions indexed a computed
/// array's element 0 as "row 1 of the current sheet", silently dropping it whenever sheet row 1
/// happened to be hidden or hold a nested SUBTOTAL/AGGREGATE formula. Fixed by adding the same
/// isReference guard (mirroring BuiltInFunctions.Subtotal.cs) to AGGREGATE's four slow-path
/// collectors in BuiltInFunctions.InformationA2.cs.
/// </summary>
public sealed class R25_AggregateComputedArrayTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    [Fact]
    public void Aggregate_ComputedArrayOperand_IncludesAllElements_DespiteHiddenSheetRow1()
    {
        // A1:A5 = {10,20,30,40,50}; B1:B5 all 1 so FILTER keeps every row unchanged. Sheet row 1
        // (which has nothing to do with the computed array's own positions) is hidden.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new NumberValue(1)),
            (2, 1, new NumberValue(20)), (2, 2, new NumberValue(1)),
            (3, 1, new NumberValue(30)), (3, 2, new NumberValue(1)),
            (4, 1, new NumberValue(40)), (4, 2, new NumberValue(1)),
            (5, 1, new NumberValue(50)), (5, 2, new NumberValue(1)));
        sheet.GroupHiddenRows.Add(1);

        // Pre-fix: AGGREGATE mistook the computed array's element 0 for "sheet row 1", which is
        // hidden, and dropped it -> 20+30+40+50 = 140. Real Excel / post-fix: a virtual array has
        // no sheet position, so hidden-row exclusion must not apply -> 150.
        // function_num 9 = SUM, options 5 = ignore hidden rows (and nested SUBTOTAL/AGGREGATE).
        var result = _eval.Evaluate("=AGGREGATE(9,5,FILTER(A1:A5,B1:B5>0))", sheet);

        result.Should().Be(new NumberValue(150));
    }

    [Fact]
    public void Aggregate_ComputedArrayOperand_NestedAggregateTextAtHiddenDefaultCoordDoesNotExclude()
    {
        // Cell A1 itself literally holds the text of an AGGREGATE(...) formula (simulating the
        // nested-AGGREGATE exclusion trigger), but the computed array passed to the outer AGGREGATE
        // is a virtual array (SEQUENCE) whose values are unrelated to A1 and which only
        // coincidentally shares A1's default (1,1) coordinate. The nested-formula exclusion must
        // not fire for it.
        var sheet = MakeSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(999));
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "=AGGREGATE(9,0,B1:B2)");

        // SEQUENCE(3) => {1;2;3}; AGGREGATE(9,0,...) should sum all three => 6, not skip element 1
        // because it happens to land on default coordinate (1,1) which holds a nested AGGREGATE
        // formula. options 0 = ignore nothing but nested SUBTOTAL/AGGREGATE cells.
        var result = _eval.Evaluate("=AGGREGATE(9,0,SEQUENCE(3))", sheet);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Aggregate_GenuineReferenceReachingSlowPath_StillExcludesHiddenRow()
    {
        // B2:B6 = {10,20,30,40,50}. Wrapping the reference in IF(TRUE, ...) forces AGGREGATE past
        // its direct-range fast path (which only accepts a bare RangeRefNode argument) into the
        // same coordinate-aware slow path that computed arrays go through — but this argument is a
        // genuine worksheet reference (real row/col, not the (1,1) virtual-array default), so its
        // hidden row must still be excluded, proving the fix distinguishes real references from
        // computed arrays rather than disabling the exclusion outright.
        var sheet = MakeSheet(
            (2, 2, new NumberValue(10)),
            (3, 2, new NumberValue(20)), // hidden outlier
            (4, 2, new NumberValue(30)),
            (5, 2, new NumberValue(40)),
            (6, 2, new NumberValue(50)));
        sheet.HiddenRows.Add(3);

        var withHiddenExcluded = _eval.Evaluate("=AGGREGATE(9,5,IF(TRUE,B2:B6))", sheet);
        var withHiddenIncluded = _eval.Evaluate("=AGGREGATE(9,4,IF(TRUE,B2:B6))", sheet);

        // Excluding the hidden 20 leaves {10,30,40,50} -> 130.
        withHiddenExcluded.Should().Be(new NumberValue(130));
        // Including everything (options 4 = ignore nothing) sums all five -> 150, proving the
        // hidden row was genuinely read for the "include" variant while excluded for the other.
        withHiddenIncluded.Should().Be(new NumberValue(150));
    }
}
