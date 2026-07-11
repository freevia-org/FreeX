using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for Round 23 findings R23-large-scale-correctness-1/2:
/// FormulaEvaluator.FastAggregates.cs's TryAcceptFastAggregateRange applied the wrong
/// safety cap to two fast-aggregate kinds, producing spurious #REF! errors on ranges that
/// the underlying implementation handles fine.
///
/// 1) COUNTBLANK is deliberately excluded from the used-range clamp (it must count blanks
///    across the WHOLE nominal full-column/full-row range), but the cap check still
///    measured that un-clamped nominal cell count against MaxStreamingRangeCells
///    (1,048,576) — so COUNTBLANK(A:B) (2 full columns = 2,097,152 nominal cells) wrongly
///    returned #REF! even though its implementation is a plain streaming loop.
///
/// 2) STDEV/STDEVP/VAR/VARP ("Stdev"/"Var" kinds) were capped at MaxMaterializedRangeCells
///    (1,000,000) even though EvaluateFastRangeOnlyVariance is a pure streaming Welford
///    accumulator (no materialization) — so STDEV(A:A) over a full used column
///    (1,048,576 cells, same size SUM/AVERAGE happily accept) wrongly returned #REF!.
/// </summary>
public sealed class R23_FastAggregateSafetyCapTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook Workbook, Sheet Sheet) MakeWb(params (uint row, uint col, ScalarValue val)[] cells)
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, r, c), v);
        return (wb, sheet);
    }

    [Fact]
    public void CountBlank_TwoFullColumns_ComputesInsteadOfRefError()
    {
        // COUNTBLANK(A:B) nominally spans 2 * 1,048,576 = 2,097,152 cells — over the
        // streaming cap that TryAcceptFastAggregateRange wrongly applied to CountBlank
        // before the fix (it must stay un-clamped to count the whole nominal range).
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(5)),
            (2, 2, new NumberValue(10)));

        var result = _eval.Evaluate("=COUNTBLANK(A:B)", sheet, wb);

        result.Should().Be(new NumberValue(2 * CellAddress.MaxRow - 2));
    }

    [Fact]
    public void Stdev_FullUsedColumn_ComputesInsteadOfRefError()
    {
        // Populate row 1 and the very last row so the sheet's used range for column A spans
        // the full 1,048,576 rows (same nominal size as a single full column SUM/AVERAGE
        // already accept via MaxStreamingRangeCells), with just the two values needed for a
        // simple sample-stdev check.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(2)),
            (CellAddress.MaxRow, 1, new NumberValue(4)));

        var result = _eval.Evaluate("=STDEV(A:A)", sheet, wb);

        result.Should().Be(new NumberValue(Math.Sqrt(2)));
    }

    [Fact]
    public void Var_FullUsedColumn_ComputesInsteadOfRefError()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(2)),
            (CellAddress.MaxRow, 1, new NumberValue(4)));

        var result = _eval.Evaluate("=VAR(A:A)", sheet, wb);

        result.Should().Be(new NumberValue(2));
    }
}
