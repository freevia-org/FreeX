using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for round-25 findings in BuiltInFunctions.Lookup.Modern.cs:
///
/// R25-lookup-functions-deep-1: XLOOKUP/XMATCH search_mode 2/-2 (binary search) fell back to a
/// full linear scan in the slow (non-direct-range) evaluation path, so a formula whose range
/// argument is wrapped (e.g. IF(TRUE,B1:B5)) diverged from the fast direct-range path's real
/// binary search over identical data. Fixed by giving the slow path the same binary-search
/// algorithm as FormulaEvaluator.LookupFastPaths.cs's TryFindDirectBinaryLookupIndex.
///
/// R25-lookup-functions-deep-2: XLOOKUP with an array lookup_value, a multi-column return_array,
/// and a scalar if_not_found used to collapse the ENTIRE spilled result to #VALUE! the moment any
/// single lookup in the array missed, instead of broadcasting if_not_found across the unmatched
/// row/column. Fixed by broadcasting the scalar result across the known output width/height.
///
/// R25-lookup-functions-deep-3: XLOOKUP treated an explicitly-supplied but blank-valued
/// if_not_found argument the same as an omitted one, always substituting #N/A instead of
/// returning the supplied blank value verbatim. Fixed by keying the default off argument arity
/// (args.Count > 3) alone.
/// </summary>
public class R25_LookupModernTests
{
    private readonly FormulaEvaluator _eval = new();

    // ── R25-lookup-functions-deep-1 ─────────────────────────────────────────────────────────
    // XMATCH/XLOOKUP binary search must match the direct-range fast path's real result for the
    // same data reached through a wrapped (non-bare) range argument.

    [Fact]
    public void Xmatch_UnsortedDataExactBinarySearch_WrappedRangeMatchesDirectRangeNotFound()
    {
        // B1:B5 = {10,4,1,2,3} (unsorted). The direct-range fast path's real binary search
        // computes an empty "equal" boundary for lookup value 4 on this unsorted data and
        // returns #N/A. Before the fix, the slow path (reached via the IF() wrapper) ignored
        // search_mode entirely for exact match and fell back to a full linear scan, finding 4 at
        // position 2 -- diverging from the fast path for identical data.
        var sheet = MakeSheet(
            (1, 2, new NumberValue(10)), (2, 2, new NumberValue(4)), (3, 2, new NumberValue(1)),
            (4, 2, new NumberValue(2)), (5, 2, new NumberValue(3)));

        var direct = _eval.Evaluate("=XMATCH(4,B1:B5,0,2)", sheet);
        var wrapped = _eval.Evaluate("=XMATCH(4,IF(TRUE,B1:B5),0,2)", sheet);

        direct.Should().Be(ErrorValue.NA);
        wrapped.Should().Be(direct);
    }

    [Fact]
    public void Xmatch_SortedDataWithDuplicatesExactBinarySearch_WrappedRangeMatchesDirectRange()
    {
        // Sibling already-working case (ascending + descending), mirroring
        // FunctionLibraryTests.Lookup.cs's
        // Xmatch_And_Xlookup_DirectBinarySearchExactModes_HandleDuplicatesAndMissingValues --
        // proves the fast path's real result is preserved (not broken) for wrapped ranges too.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(2)),
            (4, 1, new NumberValue(2)), (5, 1, new NumberValue(4)),
            (1, 2, new NumberValue(4)), (2, 2, new NumberValue(2)), (3, 2, new NumberValue(2)),
            (4, 2, new NumberValue(2)), (5, 2, new NumberValue(1)));

