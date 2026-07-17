using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R48-meta-2: LOOKUP's own dedicated vector-form fast path (EvaluateLookupDirectVectors, reached
/// via TryEvaluateLookupDirectRanges for the common bare-range shape LOOKUP(value, vector[,
/// result_vector])) never got the r47/r47b blank-coercion fix that VLOOKUP/HLOOKUP/MATCH's fast
/// paths (EvaluateLegacyLookupDirectTable / EvaluateMatchDirectRange, see
/// R47b_LookupFastPathBlankCoercionTests) and LOOKUP's own general/slow path
/// (BuiltInFunctions.Lookup.Legacy.cs, see R47_LookupApproximateBlankCoercionTests) both already
/// received: a blank candidate cell was filtered out by the approximate-match type-class check
/// instead of being let through so CompareScalar's blank-to-0 coercion gets a chance to run,
/// producing #N/A from the fast path where the slow path (and real Excel) finds a match.
///
/// Unlike R47_LookupApproximateBlankCoercionTests (which forces the slow path), these tests use a
/// bare range literal so evaluation is forced through TryEvaluateLookupDirectRanges's dedicated
/// vector fast path.
/// </summary>
public sealed class R48_LookupVectorFastPathBlankCoercionTests
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
    public void Lookup_VectorFastPath_BlankLeadingKey_CoercesToZero_NotSkipped()
    {
        // A1 is genuinely blank (never set), A2 = 10. LOOKUP(5, A1:A2) is the bare-range vector
        // form: a single-column range, so the fast path's lookup and result vectors are the same
        // A1:A2 range. Excel coerces the blank A1 key to 0 for the approximate-match scan; 0 <= 5
        // qualifies while A2's 10 does not (10 > 5), so the match lands on A1 and the returned
        // value is A1's own content read back as 0 (Excel likewise displays 0 for a blank cell
        // referenced through a formula result) -- exactly what the slow path already produces, and
        // what #N/A from the unfixed fast path diverged from.
        var sheet = MakeSheet((2, 1, new NumberValue(10)));

        _eval.Evaluate("=LOOKUP(5,A1:A2)", sheet)
            .Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Lookup_VectorFastPath_NoBlankInVector_StillFindsCorrectMatch_NotRegressed()
    {
        // Sibling regression guard: an ordinary all-numeric ascending vector (no blanks) must still
        // resolve correctly through the same bare-range vector fast path.
        var sheet = MakeSheet((1, 1, new NumberValue(1)), (2, 1, new NumberValue(10)));

        _eval.Evaluate("=LOOKUP(5,A1:A2)", sheet)
            .Should().Be(new NumberValue(1));
    }
}
