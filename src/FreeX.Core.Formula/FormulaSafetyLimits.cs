using FreeX.Core.Model;

namespace FreeX.Core.Formula;

internal static class FormulaSafetyLimits
{
    public const int MaxParseTokens = 16_384;
    public const int MaxParseDepth = 512;
    public const int MaxParseNesting = 256;

    // Bounds every call site that actually allocates an O(cells) in-memory structure for a
    // materialized range: BuildRangeValue's `new ScalarValue[rows, cols]` (INDEX's slow path,
    // VLOOKUP/HLOOKUP/MATCH/XLOOKUP fallback paths, MMULT, structured-reference functions,
    // ISREF's 2-D path), OFFSET's own `new ScalarValue[rowSpan, colSpan]`, ISFORMULA/FORMULATEXT's
    // multi-cell path, INDIRECT's array materializer, and the LARGE/SMALL/PERCENTILE-family
    // selection buffer. It previously sat at exactly 1,000,000 -- just UNDER a single full
    // worksheet column's real height (CellAddress.MaxRow = 1,048,576) -- which made even the
    // textbook `=OFFSET($A$1,0,0,ROWS($A:$A),1)` "whole column" idiom, or an ordinary explicit
    // bounded range like A1:C500000 (3 cols x 500,000 rows = 1,500,000 cells, still far inside
    // Excel's real 1,048,576-row sheet), deterministically return #REF! regardless of how much
    // data the range actually held -- even though both are trivially valid in real Excel. Raise it
    // to comfortably cover realistic explicit multi-column ranges up to a full column's height
    // (including that exact idiom) while still bounding worst-case memory: at ~8 bytes per cell
    // reference on 64-bit, 16,777,216 cells (16 full worksheet columns' worth of rows) is a ~134MB
    // worst-case allocation, versus an unbounded cap risking a multi-gigabyte-to-terabyte
    // allocation (and an OutOfMemoryException taking the whole recalculation down with it) for a
    // pathological explicit whole-sheet-scale reference like A1:XFD1048576 (~17.2 billion cells).
    public const long MaxMaterializedRangeCells = 16_777_216L;

    // The fast-aggregate streaming path (SUM/AVERAGE/COUNT/MAX/MIN/STDEV/VAR/COUNTBLANK; see
    // FormulaEvaluator.FastAggregates.cs) never materializes the range -- it's a plain
    // running-sum/Welford accumulator over cells fetched one at a time -- so the cap here only
    // needs to bound wall-clock iteration time, not memory. It previously equaled exactly one
    // full column's cell count (CellAddress.MaxRow), which made sense for a 1-D full-column
    // range but wrongly rejected perfectly ordinary explicit 2-D ranges (e.g. A1:J200000, 10
    // cols x 200,000 rows = 2,000,000 cells) that are still far inside Excel's real sheet
    // capacity (CellAddress.MaxRow rows x CellAddress.MaxCol cols) with #REF!, independent of
    // how much data the range actually contains. Size the cap to that real 2-D sheet capacity
    // instead: every fast-aggregate range argument is also clamped to the sheet's used range
    // first (TryResolveFastAggregateRange), so in practice this ceiling is only ever reached by
    // a range whose ACTUAL populated extent is that large -- and Excel has no synthetic
    // 1-column-sized limit on top of its real per-axis row/column limits.
    public const long MaxStreamingRangeCells = (long)CellAddress.MaxRow * CellAddress.MaxCol;
    public const int MaxRegexCacheEntries = 1_024;
    public const int MaxParsedFormulaCacheEntries = 1_024;
    public const int MaxTokenizedFormulaCacheEntries = 1_024;
    public const int MaxParsedTokenFormulaCacheEntries = 1_024;

    public static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static long GetRangeCellCount(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var rows = Math.Abs((long)endRow - startRow) + 1;
        var cols = Math.Abs((long)endCol - startCol) + 1;
        return rows * cols;
    }
}

internal sealed record RangeMaterializationErrorValue(ErrorValue Error) : ScalarValue;
