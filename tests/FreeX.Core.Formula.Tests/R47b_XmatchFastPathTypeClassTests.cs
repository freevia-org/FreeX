using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R47b-xmatch-fastpath-typeclass (r46-deferred xmatch-approx-fast-path-typeclass): the
/// approximate-match scan in FormulaEvaluator.LookupFastPaths.cs's
/// TryFindDirectApproximateXmatchIndex (the bare-range-literal fast path for XMATCH/XLOOKUP)
/// lacked the type-class filter that r46 added to the general, non-fast-path implementation
/// (BuiltInFunctions.Lookup.Modern.cs's TryFindApproximateMatchIndexLinear): a text or boolean
/// candidate must never qualify as a numeric "next larger/smaller" approximate match. Without
/// the filter, mixed-type-order comparisons let a text candidate silently win the approximate
/// scan against a purely numeric lookup value.
/// </summary>
public sealed class R47b_XmatchFastPathTypeClassTests
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
    public void Xmatch_FastPath_Approximate_TextCandidateAmongNumeric_DoesNotQualifyAsNextLarger()
    {
        // {5, "Banana", "Apple"}: no numeric candidate is >= 15, so the two text candidates must
        // not be considered eligible substitutes for a numeric "next larger" match -- the result
        // must be #N/A, not a match on "Apple" (which mixed-type ordering would otherwise win).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new TextValue("Banana")),
            (3, 1, new TextValue("Apple")));

        _eval.Evaluate("=XMATCH(15,A1:A3,1)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xmatch_FastPath_Approximate_QualifyingNumericCandidate_StillMatches()
    {
        // Sibling no-regression guard: with a genuine qualifying numeric candidate (20 >= 15)
        // present alongside a text value, the numeric candidate must still be found and the
        // text value must still be correctly excluded.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(5)),
            (2, 1, new NumberValue(20)),
            (3, 1, new TextValue("Banana")));

        _eval.Evaluate("=XMATCH(15,A1:A3,1)", sheet).Should().Be(new NumberValue(2));
    }
}
