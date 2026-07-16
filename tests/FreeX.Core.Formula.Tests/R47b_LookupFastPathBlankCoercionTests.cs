using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R47b-lookup-fastpath-blank: mirrors R47-formula-vlookup-hlookup-approx-3-1, but targets the
/// independently-implemented approximate-match scan in FormulaEvaluator.LookupFastPaths.cs
/// (EvaluateLegacyLookupDirectTable / EvaluateMatchDirectRange), which intercepts the common
/// "bare range literal" call shape (e.g. VLOOKUP(x, A1:B5, 2, TRUE)) before it ever reaches
/// BuiltInFunctions.Lookup.Legacy.cs's slow path -- so the r47 fix there did not cover this shape.
///
/// Unlike R47_LookupApproximateBlankCoercionTests (which deliberately wraps the range in
/// INDEX(...,0,0) to force the slow path), these tests use a bare range literal so evaluation is
/// forced through the fast path's own scan.
/// </summary>
public sealed class R47b_LookupFastPathBlankCoercionTests
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
    public void Vlookup_FastPath_Approximate_BlankBetweenNumericKeys_CoercesToZero_NotSkipped()
    {
        // Ascending column [-5, blank, 10] (blank coerces to 0, giving effective [-5, 0, 10]).
        // A bare range literal (no INDEX wrapper) forces the direct-range fast path.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-5)), (1, 2, new TextValue("low")),
            (2, 1, BlankValue.Instance), (2, 2, new TextValue("mid")),
            (3, 1, new NumberValue(10)), (3, 2, new TextValue("high")));

        _eval.Evaluate("=VLOOKUP(0.5,A1:B3,2,TRUE)", sheet)
            .Should().Be(new TextValue("mid"));
    }

    [Fact]
    public void Vlookup_FastPath_Approximate_NoBlankInColumn_StillFindsCorrectRow_NotRegressed()
    {
        // Sibling regression guard: an ordinary all-numeric sorted column (no blanks) must still
        // resolve correctly through the same bare-range fast path.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-5)), (1, 2, new TextValue("low")),
            (2, 1, new NumberValue(1)), (2, 2, new TextValue("mid")),
            (3, 1, new NumberValue(10)), (3, 2, new TextValue("high")));

        _eval.Evaluate("=VLOOKUP(0.5,A1:B3,2,TRUE)", sheet)
            .Should().Be(new TextValue("low"));
    }

    [Fact]
    public void Match_FastPath_Approximate_Ascending_BlankBetweenNumericKeys_CoercesToZero_NotSkipped()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-5)),
            (2, 1, BlankValue.Instance),
            (3, 1, new NumberValue(10)));

        // Effective sequence [-5, 0, 10]: MATCH(0.5, ..., 1) should land on row 2 (the blank).
        _eval.Evaluate("=MATCH(0.5,A1:A3,1)", sheet)
            .Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Match_FastPath_Approximate_Descending_BlankBetweenNumericKeys_CoercesToZero_NotSkipped()
    {
        // Descending column [10, blank, -5] (blank coerces to 0, giving effective [10, 0, -5]).
        // MATCH(-2, ..., -1) finds the smallest value >= -2: 10 qualifies, the coerced blank (0)
        // also qualifies (0 >= -2) and -- being scanned after 10 -- overtakes it as the smallest
        // qualifying value so far, but -5 does not qualify (-5 < -2). The last-updated qualifying
        // index (the blank row) must win, landing on row 2, not row 1.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, BlankValue.Instance),
            (3, 1, new NumberValue(-5)));

        _eval.Evaluate("=MATCH(-2,A1:A3,-1)", sheet)
            .Should().Be(new NumberValue(2));
    }
}
