using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-127B follow-up to R127_DirectSelectionBufferSparseAllocationTests: the r127 fix sized
/// CreateDirectSelectionBuffer's ArrayPool&lt;double&gt; rent from the sheet's used-range
/// intersection instead of the full nominal referenced rectangle, but the AGGREGATE(13,...) /
/// MODE.SNGL direct-range branch (FormulaEvaluator.SelectionFastPaths.cs, functionNumber == 13 in
/// TryEvaluateAggregateSelectionDirectRanges) is a sibling that does NOT go through
/// CreateDirectSelectionBuffer at all -- it routes through EvaluateAggregateModeDirectRanges /
/// CreateDirectRangeModeBuffer, a structurally different pooled open-addressing table (it needs
/// value-&gt;frequency counting for MODE, not just a flat bag of numbers), which still sized its
/// THREE ArrayPool rents (_keys: double[], _counts: int[], _firstOrdinals: int[]) from the raw
/// nominal rowCount*colCount with no used-range intersection at all.
///
/// The fix mirrors the r127 pattern into CreateDirectRangeModeBuffer: its initial capacity is now
/// sized from EstimateDirectSelectionPopulatedCellCount (the sheet's populated extent intersected
/// with the requested rectangle) instead of the raw nominal cell count. The existing Grow()
/// doubling logic in DirectRangeModeBuffer.Add() (already exercised whenever the context isn't a
/// SheetEvalContext, or the estimate undershoots) makes up any shortfall, so a sparse estimate
/// never drops a legitimately populated cell -- only the up-front allocation shrinks.
/// </summary>
public sealed class R127B_DirectRangeModeBufferSparseAllocationTests
{
    private static (Workbook Workbook, Sheet Sheet) MakeSparseModeWb()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        // Only 6 populated numeric cells -- the sheet's used range is tiny (A1:A6) compared to the
        // formula's stated 10-column x 1,000,000-row rectangle below. Value 7 repeats so MODE.SNGL
        // has a well-defined answer.
        var values = new double[] { 7, 3, 7, 1, 9, 2 };
        for (uint r = 1; r <= values.Length; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(values[r - 1]));
        return (wb, sheet);
    }

    [Fact]
    public void Aggregate13_MostlyEmptyExplicitBoundedRange_DoesNotEagerlyAllocateFullNominalModeTable()
    {
        var (wb, sheet) = MakeSparseModeWb();
        var eval = new FormulaEvaluator();

        // Warm up JIT / parser / regex-cache paths first so the measured delta below isn't
        // polluted by one-time allocations unrelated to the mode buffer itself.
        eval.Evaluate("=AGGREGATE(13,0,A1:A6)", sheet, wb);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = eval.Evaluate("=AGGREGATE(13,0,A1:J1000000)", sheet, wb);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        result.Should().Be(new NumberValue(7));

        // Before the fix: CreateDirectRangeModeBuffer computed cellCount from the raw 10 x
        // 1,000,000 rectangle (10,000,000 cells) and rented a table sized via
        // GetDirectRangeModeTableCapacity(10_000_000) (~2x, rounded to a power of two -- roughly
        // 16,777,216 slots) across THREE arrays (double[], int[], int[]) -- well over 200MB --
        // regardless of the 6 actually-populated cells. After the fix, the initial rent is sized
        // from the used-range intersection (6 cells), so total allocation for the whole evaluation
        // should be a tiny fraction of that. 4,000,000 bytes is a generous ceiling that still fails
        // against the un-fixed behavior and passes comfortably against the fixed one.
        allocated.Should().BeLessThan(4_000_000,
            "the AGGREGATE(13,...) / MODE.SNGL direct-range mode table must not eagerly rent " +
            "ArrayPool buffers sized to the full nominal 10,000,000-cell range");
    }

    [Fact]
    public void Aggregate13_DenselyPopulatedRangeWithinUsedExtent_StillComputesCorrectResult_NoRegression()
    {
        // No-regression sibling: when the sheet's used range does NOT shrink the scanned
        // rectangle (data genuinely fills it), the mode table's Grow() doubling logic must still
        // land on the exact correct mode -- proving the smaller initial estimate never drops a
        // populated cell. Values 1..200 with 42 repeated three times so it has a well-defined mode.
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        for (uint r = 1; r <= 200; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
        sheet.SetCell(new CellAddress(sheet.Id, 201, 1), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 202, 1), new NumberValue(42));

        var eval = new FormulaEvaluator();
        var result = eval.Evaluate("=AGGREGATE(13,0,A1:A202)", sheet, wb);

        result.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Aggregate13_MultiRangeArguments_ActiveAreaDiffersFromOtherAreas_StillFindsModeAcrossAllRanges()
    {
        // Guards against the "early gate silently no-ops the whole operation" failure mode called
        // out for this round: the two range arguments have DIFFERENT populated extents (A1:A3 vs
        // a far-away, sparsely-populated F1:F1000000), so a naive per-range estimate must not
        // cause the combined table to drop cells from the range whose used-range intersection is
        // smaller/empty-looking relative to the other. Mode 5 is only reachable by combining both
        // ranges (5 appears once in each).
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
        // Column F (col 6), far down the sheet -- a different "active area" than column A.
        sheet.SetCell(new CellAddress(sheet.Id, 500_000, 6), new NumberValue(5));

        var eval = new FormulaEvaluator();
        var result = eval.Evaluate("=AGGREGATE(13,0,A1:A3,F1:F1000000)", sheet, wb);

        result.Should().Be(new NumberValue(5));
    }
}
