using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for R20-lookup-reference-functions-1.
///
/// The XLOOKUP direct-range fast path (FormulaEvaluator.LookupFastPaths.cs,
/// TryEvaluateXlookupDirectRanges) used to accept a return array whose
/// orientation mismatched the lookup array as long as the total element
/// COUNT matched (e.g. a 5-row/1-col lookup array paired with a 1-row/5-col
/// return array both have 5 elements), silently returning a wrong value
/// instead of #VALUE!. Real Excel -- and FreeX's own slow path in
/// BuiltInFunctions.Lookup.Modern.cs -- requires the return array's row
/// count to match the lookup array's row count for a vertical lookup array
/// (and column count for a horizontal one), not merely the same element
/// count.
/// </summary>
public class R20_lookup_xlookup_Tests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Xlookup_VerticalLookupWithHorizontalReturnOfSameCount_ReturnsValueError()
    {
        // A1:A5 = {1,2,3,4,5} (vertical, 5 rows x 1 col).
        // B1:F1 = {10,20,30,40,50} (horizontal, 1 row x 5 cols) -- same element
        // count (5) as the lookup array, but a mismatched orientation.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)),
            (4, 1, new NumberValue(4)), (5, 1, new NumberValue(5)),
            (1, 2, new NumberValue(10)), (1, 3, new NumberValue(20)), (1, 4, new NumberValue(30)),
            (1, 5, new NumberValue(40)), (1, 6, new NumberValue(50)));

        // Excel (and FreeX's slow path) return #VALUE! here -- NOT the value 30
        // that a naive same-total-count fast path would silently return.
        _eval.Evaluate("=XLOOKUP(3,A1:A5,B1:F1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Xlookup_HorizontalLookupWithVerticalReturnOfSameCount_ReturnsValueError()
    {
        // A1:E1 = {1,2,3,4,5} (horizontal, 1 row x 5 cols).
        // B1:B5 = {10,20,30,40,50} (vertical, 5 rows x 1 col) -- same element
        // count, mismatched orientation the other way around.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (1, 4, new NumberValue(4)), (1, 5, new NumberValue(5)),
            (1, 6, new NumberValue(10)), (2, 6, new NumberValue(20)), (3, 6, new NumberValue(30)),
            (4, 6, new NumberValue(40)), (5, 6, new NumberValue(50)));

        // F1:F5 (vertical, 5 rows x 1 col) has the same element count (5) as the
        // horizontal A1:E1 lookup array, but a mismatched orientation.
        _eval.Evaluate("=XLOOKUP(3,A1:E1,F1:F5)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Xlookup_VerticalLookupWithAlignedVerticalReturn_StillReturnsCorrectValue()
    {
        // Aligned case (same orientation, same count) must keep working exactly
        // as before -- this is the common real-world XLOOKUP shape.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)),
            (4, 1, new NumberValue(4)), (5, 1, new NumberValue(5)),
            (1, 2, new NumberValue(10)), (2, 2, new NumberValue(20)), (3, 2, new NumberValue(30)),
            (4, 2, new NumberValue(40)), (5, 2, new NumberValue(50)));

        _eval.Evaluate("=XLOOKUP(3,A1:A5,B1:B5)", sheet).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void Xlookup_HorizontalLookupWithAlignedHorizontalReturn_StillReturnsCorrectValue()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new NumberValue(2)), (1, 3, new NumberValue(3)),
            (1, 4, new NumberValue(4)), (1, 5, new NumberValue(5)),
            (2, 1, new NumberValue(10)), (2, 2, new NumberValue(20)), (2, 3, new NumberValue(30)),
            (2, 4, new NumberValue(40)), (2, 5, new NumberValue(50)));

        _eval.Evaluate("=XLOOKUP(3,A1:E1,A2:E2)", sheet).Should().Be(new NumberValue(30));
    }

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), val);
        return sheet;
    }
}
