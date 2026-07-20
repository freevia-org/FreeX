using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R51-formula-lookup-xlookup-binary-3-1/-2: XLOOKUP/XMATCH blank-cell coercion gaps.
///
/// -3-1: ScalarEquals (BuiltInFunctions.Coercion.cs) coerced a blank operand to NumberValue(0)
/// for ANY non-text other operand -- including a BoolValue -- instead of BoolValue(false), so a
/// genuinely blank cell never compared equal to TRUE/FALSE even though Excel treats a blank as
/// FALSE for comparison purposes (and FreeX's own CompareScalar/CoerceBlankForCompare already
/// gets this right). This broke XMATCH/XLOOKUP's exact match_mode (0) whenever a blank cell was
/// compared against a boolean lookup value.
///
/// -3-2: TryFindApproximateMatchIndexLinear (BuiltInFunctions.Lookup.Modern.cs) filtered
/// approximate-match (match_mode -1/1) candidates by type class BEFORE coercing, so a genuinely
/// blank candidate (type class 0) was skipped outright instead of being allowed through to
/// CompareScalar's own blank-to-0 coercion -- unlike the classic MATCH/VLOOKUP/HLOOKUP fast
/// paths (FormulaEvaluator.LookupFastPaths.cs), which already let blank candidates through.
/// </summary>
public sealed class R51_XlookupBlankBoolAndApproximateBlankTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    // --- R51-formula-lookup-xlookup-binary-3-1 ---

    [Fact]
    public void Xmatch_ExactMatch_BlankCellComparedToBoolean_CoercesBlankToFalse()
    {
        // A1 is genuinely blank (never set), A2 = TRUE. XMATCH(FALSE, A1:A2, 0) must find the
        // blank cell at row 1, since Excel coerces a blank to FALSE for comparison purposes.
        var sheet = MakeSheet((2, 1, new BoolValue(true)));

        _eval.Evaluate("=XMATCH(FALSE,A1:A2,0)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Xmatch_ExactMatch_BooleanCandidates_StillMatchNormally()
    {
        // Sibling no-regression: plain boolean-vs-boolean exact match (no blanks involved) must
        // still work exactly as before the fix.
        var sheet = MakeSheet(
            (1, 1, new BoolValue(false)),
            (2, 1, new BoolValue(true)));

        _eval.Evaluate("=XMATCH(TRUE,A1:A2,0)", sheet).Should().Be(new NumberValue(2));
    }

    // --- R51-formula-lookup-xlookup-binary-3-2 ---

    [Fact]
    public void Xmatch_ApproximateNextSmaller_LinearScan_BlankCandidateCoercesToZero()
    {
        // A1 is genuinely blank (coerces to 0), A2 = 10, A3 = 20. XMATCH(0.5, A1:A3, -1, 1) asks
        // for an exact match or, failing that, the next-smaller value scanning first-to-last.
        // 0 <= 0.5 so the blank cell is the (only) qualifying "next smaller" candidate -> row 1.
        //
        // The lookup array is wrapped in IF(TRUE,...) so the argument node is a FunctionCallNode
        // rather than a bare RangeRefNode -- this routes evaluation through the general
        // BuiltInFunctions.Xmatch/TryFindApproximateMatchIndexLinear implementation (the fix
        // target in this bucket) instead of FormulaEvaluator.LookupFastPaths.cs's direct-range
        // fast path (a separate, out-of-scope call site with the identical bug).
        var sheet = MakeSheet(
            (2, 1, new NumberValue(10)),
            (3, 1, new NumberValue(20)));

        _eval.Evaluate("=XMATCH(0.5,IF(TRUE,A1:A3),-1,1)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Xmatch_ApproximateNextSmaller_LinearScan_BlankCandidateStillExcludedWhenTooLarge()
    {
        // Sibling no-regression: letting the blank candidate through the type-class filter must
        // not make it match unconditionally -- CompareScalar's coercion (blank -> 0) still has to
        // satisfy the "next smaller" (<=) relationship. Here the lookup value is -5, so the
        // coerced-to-0 blank (0 > -5) does NOT qualify, and neither do 10/20 -> #N/A.
        var sheet = MakeSheet(
            (2, 1, new NumberValue(10)),
            (3, 1, new NumberValue(20)));

        _eval.Evaluate("=XMATCH(-5,IF(TRUE,A1:A3),-1,1)", sheet).Should().Be(ErrorValue.NA);
    }
}
