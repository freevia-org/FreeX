using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for round-42 finding R42-formula-xlookup-xmatch-2-1 in
/// BuiltInFunctions.Lookup.Modern.cs's XlookupRangeLookupValues:
///
/// XLOOKUP with an array lookup_value and a multi-column (or multi-row) return_array used to
/// truncate the spilled result to a single column (or row) whenever EVERY lookup in the array
/// missed. This is precisely the edge case left uncovered by R25-lookup-functions-deep-2's own
/// regression tests (R25_LookupModernTests.cs), all of which include at least one matching row --
/// none exercise the all-miss case.
///
/// Root cause: `hasRangeResult` (used to decide whether the output needs reshaping to the
/// return_array's width/height) only flips true when at least one lookup hits and XlookupReturnAt
/// produces a RangeValue. When every lookup misses, every per-cell result is the bare scalar
/// if_not_found, hasRangeResult stays false, and the code short-circuits to
/// `new RangeValue(results)` sized to lookup_value's own shape -- never widened to match
/// return_array. Fixed by additionally reshaping whenever return_array's non-lookup axis is
/// greater than 1, independent of hit/miss outcome, falling back to return_array's own dimension
/// for the output width/height when no hit produced a RangeValue to infer it from.
/// </summary>
public class R42_XlookupAllMissMultiColumnSpillTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Xlookup_VerticalArrayLookupAllMissed_MultiColumnReturn_SpillsFullWidthNotOneColumn()
    {
        // A1:A3 = {98;99;100} (vertical lookup values, none present anywhere in D1:D3).
        // D1:D3 = {1;2;3} (lookup range). E1:F3 = 2-column return range.
        // Real Excel spills a 3-row x 2-column array of "NF" (matching E1:F3's width), not a
        // single column of three "NF" cells.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(98)), (2, 1, new NumberValue(99)), (3, 1, new NumberValue(100)),
            (1, 4, new NumberValue(1)), (2, 4, new NumberValue(2)), (3, 4, new NumberValue(3)),
            (1, 5, new TextValue("a")), (1, 6, new TextValue("x")),
            (2, 5, new TextValue("b")), (2, 6, new TextValue("y")),
            (3, 5, new TextValue("c")), (3, 6, new TextValue("z")));

        var result = _eval.Evaluate("=XLOOKUP(A1:A3,D1:D3,E1:F3,\"NF\")", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(3);
        result.ColCount.Should().Be(2);
        result.At(1, 1).Should().Be(new TextValue("NF"));
        result.At(1, 2).Should().Be(new TextValue("NF"));
        result.At(2, 1).Should().Be(new TextValue("NF"));
        result.At(2, 2).Should().Be(new TextValue("NF"));
        result.At(3, 1).Should().Be(new TextValue("NF"));
        result.At(3, 2).Should().Be(new TextValue("NF"));
    }

    [Fact]
    public void Xlookup_HorizontalArrayLookupAllMissed_MultiRowReturn_SpillsFullHeightNotOneRow()
    {
        // Opposite orientation of the same fix: a 1-row lookupValues array, none matching, with
        // a multi-row return array -- must spill the full return_array height, not collapse to
        // a single row.
        var sheet = MakeSheet(
            (5, 1, new NumberValue(98)), (5, 2, new NumberValue(99)), (5, 3, new NumberValue(100)),
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (2, 1, new TextValue("a")), (3, 1, new TextValue("x")),
            (2, 2, new TextValue("b")), (3, 2, new TextValue("y")),
            (2, 3, new TextValue("c")), (3, 3, new TextValue("z")));

        var result = _eval.Evaluate("=XLOOKUP(A5:C5,A1:C1,A2:C3,\"NF\")", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(3);
        result.At(1, 1).Should().Be(new TextValue("NF"));
        result.At(2, 1).Should().Be(new TextValue("NF"));
        result.At(1, 2).Should().Be(new TextValue("NF"));
        result.At(2, 2).Should().Be(new TextValue("NF"));
        result.At(1, 3).Should().Be(new TextValue("NF"));
        result.At(2, 3).Should().Be(new TextValue("NF"));
    }

    [Fact]
    public void Xlookup_VerticalArrayLookupAllMissed_SingleColumnReturn_StillSpillsOneColumn()
    {
        // Sibling no-regression case: when return_array is single-column, an all-miss array
        // lookup must keep spilling a plain single column of if_not_found (no reshaping needed,
        // since return_array's own width is already 1) -- proves the fix's extra reshape
        // condition doesn't kick in when it shouldn't.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(98)), (2, 1, new NumberValue(99)),
            (1, 4, new NumberValue(1)), (2, 4, new NumberValue(2)), (3, 4, new NumberValue(3)),
            (1, 5, new TextValue("a")), (2, 5, new TextValue("b")), (3, 5, new TextValue("c")));

        var result = _eval.Evaluate("=XLOOKUP(A1:A2,D1:D3,E1:E3,\"NF\")", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be(2);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new TextValue("NF"));
        result.At(2, 1).Should().Be(new TextValue("NF"));
    }

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), val);
        return sheet;
    }
}
