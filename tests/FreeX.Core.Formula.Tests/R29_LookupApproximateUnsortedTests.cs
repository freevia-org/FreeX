using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-29 fix-bucket "lookup-legacy" regression test.
///
/// R29-lookup-repass-1: VLOOKUP/HLOOKUP/MATCH's approximate-match scan
/// (<c>BuiltInFunctions.Lookup.Legacy.cs</c>: VlookupScalar/HlookupScalar/MatchScalar) used to abort
/// the whole scan with an "else break" the moment it hit a same-type-class row whose value already
/// exceeded (or, for descending MATCH, fell below) the lookup value — even if that row was the very
/// first row scanned. Real Excel does not verify the table is actually sorted before performing an
/// approximate match and still returns a deterministic, non-error result for genuinely unsorted data;
/// it does not unconditionally error out just because the first row happens to be "out of order".
/// The fix removes the early break so the scan keeps going (mirroring the no-break full scan already
/// used a few lines below by the LOOKUP() vector form in the same file), fixing the bug case below
/// without changing the result for the already-working sorted-ascending sibling case.
///
/// A direct literal-range table/vector argument (e.g. "=VLOOKUP(2,A1:B4,2,TRUE)") is intercepted by
/// FormulaEvaluator's "direct range" fast paths (FormulaEvaluator.LookupFastPaths.cs), which have
/// their own separate copy of the same early-break scan and are out of scope for this fix (a
/// different file/bucket). These tests route the table/vector argument through a defined name
/// instead, which those fast paths deliberately don't intercept (their TryAsRangeRef predicate only
/// matches a literal RangeRefNode), so the formula falls through to the general evaluation path and
/// actually exercises the fixed BuiltInFunctions.Lookup.Legacy.cs scalar functions.
/// </summary>
public partial class FunctionLibraryTests
{
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

    [Fact]
    public void Vlookup_Approximate_UnsortedTable_FindsLaterMatch_InsteadOfNA()
    {
        // A1:A4 = {100,1,2,3} (unsorted), B1:B4 = {1000,10,20,30}.
        // Old behavior: row 1 (100) already exceeds the lookup value (2), so the scan aborted on the
        // very first iteration and returned #N/A even though row 3 is an exact match.
        var (workbook, sheet) = MakeNamedRangeWorkbook(4, 2,
            (1, 1, new NumberValue(100)), (1, 2, new NumberValue(1000)),
            (2, 1, new NumberValue(1)), (2, 2, new NumberValue(10)),
            (3, 1, new NumberValue(2)), (3, 2, new NumberValue(20)),
            (4, 1, new NumberValue(3)), (4, 2, new NumberValue(30)));

        _eval.Evaluate("=VLOOKUP(2,Tbl,2,TRUE)", sheet, workbook).Should().Be(new NumberValue(20));
        // Omitted 4th arg defaults to approximate match too, and must hit the same fixed path.
        _eval.Evaluate("=VLOOKUP(2,Tbl,2)", sheet, workbook).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Hlookup_Approximate_UnsortedTable_FindsLaterMatch_InsteadOfNA()
    {
        // Row-oriented mirror of the VLOOKUP case: first row = {100,1,2,3}, second row = {1000,10,20,30}.
        var (workbook, sheet) = MakeNamedRangeWorkbook(2, 4,
            (1, 1, new NumberValue(100)), (1, 2, new NumberValue(1)), (1, 3, new NumberValue(2)), (1, 4, new NumberValue(3)),
            (2, 1, new NumberValue(1000)), (2, 2, new NumberValue(10)), (2, 3, new NumberValue(20)), (2, 4, new NumberValue(30)));

        _eval.Evaluate("=HLOOKUP(2,Tbl,2,TRUE)", sheet, workbook).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Match_Approximate_Ascending_UnsortedVector_FindsLaterMatch_InsteadOfNA()
    {
        // Same unsorted vector as the VLOOKUP case: {100,1,2,3}; MATCH(2,...,1) must land on
        // position 3 (the exact match) instead of erroring out on the leading 100.
        var (workbook, sheet) = MakeNamedRangeWorkbook(4, 1,
            (1, 1, new NumberValue(100)),
            (2, 1, new NumberValue(1)),
            (3, 1, new NumberValue(2)),
            (4, 1, new NumberValue(3)));

        _eval.Evaluate("=MATCH(2,Tbl,1)", sheet, workbook).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Match_Approximate_Descending_UnsortedVector_FindsLaterMatch_InsteadOfNA()
    {
        // Descending mirror: {1,100,50,2}. Row 1 (1) is already below the lookup value (2), so the old
        // descending scan ("smallest value >= lookupValue") aborted on the very first iteration.
        var (workbook, sheet) = MakeNamedRangeWorkbook(4, 1,
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(100)),
            (3, 1, new NumberValue(50)),
            (4, 1, new NumberValue(2)));

        _eval.Evaluate("=MATCH(2,Tbl,-1)", sheet, workbook).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Vlookup_Approximate_SortedTable_StillReturnsBestFit_SiblingCase()
    {
        // Sibling regression: ordinary ascending-sorted data (the common, already-working case) must
        // still return the correct "largest value <= lookup" best fit after removing the early break.
        var (workbook, sheet) = MakeNamedRangeWorkbook(3, 2,
            (1, 1, new NumberValue(1)), (1, 2, new TextValue("one")),
            (2, 1, new NumberValue(10)), (2, 2, new TextValue("ten")),
            (3, 1, new NumberValue(100)), (3, 2, new TextValue("hundred")));

        _eval.Evaluate("=VLOOKUP(50,Tbl,2,TRUE)", sheet, workbook).Should().Be(new TextValue("ten"));
    }

    // --- Literal-range fast-path mirror (FormulaEvaluator.LookupFastPaths.cs) ---
    // The common call shape (a literal RangeRefNode argument, e.g. "=VLOOKUP(2,A1:B4,2,TRUE)") is
    // intercepted by the direct-range fast paths, which carried their OWN copy of the same early-break
    // bug. These tests use literal ranges (no defined name) so they exercise
    // EvaluateLegacyLookupDirectTable / EvaluateMatchDirectRange, confirming the fast-path mirror of
    // R29-lookup-repass-1 agrees with the slow path above.

    private static (Workbook workbook, Sheet sheet) MakeLiteralWorkbook(params (int row, int col, ScalarValue val)[] cells)
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), val);
        return (workbook, sheet);
    }

