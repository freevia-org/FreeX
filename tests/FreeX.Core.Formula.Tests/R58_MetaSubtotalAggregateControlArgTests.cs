using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R58-meta-1: the r57 fix that added SUBTOTAL/AGGREGATE to SingleCellReferenceRangeFunctions
/// (so a bare single-cell DATA argument gets wrapped into a 1x1 RangeValue carrying row
/// provenance) unconditionally wrapped EVERY bare-cell-ref argument, including AGGREGATE's own
/// leading control arguments (function_num, options) and SUBTOTAL's function_num. When those
/// control args are themselves bare cell references (not literals) and the fast-path direct-range
/// evaluation bails to the slow path, ToNumber(RangeValue) has no case and throws, surfacing as
/// #VALUE! instead of the correct numeric result. The fix makes the wrapping argIndex-aware
/// (IsSingleCellReferenceRangeDataArgument), leaving control args to fall through to plain scalar
/// evaluation while still wrapping the actual data/range arguments (preserving the r57 hidden-row
/// exclusion behavior).
/// </summary>
public sealed class R58_MetaSubtotalAggregateControlArgTests
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
    public void Aggregate_BareCellRef_FunctionNumAndOptions_DoNotBreakToNumberCoercion()
    {
        // B1=9 (SUM function_num), C1=0 (options: keep hidden), A5=42 is a single data cell.
        // All three arguments are bare cell references -- none are literals. Before the fix,
        // wrapping B1/C1 into 1x1 RangeValues broke ToNumber(func_num)/ToNumber(options) and
        // produced #VALUE!; the correct Excel result is 42.
        var sheet = MakeSheet(
            (1, 2, new NumberValue(9)),  // B1
            (1, 3, new NumberValue(0)),  // C1
            (5, 1, new NumberValue(42))); // A5

        _eval.Evaluate("=AGGREGATE(B1,C1,A5)", sheet).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Aggregate_BareSingleCellRef_HiddenRow_IgnoreHiddenOption_StillExcludesIt()
    {
        // Sibling/no-regression: the r57 hidden-row exclusion behavior for the DATA argument must
        // remain intact -- AGGREGATE(9,1,A5) with row 5 hidden must still exclude it (yielding 0),
        // proving the argIndex-aware guard didn't regress the r57 fix for the data argument itself.
        var sheet = MakeSheet((5, 1, new NumberValue(42)));
        sheet.GroupHiddenRows.Add(5);

        _eval.Evaluate("=AGGREGATE(9,1,A5)", sheet).Should().Be(new NumberValue(0));
    }
}
