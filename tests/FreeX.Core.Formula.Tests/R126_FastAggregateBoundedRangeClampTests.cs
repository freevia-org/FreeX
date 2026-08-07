using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-126 regression tests for the HIGH-severity defect at
/// FormulaEvaluator.FastAggregates.cs:580 (TryResolveFastAggregateRange).
///
/// SUM/AVERAGE/COUNT/MAX/MIN/STDEV/VAR only clamped a fast-aggregate range argument to the
/// sheet's used range when the argument was syntactically a full-column (A:C) / full-row (1:5)
/// range, or when the function was COUNTBLANK. An ordinary explicit BOUNDED 2-D range like
/// A1:J200000 (10 cols x 200,000 rows = 2,000,000 cells) is not full-column/full-row shaped, so
/// it skipped the clamp entirely and its raw nominal cell count (2,000,000) was checked directly
/// against FormulaSafetyLimits.MaxStreamingRangeCells, which was sized for exactly one full
/// column (1,048,576) -- not a 2-D area. That made =SUM(A1:J200000) deterministically return
/// #REF!, independent of how much data the range actually contained, even though both extents
/// (200,000 rows, 10 columns) are individually far inside Excel's real 1,048,576-row x
/// 16,384-column sheet limits.
///
/// The fix has two parts, both exercised below:
///  1) Generalize the used-range clamp (already applied for COUNTBLANK) to apply for ANY range
///     shape across all three fast-aggregate resolution branches: literal RangeRefNode,
///     INDIRECT, and NamedRangeNode. This alone fixes the common case where the queried range is
///     much larger than the sheet's actual populated extent.
///  2) Raise MaxStreamingRangeCells from one full column's cell count (1,048,576) to Excel's real
///     2-D sheet capacity (CellAddress.MaxRow * CellAddress.MaxCol), since the streaming
///     accumulators used here never materialize the range -- the cap only needs to bound
///     iteration time, and the used-range clamp from (1) already bounds it to the sheet's real
///     populated extent in the overwhelming common case. This is needed for the sparse-but-wide
///     case where the used-range-clamped BOUNDING BOX (not the populated cell count) still
///     exceeds the old cap.
/// </summary>
public sealed class R126_FastAggregateBoundedRangeClampTests
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
    public void Sum_ExplicitTwoMillionCellBoundedRange_ComputesInsteadOfRefError()
    {
        // The exact headline repro from the finding: A1:J200000 = 10 cols x 200,000 rows =
        // 2,000,000 nominal cells, well over the old MaxStreamingRangeCells (1,048,576), but the
        // sheet only has one populated cell, so the used-range clamp should shrink the scanned
        // rectangle down to essentially nothing.
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(42)));

        var result = _eval.Evaluate("=SUM(A1:J200000)", sheet, wb);

        result.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Average_ExplicitTwoMillionCellBoundedRange_ComputesInsteadOfRefError()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (2, 1, new NumberValue(20)));

        var result = _eval.Evaluate("=AVERAGE(A1:J200000)", sheet, wb);

        result.Should().Be(new NumberValue(15));
    }

    [Fact]
    public void Count_ExplicitTwoMillionCellBoundedRange_ComputesInsteadOfRefError()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)),
            (5, 3, new NumberValue(20)),
            (100, 9, new TextValue("not a number")));

        var result = _eval.Evaluate("=COUNT(A1:J200000)", sheet, wb);

        result.Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Max_ExplicitTwoMillionCellBoundedRange_ComputesInsteadOfRefError()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(3)),
            (50_000, 5, new NumberValue(99)));

        var result = _eval.Evaluate("=MAX(A1:J200000)", sheet, wb);

        result.Should().Be(new NumberValue(99));
    }

    [Fact]
    public void Min_ExplicitTwoMillionCellBoundedRange_ComputesInsteadOfRefError()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(3)),
            (50_000, 5, new NumberValue(-7)));

        var result = _eval.Evaluate("=MIN(A1:J200000)", sheet, wb);

        result.Should().Be(new NumberValue(-7));
    }

    [Fact]
    public void Sum_ExplicitTwoMillionCellBoundedRange_EmptySheet_ReturnsZeroInsteadOfRefError()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");

        var result = _eval.Evaluate("=SUM(A1:J200000)", sheet, wb);

        result.Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Sum_SparseRangeSpanningTwoFullHeightColumns_RequiresRaisedStreamingCap_ComputesInsteadOfRefError()
    {
        // This specifically exercises fix part (2), the raised MaxStreamingRangeCells. Two
        // populated cells at (row 1, col A) and (row CellAddress.MaxRow, col B) give the sheet's
        // used range a bounding box spanning both full-height columns: 2 cols x 1,048,576 rows =
        // 2,097,152 cells. The used-range clamp (fix part 1) can only shrink the queried
        // rectangle down to that BOUNDING BOX -- it cannot skip the sparse interior -- so the
        // clamped rectangle is still 2,097,152 cells, over the OLD 1,048,576 cap (which was sized
        // for exactly one full column). Only raising the cap (since the streaming accumulator
        // never materializes memory) lets this compute instead of spuriously erroring. The
        // formula uses explicit row numbers (A1:B1048576), not the "A:B" full-column syntax, so
        // it exercises the ordinary-bounded-range path, not the pre-existing full-column clamp.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(4)),
            (CellAddress.MaxRow, 2, new NumberValue(6)));

        var result = _eval.Evaluate($"=SUM(A1:B{CellAddress.MaxRow})", sheet, wb);

        result.Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Sum_IndirectBoundedRangeOverOneMillionCells_ComputesInsteadOfRefError()
    {
        // Sibling fast-aggregate resolution branch: INDIRECT() text ranges go through a separate
        // code path in TryResolveFastAggregateRange from literal RangeRefNode arguments, and had
        // the identical full-column/full-row-only clamp gate.
        var (wb, sheet) = MakeWb((1, 1, new NumberValue(7)));

        var result = _eval.Evaluate("=SUM(INDIRECT(\"A1:B600000\"))", sheet, wb);

        result.Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Sum_NamedRangeBoundedRangeOverOneMillionCells_ComputesInsteadOfRefError()
    {
        // Third sibling fast-aggregate resolution branch: an ordinary bounded named range (not
        // full-column/full-row) had the identical gate.
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(9));
        wb.DefineNamedRange("BigData", new GridRange(
            a1,
            new CellAddress(sheet.Id, 600_000, 2)));

        var result = _eval.Evaluate("=SUM(BigData)", sheet, wb);

        result.Should().Be(new NumberValue(9));
    }

    [Fact]
    public void CountBlank_BoundedRangeOverOneMillionCells_StillComputesCorrectly_NoRegression()
    {
        // No-regression sibling: COUNTBLANK already had its own always-clamp behavior (Round 25)
        // via the `kind == FastAggregateKind.CountBlank` condition that this fix generalizes to
        // every fast-aggregate kind. Removing that now-redundant special case in
        // TryResolveFastAggregateRange must not disturb COUNTBLANK's own NominalCellCount
        // bookkeeping, which still counts blanks across the whole nominal range rather than only
        // the used-range-clamped scanned rectangle.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(5)),
            (3, 2, new TextValue("x")));

        var result = _eval.Evaluate("=COUNTBLANK(A1:B600000)", sheet, wb);

        // 1,200,000 nominal cells, 2 of them non-blank.
        result.Should().Be(new NumberValue(1_200_000L - 2));
    }

    [Fact]
    public void Sum_SmallOrdinaryBoundedRange_StillComputesExactly_NoRegression()
    {
        // No-regression sibling: an ordinary small bounded range (nowhere near the streaming
        // cap) must still sum correctly through the now-unconditional clamp path.
        var (wb, sheet) = MakeWb(
            (2, 2, new NumberValue(3)),
            (4, 3, new NumberValue(4)));

        var result = _eval.Evaluate("=SUM(B2:D6)", sheet, wb);

        result.Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Sum_FullColumnRange_StillComputesInsteadOfRefError_NoRegression()
    {
        // No-regression sibling: the original full-column clamp path (Round 22/23) must still
        // work identically now that the clamp condition applies unconditionally rather than via
        // an explicit `argument is FullColumnRangeRefNode` check.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(2)),
            (CellAddress.MaxRow, 1, new NumberValue(4)));

        var result = _eval.Evaluate("=SUM(A:A)", sheet, wb);

        result.Should().Be(new NumberValue(6));
    }
}
