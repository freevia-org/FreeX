using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R106: XMATCH's and XLOOKUP's linear approximate-match helper
/// (TryFindApproximateMatchIndexLinear in BuiltInFunctions.Lookup.Modern.cs, and its duplicated
/// fast-path twin TryFindDirectApproximateXmatchIndex in FormulaEvaluator.LookupFastPaths.cs)
/// computed the type-class filter from the raw ApproxLookupTypeClass(lookupValue) instead of
/// ApproxLookupClassForLookupValue(lookupValue). ApproxLookupTypeClass(BlankValue) returns the
/// dedicated "blank" class, distinct from number/text/bool, so the filter
/// "if (candidate is not BlankValue && ApproxLookupTypeClass(candidate) != lookupClass) continue;"
/// skipped every non-blank (e.g. numeric) candidate whenever lookup_value itself was blank --
/// silently discarding any genuine next-smaller/next-larger match and returning #N/A, even though
/// Excel coerces a blank lookup_value to 0 for approximate match (the same coercion already
/// applied to VLOOKUP/HLOOKUP/MATCH/LOOKUP by the R75 fix, ApproxLookupClassForLookupValue).
///
/// Covers both the general/slow path (array-literal lookup vector, which bails out of the
/// direct-range fast path) and the direct-range fast path (bare cell-range arguments), since the
/// bug was duplicated verbatim in both TryFindApproximateMatchIndexLinear and
/// TryFindDirectApproximateXmatchIndex.
/// </summary>
public class R106_XmatchXlookupApproximateBlankLookupValueTests
{
    private readonly FormulaEvaluator _eval = new();

    // B1:B3 = -5,-10,30 (unsorted -- exercises the "closest" scan, not a sorted binary search).
    // A1 is left unset (blank). C1:C3 = 100,200,300 (XLOOKUP return array).
    private static (Workbook workbook, Sheet sheet) MakeWorkbook()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        double[] keys = [-5, -10, 30];
        for (uint r = 1; r <= 3; r++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(keys[r - 1]));
            sheet.SetCell(new CellAddress(sheet.Id, r, 3), new NumberValue(r * 100));
        }
        return (workbook, sheet);
    }

    [Fact]
    public void Xmatch_BlankLookupValue_ApproximateMatch_DirectRangeFastPath_FindsNumericCandidate()
    {
        var (workbook, sheet) = MakeWorkbook();

        // A1 (blank cell ref, not a multi-cell range) as lookup_value + B1:B3 as a bare cell
        // range both take the direct-range fast path (TryFindDirectApproximateXmatchIndex).
        // A1 -> coerced to 0 -> largest candidate <= 0 among {-5,-10,30} is -5, at row 1.
        var result = _eval.Evaluate("=XMATCH(A1,B1:B3,-1,1)", sheet, workbook);

        result.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Xlookup_BlankLookupValue_ApproximateMatch_DirectRangeFastPath_FindsNumericCandidate()
    {
        var (workbook, sheet) = MakeWorkbook();

        // Same fast path, via XLOOKUP: A1 -> 0 -> nearest-smaller-or-equal candidate is -5 (row 1) -> C1 = 100.
        var result = _eval.Evaluate("=XLOOKUP(A1,B1:B3,C1:C3,,-1)", sheet, workbook);

        result.Should().Be(new NumberValue(100));
    }

    [Fact]
    public void Xmatch_BlankLookupValue_ApproximateMatch_ArrayLiteral_SlowPath_FindsNumericCandidate()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        // A1 left blank. Lookup vector is an array literal (not a bare range ref), which bails
        // the direct-range fast path and forces the general TryFindApproximateMatchIndexLinear path.
        var result = _eval.Evaluate("=XMATCH(A1,{-5,-10,30},-1,1)", sheet, workbook);

        result.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Xmatch_NonBlankApproximateLookup_Unchanged_SiblingNoRegression()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(-5));

        // A1 = -5 (non-blank, genuinely equals a candidate) -> exact hit at row 1, unaffected by
        // the blank-coercion fix.
        var result = _eval.Evaluate("=XMATCH(A1,B1:B3,-1,1)", sheet, workbook);

        result.Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Xmatch_BlankCandidateStillMatches_SiblingNoRegression()
    {
        // Verifies the pre-existing "let a blank CANDIDATE through the type-class filter" branch
        // (R29-lookup-repass-1) still works after this fix: lookup_value is a genuine non-blank
        // 0, and the closest candidate is a blank cell (coerced to 0 by CompareScalar), which
        // should still be picked as an exact match via the equality scan.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(0));
        // B1 left blank, B2 = 5.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(5));

        var result = _eval.Evaluate("=XMATCH(A1,B1:B2,-1,1)", sheet, workbook);

        result.Should().Be(new NumberValue(1));
    }
}
