using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R99-formula-xmatch-xlookup-mode-error-guard: when match_mode (XMATCH) or search_mode/match_mode
/// (XLOOKUP) is supplied as a multi-cell range argument (cross-broadcast against lookup_value the
/// same way R98 fixed for lookup_value/col_index/match_type), XmatchScalar/XlookupScalar called the
/// raw ToNumber(...) helper on the broadcast per-cell value with no ErrorValue guard. ToNumber's
/// default case throws FormulaEvalException("#VALUE!", ...) for anything that isn't a
/// Number/DateTime/Bool/Blank/parseable-text -- including ErrorValue. That throw is not caught
/// anywhere inside the MapTernaryTextArgsGrowBroadcast loop; it unwinds all the way out to the single
/// blanket catch around the WHOLE function call in FormulaEvaluator.Functions.cs, which turns the
/// entire spilled array result into ONE #VALUE! scalar, discarding every other array element that
/// would otherwise have computed correctly. Real Excel dynamic arrays propagate an error only at the
/// affected spilled cell. Fixed by guarding matchModeValue/searchModeValue for ErrorValue up front in
/// XmatchScalar and XlookupScalar, mirroring the established VlookupScalar/HlookupScalar/MatchScalar
/// pattern (BuiltInFunctions.Lookup.Legacy.cs).
/// </summary>
public class R99_XmatchXlookupModeErrorGuardTests
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
    public void Xmatch_MatchModeRangeWithOneErrorCell_OnlyThatSpilledCellIsError()
    {
        // A1 = 20 (lookup_value). B1:B3 = 10,20,30 (lookup_array). C1:C3 = 0, #REF!, 0
        // (match_mode as a 3x1 broadcast range, middle cell a genuine error).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(20)),
            (1, 2, new NumberValue(10)), (2, 2, new NumberValue(20)), (3, 2, new NumberValue(30)),
            (1, 3, new NumberValue(0)), (2, 3, ErrorValue.Ref), (3, 3, new NumberValue(0)));

        var result = _eval.Evaluate("=XMATCH(A1,B1:B3,C1:C3)", sheet);

        // Before the fix this whole call threw and collapsed to a single #VALUE! scalar (not even a
        // RangeValue). After the fix only the middle spilled cell is #REF!; the other two still
        // compute the real exact-match index (B2=20 -> position 2).
        AssertGrid(result, new ScalarValue[,]
        {
            { new NumberValue(2) },
            { ErrorValue.Ref },
            { new NumberValue(2) },
        });
    }

    [Fact]
    public void Xlookup_SearchModeRangeWithOneErrorCell_OnlyThatSpilledCellIsError()
    {
        // A1 = 20 (lookup_value). B1:B3 = 10,20,30 (lookup_array). D1:D3 = 100,200,300 (return_array).
        // F1:F3 = 1, #DIV/0!, 1 (search_mode as a 3x1 broadcast range, middle cell a genuine error).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(20)),
            (1, 2, new NumberValue(10)), (2, 2, new NumberValue(20)), (3, 2, new NumberValue(30)),
            (1, 4, new NumberValue(100)), (2, 4, new NumberValue(200)), (3, 4, new NumberValue(300)),
            (1, 6, new NumberValue(1)), (2, 6, ErrorValue.DivByZero), (3, 6, new NumberValue(1)));

        // =XLOOKUP(A1,B1:B3,D1:D3,,0,F1:F3) -- match_mode=0 (exact), search_mode=F1:F3.
        var result = _eval.Evaluate("=XLOOKUP(A1,B1:B3,D1:D3,,0,F1:F3)", sheet);

        AssertGrid(result, new ScalarValue[,]
        {
            { new NumberValue(200) },
            { ErrorValue.DivByZero },
            { new NumberValue(200) },
        });
    }

    [Fact]
    public void Xmatch_MatchModeRangeAllValid_SiblingNoRegression_StillCrossBroadcastsNormally()
    {
        // Sibling no-regression: the R98 cross-broadcast behavior for an all-valid match_mode range
        // must still work exactly as before this fix (no error cell anywhere in the broadcast).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(20)),
            (1, 2, new NumberValue(10)), (2, 2, new NumberValue(20)), (3, 2, new NumberValue(30)),
            (1, 3, new NumberValue(0)), (2, 3, new NumberValue(0)), (3, 3, new NumberValue(0)));

        var result = _eval.Evaluate("=XMATCH(A1,B1:B3,C1:C3)", sheet);

        AssertGrid(result, new ScalarValue[,]
        {
            { new NumberValue(2) },
            { new NumberValue(2) },
            { new NumberValue(2) },
        });
    }
}