        _eval.Evaluate("=XMATCH(2,A1:A5,0,2)", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=XMATCH(2,IF(TRUE,A1:A5),0,2)", sheet).Should().Be(new NumberValue(2));
        _eval.Evaluate("=XMATCH(2,B1:B5,0,-2)", sheet).Should().Be(new NumberValue(4));
        _eval.Evaluate("=XMATCH(2,IF(TRUE,B1:B5),0,-2)", sheet).Should().Be(new NumberValue(4));

        _eval.Evaluate("=XMATCH(3,A1:A5,0,2)", sheet).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=XMATCH(3,IF(TRUE,A1:A5),0,2)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xlookup_ApproximateBinarySearch_WrappedRangeMatchesDirectRange()
    {
        // Sibling approximate-match (-1/1) case, mirroring FunctionLibraryTests.Lookup.cs's
        // Xmatch_And_Xlookup_DirectBinarySearchApproximateModes_HandleAscendingAndDescendingBounds.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(3)), (3, 1, new NumberValue(3)),
            (4, 1, new NumberValue(5)), (5, 1, new NumberValue(7)),
            (1, 3, new TextValue("asc-one")), (2, 3, new TextValue("asc-three-first")),
            (3, 3, new TextValue("asc-three-last")), (4, 3, new TextValue("asc-five")),
            (5, 3, new TextValue("asc-seven")));

        _eval.Evaluate("=XLOOKUP(4,A1:A5,C1:C5,\"missing\",-1,2)", sheet).Should().Be(new TextValue("asc-three-first"));
        _eval.Evaluate("=XLOOKUP(4,IF(TRUE,A1:A5),IF(TRUE,C1:C5),\"missing\",-1,2)", sheet).Should().Be(new TextValue("asc-three-first"));
        _eval.Evaluate("=XLOOKUP(4,A1:A5,C1:C5,\"missing\",1,2)", sheet).Should().Be(new TextValue("asc-five"));
        _eval.Evaluate("=XLOOKUP(4,IF(TRUE,A1:A5),IF(TRUE,C1:C5),\"missing\",1,2)", sheet).Should().Be(new TextValue("asc-five"));
        _eval.Evaluate("=XMATCH(0,IF(TRUE,A1:A5),-1,2)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xmatch_WildcardMatchMode_WithBinarySearchModeAndWrappedRange_StillLinearScans()
    {
        // match_mode=2 (wildcard) cannot be binary-searched and must keep scanning linearly in
        // the direction implied by search_mode, both directly and through a wrapped range --
        // proves the restructured dispatch didn't disturb the (unchanged) wildcard fallback.
        var sheet = MakeSheet(
            (1, 1, new TextValue("Alpha")), (2, 1, new TextValue("Beta")), (3, 1, new TextValue("Alpine")));

        _eval.Evaluate("=XMATCH(\"Al*\",A1:A3,2,2)", sheet).Should().Be(new NumberValue(1));
        _eval.Evaluate("=XMATCH(\"Al*\",IF(TRUE,A1:A3),2,2)", sheet).Should().Be(new NumberValue(1));
    }

    // ── R25-lookup-functions-deep-2 ─────────────────────────────────────────────────────────
    // XLOOKUP array lookup_value + multi-column/row return_array + scalar if_not_found must
    // broadcast if_not_found across the unmatched row/column, not fail the whole spill.

    [Fact]
    public void Xlookup_VerticalArrayLookupWithMultiColumnReturn_BroadcastsScalarIfNotFoundAcrossRow()
    {
        // A1:A3 = {1;2;99} (vertical lookup values, last one unmatched).
        // D1:D3 = {1;2;3} (lookup range). E1:F3 = 2-column return range.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(99)),
            (1, 4, new NumberValue(1)), (2, 4, new NumberValue(2)), (3, 4, new NumberValue(3)),
            (1, 5, new TextValue("a")), (1, 6, new TextValue("x")),
            (2, 5, new TextValue("b")), (2, 6, new TextValue("y")),
            (3, 5, new TextValue("c")), (3, 6, new TextValue("z")));

        var result = _eval.Evaluate("=XLOOKUP(A1:A3,D1:D3,E1:F3,\"NF\")", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(2);
        result.At(1, 1).Should().Be(new TextValue("a"));
        result.At(1, 2).Should().Be(new TextValue("x"));
        result.At(2, 1).Should().Be(new TextValue("b"));
        result.At(2, 2).Should().Be(new TextValue("y"));
        result.At(3, 1).Should().Be(new TextValue("NF"));
        result.At(3, 2).Should().Be(new TextValue("NF"));
    }

    [Fact]
    public void Xlookup_HorizontalArrayLookupWithMultiRowReturn_BroadcastsScalarIfNotFoundAcrossColumn()
    {
        // Opposite orientation of the same fix: a 1-row lookupValues array with a multi-row
        // return array, mirroring Xlookup_RowLookupValuesAndMultiRowReturnArray_SpillsColumns
        // but with one miss.
        var sheet = MakeSheet(
            (5, 1, new NumberValue(1)), (5, 2, new NumberValue(2)), (5, 3, new NumberValue(99)),
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new TextValue("a")), (3, 1, new TextValue("x")),
            (2, 2, new TextValue("b")), (3, 2, new TextValue("y")),
            (2, 3, new TextValue("c")), (3, 3, new TextValue("z")));

        var result = _eval.Evaluate("=XLOOKUP(A5:C5,A1:C1,A2:C3,\"NF\")", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(3);
        result.At(1, 1).Should().Be(new TextValue("a"));
        result.At(2, 1).Should().Be(new TextValue("x"));
        result.At(1, 2).Should().Be(new TextValue("b"));
        result.At(2, 2).Should().Be(new TextValue("y"));
        result.At(1, 3).Should().Be(new TextValue("NF"));
        result.At(2, 3).Should().Be(new TextValue("NF"));
    }

    [Fact]
    public void Xlookup_VerticalArrayLookupAllMatched_MultiColumnReturn_StillSpillsCorrectly()
    {
        // Sibling already-working case (no misses at all) -- must keep working exactly as
        // before, proving the broadcast fix doesn't disturb the fully-matched path.
        var sheet = MakeSheet(
            (1, 1, new TextValue("A")), (2, 1, new TextValue("B")), (3, 1, new TextValue("C")),
            (1, 2, new TextValue("A1")), (1, 3, new TextValue("A2")),
            (2, 2, new TextValue("B1")), (2, 3, new TextValue("B2")),
            (3, 2, new TextValue("C1")), (3, 3, new TextValue("C2")),
            (1, 4, new TextValue("B")), (2, 4, new TextValue("C")));

        var result = _eval.Evaluate("=XLOOKUP(D1:D2,A1:A3,B1:C3)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(2);
        result.At(1, 1).Should().Be(new TextValue("B1"));
        result.At(1, 2).Should().Be(new TextValue("B2"));
        result.At(2, 1).Should().Be(new TextValue("C1"));
        result.At(2, 2).Should().Be(new TextValue("C2"));
    }

    // ── R25-lookup-functions-deep-3 ─────────────────────────────────────────────────────────
    // An explicitly-supplied but blank if_not_found must be returned verbatim, not silently
    // replaced with #N/A -- only a genuinely omitted argument should default to #N/A.

    [Fact]
    public void Xlookup_ExplicitlySuppliedBlankIfNotFound_ReturnsBlankNotNA()
    {
        // A1:A3 = {1,2,3}; C1 is a genuinely empty cell, referenced explicitly as if_not_found.
        // The lookup/return ranges are wrapped in IF(TRUE,...) to force evaluation through the
        // slow path this bucket owns (BuiltInFunctions.Lookup.Modern.cs's Xlookup) -- a bare
        // "=XLOOKUP(99,A1:A3,A1:A3,C1)" takes FormulaEvaluator.LookupFastPaths.cs's direct-range
        // fast path instead (out of this bucket's scope; TryAsRangeRef doesn't match a bare
        // single-cell if_not_found argument like C1, so that path doesn't even bail on it), which
        // has the identical bug duplicated at LookupFastPaths.cs:207-208.
        //
        // FormulaEvaluator.NormalizeTopLevelResult converts a formula whose FINAL top-level result
        // is blank into 0 (matching real Excel: "=A2" with A2 empty displays 0, not blank) -- this
        // is pre-existing, intentional behavior, not part of this fix. So the fix's effect is
        // visible at the top level as "0" instead of the old bug's "#N/A", and is visible directly
        // (pre-normalization) via ISBLANK, which receives the raw internal result.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)));

        _eval.Evaluate("=ISBLANK(XLOOKUP(99,IF(TRUE,A1:A3),IF(TRUE,A1:A3),C1))", sheet).Should().Be(new BoolValue(true));
        _eval.Evaluate("=XLOOKUP(99,IF(TRUE,A1:A3),IF(TRUE,A1:A3),C1)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Xlookup_OmittedIfNotFound_StillDefaultsToNA()
    {
        // Sibling already-working case: a genuinely omitted (3-arg call) if_not_found must keep
        // defaulting to #N/A -- the arity-based fix must not affect this case.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)));

        _eval.Evaluate("=XLOOKUP(99,IF(TRUE,A1:A3),IF(TRUE,A1:A3))", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xlookup_ExplicitlySuppliedNonBlankIfNotFound_StillReturnsSuppliedValue()
    {
        // Sibling already-working case: a non-blank explicit if_not_found value must keep
        // working exactly as before.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)));

        _eval.Evaluate("=XLOOKUP(99,IF(TRUE,A1:A3),IF(TRUE,A1:A3),\"NF\")", sheet).Should().Be(new TextValue("NF"));
    }

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), val);
        return sheet;
    }
}
