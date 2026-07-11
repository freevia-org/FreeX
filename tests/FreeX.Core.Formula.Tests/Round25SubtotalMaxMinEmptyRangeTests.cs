using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R25-aggregate-subtotal-deep-1: SUBTOTAL(4,...)/MAX and SUBTOTAL(5,...)/MIN used to return
/// #DIV/0! when the referenced range had no numeric values (e.g. all text), disagreeing with
/// real Excel and with this codebase's own plain MAX()/MIN() functions, both of which return 0
/// for the identical input. Only AVERAGE/STDEV/VAR (funcNum 1,7,8,10,11) should error on an empty
/// numeric sample. Fixed in BuiltInFunctions.Subtotal.cs by special-casing 4/5 to return 0 on an
/// empty numeric accumulator, matching the existing special-case for PRODUCT (funcNum 6).
///
/// A plain direct-range argument (e.g. "=SUBTOTAL(4,A1:A3)") is intercepted by the streaming fast
/// path in FormulaEvaluator.SubtotalAggregateFastPaths.cs (TryEvaluateSubtotalDirectRanges /
/// EvaluateSubtotalAggregateNumericResult), which has its own separate copy of this same defect
/// and is outside this file's scope. To specifically exercise the BuiltInFunctions.Subtotal.cs
/// code path fixed here, these tests wrap the range in FILTER(...,...>0) with an all-true mask
/// (matching the R19_SubtotalComputedArrayTests.cs technique) so the argument is a computed array
/// that the fast path bails out on (TryCreateDirectRangeArgument returns Unsupported for a
/// FunctionCallNode), forcing evaluation through the general slow path.
/// </summary>
public sealed class Round25SubtotalMaxMinEmptyRangeTests
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
    public void Subtotal_Max_AllTextRange_ReturnsZero_NotDivByZero()
    {
        // Real Excel: MAX over all-text cells returns 0. A1:A3 = "a","b","c"; B1:B3 all 1 so
        // FILTER keeps every row unchanged, forcing the slow (computed-array) evaluation path.
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")), (1, 2, new NumberValue(1)),
            (2, 1, new TextValue("b")), (2, 2, new NumberValue(1)),
            (3, 1, new TextValue("c")), (3, 2, new NumberValue(1)));

        _eval.Evaluate("=SUBTOTAL(4,FILTER(A1:A3,B1:B3>0))", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Subtotal_Min_AllTextRange_ReturnsZero_NotDivByZero()
    {
        // Sibling of the MAX case: real Excel's MIN over all-text also returns 0.
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")), (1, 2, new NumberValue(1)),
            (2, 1, new TextValue("b")), (2, 2, new NumberValue(1)),
            (3, 1, new TextValue("c")), (3, 2, new NumberValue(1)));

        _eval.Evaluate("=SUBTOTAL(5,FILTER(A1:A3,B1:B3>0))", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Subtotal_Max_NormalNumericRange_StillReturnsCorrectMax_NoRegression()
    {
        // Already-working case that must keep working: a non-empty numeric sample still returns
        // the real max (the numeric.Count == 0 branch must not fire when there IS data).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)), (1, 2, new NumberValue(1)),
            (2, 1, new NumberValue(30)), (2, 2, new NumberValue(1)),
            (3, 1, new NumberValue(20)), (3, 2, new NumberValue(1)));

        _eval.Evaluate("=SUBTOTAL(4,FILTER(A1:A3,B1:B3>0))", sheet).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Subtotal_Max_NormalNumericRangeWithHiddenRow_StillExcludesHidden_NoRegression()
    {
        // Already-working case via the direct-range path: SUBTOTAL(104,...) over real numbers
        // with a hidden row still ignores the hidden value and returns the max of the visible ones.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(999)),
            (3, 1, new NumberValue(30)));
        sheet.GroupHiddenRows.Add(2);

        _eval.Evaluate("=SUBTOTAL(104,A1:A3)", sheet).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Subtotal_Average_AllTextRange_StillReturnsDivByZero_NoRegression()
    {
        // AVERAGE (funcNum 1) must still error on an empty numeric sample -- only MAX/MIN changed.
        var sheet = MakeSheet(
            (1, 1, new TextValue("a")), (1, 2, new NumberValue(1)),
            (2, 1, new TextValue("b")), (2, 2, new NumberValue(1)),
            (3, 1, new TextValue("c")), (3, 2, new NumberValue(1)));

        _eval.Evaluate("=SUBTOTAL(1,FILTER(A1:A3,B1:B3>0))", sheet).Should().Be(ErrorValue.DivByZero);
    }
}
