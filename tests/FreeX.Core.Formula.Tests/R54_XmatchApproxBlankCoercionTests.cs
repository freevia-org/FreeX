using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R54-formula-match-xmatch-4-1: TryFindDirectApproximateXmatchIndex (the literal-range fast
/// path behind XMATCH/XLOOKUP's approximate match_mode -1/1) filtered candidates with a bare
/// `ApproxLookupTypeClass(candidate) != lookupClass` check that, unlike every sibling
/// approximate-match implementation in this codebase (EvaluateMatchDirectRange two functions
/// above in this same file, and BuiltInFunctions.Lookup.Modern.cs's general/slow-path XMATCH),
/// did not exempt a genuinely blank candidate cell from the type-class filter. A blank cell
/// participating in an approximate numeric match must coerce to 0 (matching VLOOKUP/HLOOKUP/
/// MATCH), not be skipped as if it were a foreign type.
/// </summary>
public sealed class R54_XmatchApproxBlankCoercionTests
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
    public void Xmatch_FastPath_Approximate_NextSmaller_BlankBetweenNumericCandidates_CoercesToZero_NotSkipped()
    {
        // A1=-5, A2=<blank>, A3=20. Effective sequence for an approximate ("next smaller or
        // exact", match_mode -1) match is [-5, 0, 20]. The largest qualifying value <= 3 is
        // the coerced blank (0) at row 2, not -5 at row 1 -- so XMATCH must return 2, not 1.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-5)),
            (2, 1, BlankValue.Instance),
            (3, 1, new NumberValue(20)));

        _eval.Evaluate("=XMATCH(3,A1:A3,-1)", sheet)
            .Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Xmatch_FastPath_Approximate_NextSmaller_NoBlankInRange_StillFindsCorrectRow_NotRegressed()
    {
        // Sibling no-regression guard: the same shape without any blank candidate must still
        // resolve to the largest value <= 3 (here, 1 at row 2) through the identical fast path.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-5)),
            (2, 1, new NumberValue(1)),
            (3, 1, new NumberValue(20)));

        _eval.Evaluate("=XMATCH(3,A1:A3,-1)", sheet)
            .Should().Be(new NumberValue(2));
    }
}
