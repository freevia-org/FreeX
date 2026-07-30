using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R99-formula-xmatch-lookupvalue-error-guard: when lookup_value is itself a multi-cell (array)
/// range (e.g. =XMATCH(A1:A5,B1:B5), a common spilled-array pattern), XMATCH broadcasts per element
/// via MapTernaryTextArgsGrowBroadcast -> XmatchScalar. Neither the mapper nor XmatchScalar checked
/// whether the individual broadcast lookup_value cell was itself an ErrorValue before comparing it
/// against the lookup array via ScalarEquals/CompareScalar/MatchExactValue. Those comparison helpers
/// treat an ErrorValue operand as simply never-equal (they fall through to `return false`), so the
/// array element quietly became a false #N/A ("not found") instead of propagating the real source
/// error. Real Excel's array-formula elementwise error propagation surfaces the original error at
/// that spilled position instead. Fixed by guarding lookupValue for ErrorValue up front in
/// XmatchScalar, mirroring VlookupScalar/HlookupScalar/MatchScalar (BuiltInFunctions.Lookup.Legacy.cs)
/// and XLOOKUP's own array-lookup_value handling (XlookupRangeLookupValues, Modern.cs:166-168).
/// </summary>
public class R99_XmatchLookupValueErrorGuardTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    private static void AssertGrid(ScalarValue value, ScalarValue[,] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.GetLength(0));
        range.ColCount.Should().Be(expected.GetLength(1));
        for (int r = 0; r < expected.GetLength(0); r++)
            for (int c = 0; c < expected.GetLength(1); c++)
                range.At(r + 1, c + 1).Should().Be(expected[r, c], $"cell ({r + 1},{c + 1})");
    }

    [Fact]
    public void Xmatch_LookupValueRangeWithOneErrorCell_PropagatesSourceErrorNotFalseNA()
    {
        // A1:A3 = 20, #DIV/0!, 30 (lookup_value as a 3x1 broadcast range, middle cell a genuine
        // source error that would never be found in the lookup array). B1:B3 = 10,20,30 (lookup_array).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(20)), (2, 1, ErrorValue.DivByZero), (3, 1, new NumberValue(30)),
            (1, 2, new NumberValue(10)), (2, 2, new NumberValue(20)), (3, 2, new NumberValue(30)));

        var result = _eval.Evaluate("=XMATCH(A1:A3,B1:B3)", sheet);

        // Before the fix the middle cell silently became #N/A ("not found") instead of propagating
        // the real #DIV/0! source error; the other two cells still compute the real exact-match index.
        AssertGrid(result, new ScalarValue[,]
        {
            { new NumberValue(2) },
            { ErrorValue.DivByZero },
            { new NumberValue(3) },
        });
    }

    [Fact]
    public void Xmatch_LookupValueRangeAllValid_SiblingNoRegression_StillBroadcastsNormally()
    {
        // Sibling no-regression: a lookup_value range with no error cell anywhere must still
        // broadcast and match exactly as before this fix.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(30)), (2, 1, new NumberValue(10)), (3, 1, new NumberValue(20)),
            (1, 2, new NumberValue(10)), (2, 2, new NumberValue(20)), (3, 2, new NumberValue(30)));

        var result = _eval.Evaluate("=XMATCH(A1:A3,B1:B3)", sheet);

        AssertGrid(result, new ScalarValue[,]
        {
            { new NumberValue(3) },
            { new NumberValue(1) },
            { new NumberValue(2) },
        });
    }

    [Fact]
    public void Xmatch_LookupValueRangeErrorCell_NotFoundNoLongerFalsePositiveViaIsNa()
    {
        // Downstream ISNA-based logic must see the true error, not a false "value not found" signal.
        // Single-cell sanity check isolating the exact scenario the finding calls out.
        var sheet = MakeSheet(
            (1, 1, ErrorValue.Ref),
            (1, 2, new NumberValue(10)), (2, 2, new NumberValue(20)));

        var result = _eval.Evaluate("=ISNA(XMATCH(A1,B1:B2))", sheet);

        // #REF! is not #N/A, so ISNA must be FALSE -- the error must reach ISNA as #REF!, not be
        // swallowed into a #N/A that ISNA would then report as TRUE.
        result.Should().Be(new BoolValue(false));
    }
}
