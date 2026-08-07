using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-127 regression tests for the MED-severity defect at
/// FormulaEvaluator.SelectionFastPaths.cs:194 (CreateDirectSelectionBuffer) -- unlike
/// SUM/AVERAGE/COUNT (FormulaEvaluator.FastAggregates.cs) and SUMIF/COUNTIF/SUMIFS/COUNTIFS
/// (FormulaEvaluator.ConditionalAggregateFastPaths.cs), which read every cell one at a time with
/// no buffer at all, LARGE/SMALL/PERCENTILE(.INC/.EXC)/QUARTILE(.INC/.EXC)'s direct-range fast
/// path sized its ArrayPool&lt;double&gt; rent to the full NOMINAL referenced rectangle (e.g.
/// =LARGE(A1:J1000000,1) rents an 80MB double[10_000_000]) even when the sheet's actual populated
/// extent is tiny and comfortably under FormulaSafetyLimits.MaxMaterializedRangeCells.
///
/// The fix sizes the buffer's INITIAL capacity from the sheet's used-range intersection with the
/// requested rectangle instead of the raw nominal rowCount*colCount, relying on the
/// already-existing DirectSelectionBuffer.Grow() doubling logic to make up any shortfall -- so
/// CollectDirectRangeNumbers/CollectDirectAggregateNumbers still scan exactly the same cells and
/// produce exactly the same result; only the up-front allocation shrinks.
///
/// NOTE ON SCOPE: the same finding also named SUMPRODUCT/MMULT (routed through
/// FormulaEvaluator.References.cs BuildRangeValue's `new ScalarValue[rows, cols]`) as a second,
/// structurally different call site. That path was deliberately NOT changed here: BuildRangeValue
/// is shared by INDEX/VLOOKUP/OFFSET-adjacent positional consumers (which index by absolute
/// row/col offset into the full nominal rectangle) and by SUMPRODUCT/MMULT (which require their
/// range arguments' dimensions to already match/compose). Intersecting a range independently with
/// the sheet's used range is only safe when the consumer treats the range as an unordered,
/// shape-agnostic bag of numbers -- exactly the family LARGE/SMALL/PERCENTILE/QUARTILE/MEDIAN/
/// AGGREGATE's direct-range mode belong to (see the "shape-agnostic" comment block in
/// FormulaEvaluator.FunctionClassification.cs). For SUMPRODUCT, two same-length ranges with
/// DIFFERENT start offsets (e.g. SUMPRODUCT(A1:A1000000,B2:B1000001)) would clamp to DIFFERENT
/// row counts if each were independently intersected with the sheet's used range, wrongly turning
/// a legitimately-computable formula into #VALUE!. MMULT's inner-dimension-matching requirement
/// carries the same risk. See the fix's PR notes / task report for the full reasoning; left open
/// as a sibling lead rather than forced through here.
/// </summary>
public sealed class R127_DirectSelectionBufferSparseAllocationTests
{
    private static (Workbook Workbook, Sheet Sheet) MakeSparseWb()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        // Only 5 populated numeric cells -- the sheet's used range is tiny (A1:A5) compared to the
        // formula's stated 10-column x 1,000,000-row rectangle below.
        for (uint r = 1; r <= 5; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
        return (wb, sheet);
    }

    [Fact]
    public void Large_MostlyEmptyExplicitBoundedRange_DoesNotEagerlyAllocateFullNominalBuffer()
    {
        var (wb, sheet) = MakeSparseWb();
        var eval = new FormulaEvaluator();

        // Warm up the JIT / parser / regex-cache paths with an unrelated small evaluation first so
        // the measured delta below isn't polluted by one-time JIT/parse-cache allocations that have
        // nothing to do with the direct-selection buffer itself.
        eval.Evaluate("=LARGE(A1:A5,1)", sheet, wb);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = eval.Evaluate("=LARGE(A1:J1000000,1)", sheet, wb);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        result.Should().Be(new NumberValue(5));

        // Before the fix: CreateDirectSelectionBuffer computed cellCount from the raw 10 x
        // 1,000,000 rectangle (10,000,000 cells) and rented a double[10_000_000] up front --
        // ~80,000,000 bytes -- regardless of the 5 actually-populated cells. After the fix, the
        // initial rent is sized from the used-range intersection (5 cells), so total allocation for
        // the whole evaluation should be a tiny fraction of the old eager rent. 4,000,000 bytes
        // (5% of the old 80MB rent) is a generous ceiling that still fails against the un-fixed
        // behavior and passes comfortably against the fixed one.
        allocated.Should().BeLessThan(4_000_000,
            "the direct-selection buffer must not eagerly rent a buffer sized to the full nominal 10,000,000-cell range");
    }

    [Fact]
    public void Large_DenselyPopulatedRangeWithinUsedExtent_StillComputesCorrectResult_NoRegression()
    {
        // No-regression sibling: when the sheet's used range does NOT shrink the scanned rectangle
        // (data genuinely fills it), the buffer's Grow() doubling logic must still land on the
        // exact correct k-th-largest value -- proving the smaller initial estimate never drops a
        // populated cell.
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        for (uint r = 1; r <= 200; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));

        var eval = new FormulaEvaluator();
        var result = eval.Evaluate("=LARGE(A1:A200,3)", sheet, wb);

        // 3rd largest of 1..200 is 198.
        result.Should().Be(new NumberValue(198));
    }

    [Fact]
    public void Small_MostlyEmptyExplicitBoundedRange_StillComputesCorrectResult()
    {
        var (wb, sheet) = MakeSparseWb();
        var eval = new FormulaEvaluator();

        var result = eval.Evaluate("=SMALL(A1:J1000000,2)", sheet, wb);

        result.Should().Be(new NumberValue(2));
    }

    [Fact]
    public void PercentileInc_MostlyEmptyExplicitBoundedRange_StillComputesCorrectResult()
    {
        var (wb, sheet) = MakeSparseWb();
        var eval = new FormulaEvaluator();

        // PERCENTILE.INC over {1,2,3,4,5} at 0.5 is the median, 3.
        var result = eval.Evaluate("=PERCENTILE.INC(A1:J1000000,0.5)", sheet, wb);

        result.Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Aggregate_LargeFunctionOverMostlyEmptyExplicitBoundedRange_StillComputesCorrectResult()
    {
        // Exercises the OTHER CreateDirectSelectionBuffer call site (the multi-range
        // TryEvaluateAggregateSelectionDirectRanges path used by AGGREGATE's function_num 14 =
        // LARGE), not just the single-range TryEvaluateStatisticalSelectionDirectRange path.
        var (wb, sheet) = MakeSparseWb();
        var eval = new FormulaEvaluator();

        var result = eval.Evaluate("=AGGREGATE(14,6,A1:J1000000,1)", sheet, wb);

        result.Should().Be(new NumberValue(5));
    }
}