    [Fact]
    public void Vlookup_Approximate_UnsortedLiteralRange_FastPath_FindsLaterMatch_InsteadOfNA()
    {
        var (workbook, sheet) = MakeLiteralWorkbook(
            (1, 1, new NumberValue(100)), (1, 2, new NumberValue(1000)),
            (2, 1, new NumberValue(1)), (2, 2, new NumberValue(10)),
            (3, 1, new NumberValue(2)), (3, 2, new NumberValue(20)),
            (4, 1, new NumberValue(3)), (4, 2, new NumberValue(30)));

        _eval.Evaluate("=VLOOKUP(2,A1:B4,2,TRUE)", sheet, workbook).Should().Be(new NumberValue(20));
        _eval.Evaluate("=VLOOKUP(2,A1:B4,2)", sheet, workbook).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Match_Approximate_Ascending_UnsortedLiteralRange_FastPath_FindsLaterMatch_InsteadOfNA()
    {
        var (workbook, sheet) = MakeLiteralWorkbook(
            (1, 1, new NumberValue(100)),
            (2, 1, new NumberValue(1)),
            (3, 1, new NumberValue(2)),
            (4, 1, new NumberValue(3)));

        _eval.Evaluate("=MATCH(2,A1:A4,1)", sheet, workbook).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Match_Approximate_Descending_UnsortedLiteralRange_FastPath_FindsLaterMatch_InsteadOfNA()
    {
        var (workbook, sheet) = MakeLiteralWorkbook(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(100)),
            (3, 1, new NumberValue(50)),
            (4, 1, new NumberValue(2)));

        _eval.Evaluate("=MATCH(2,A1:A4,-1)", sheet, workbook).Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Vlookup_Approximate_SortedLiteralRange_FastPath_StillReturnsBestFit_SiblingCase()
    {
        // Sibling regression on the fast path: ordinary ascending-sorted literal range must still
        // return the "largest value <= lookup" best fit after removing the early break.
        var (workbook, sheet) = MakeLiteralWorkbook(
            (1, 1, new NumberValue(1)), (1, 2, new TextValue("one")),
            (2, 1, new NumberValue(10)), (2, 2, new TextValue("ten")),
            (3, 1, new NumberValue(100)), (3, 2, new TextValue("hundred")));

        _eval.Evaluate("=VLOOKUP(50,A1:B3,2,TRUE)", sheet, workbook).Should().Be(new TextValue("ten"));
    }
}
