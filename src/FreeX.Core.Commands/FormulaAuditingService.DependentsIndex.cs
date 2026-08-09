using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class FormulaAuditingService
{
    /// <summary>
    /// Build once, query many. <see cref="GetDirectDependents(Workbook, GridRange)"/> re-scans and
    /// conditionally re-parses every formula cell in the workbook on every call -- the right cost
    /// for a single "Direct Only" lookup, but O(chain-length * total-formula-count) full re-parses
    /// when a caller drives it once per BFS level (Go To Special "All Levels" -- see
    /// <c>GoToSpecialService.FindDependents</c> -- and the ribbon/keyboard "select all levels"
    /// dependents trace -- see <see cref="CollectDependentTraceArrows"/>). This index parses every
    /// formula cell's precedent regions exactly ONCE up front (reusing
    /// <see cref="ExtractPrecedentRegions"/>, the same region extraction the trace-arrow builders
    /// already use for precedents), then buckets them by sheet + row/column -- mirroring
    /// FreeX.Core.Calc.DependencyGraph's internal RangeDependencyIndex -- so a later "who depends
    /// on this cell/range" query only scans the buckets that can plausibly overlap the query,
    /// never the whole workbook (R123-core-commands-formula-auditing-all-levels-perf).
    /// </summary>
    internal sealed class FormulaDependentsIndex
    {
        private const uint RowBucketSize = 256;
        private const uint ColumnBucketSize = 16;

        // Each precedent region is bucketed under whichever dimension (row or column) yields
        // fewer buckets -- a full-column reference (huge row span, tiny column span) lands in the
        // column buckets instead of exploding into thousands of row buckets, and vice versa for a
        // full-row reference. Mirrors DependencyGraph.RangeDependencyIndex's UseRowIndex.
        private readonly Dictionary<SheetId, Dictionary<uint, List<(GridRange Region, CellAddress Dependent)>>> _rowBuckets = [];
        private readonly Dictionary<SheetId, Dictionary<uint, List<(GridRange Region, CellAddress Dependent)>>> _columnBuckets = [];

        private FormulaDependentsIndex()
        {
        }

        internal static FormulaDependentsIndex Build(Workbook workbook)
        {
            var index = new FormulaDependentsIndex();

            foreach (var sheet in workbook.Sheets)
            {
                foreach (var formulaAddress in sheet.EnumerateFormulaCells())
                {
                    var cell = sheet.GetCell(formulaAddress);
                    if (cell?.HasFormula != true || string.IsNullOrWhiteSpace(cell.FormulaText))
                        continue;

                    foreach (var region in ExtractPrecedentRegions(workbook, sheet.Id, cell.FormulaText))
                        index.Add(region, formulaAddress);
                }
            }

            return index;
        }

        private void Add(GridRange region, CellAddress dependent)
        {
            var useRowIndex = UseRowIndex(region);
            var buckets = useRowIndex ? _rowBuckets : _columnBuckets;
            var bucketSize = useRowIndex ? RowBucketSize : ColumnBucketSize;
            var start = useRowIndex ? region.Start.Row : region.Start.Col;
            var end = useRowIndex ? region.End.Row : region.End.Col;

            if (!buckets.TryGetValue(region.Start.Sheet, out var sheetBuckets))
            {
                sheetBuckets = [];
                buckets[region.Start.Sheet] = sheetBuckets;
            }

            var startBucket = GetBucket(start, bucketSize);
            var endBucket = GetBucket(end, bucketSize);
            for (var bucket = startBucket; bucket <= endBucket; bucket++)
            {
                if (!sheetBuckets.TryGetValue(bucket, out var list))
                {
                    list = [];
                    sheetBuckets[bucket] = list;
                }

                list.Add((region, dependent));
            }
        }

        /// <summary>
        /// All formula cells whose precedent region overlaps <paramref name="queryRange"/>.
        /// Equivalent to the old flattened-cell containment check in
        /// <see cref="GetDirectDependents(Workbook, GridRange)"/> (both region and query are
        /// axis-aligned rectangles on the same sheet, so region-overlap and
        /// any-flattened-cell-contained agree exactly), but touches only the buckets that can
        /// overlap instead of every formula cell in the workbook.
        /// </summary>
        internal HashSet<CellAddress> GetDirectDependents(GridRange queryRange)
        {
            var result = new HashSet<CellAddress>();
            CollectFrom(_rowBuckets, queryRange, RowBucketSize, byRow: true, result);
            CollectFrom(_columnBuckets, queryRange, ColumnBucketSize, byRow: false, result);
            return result;
        }

        private static void CollectFrom(
            Dictionary<SheetId, Dictionary<uint, List<(GridRange Region, CellAddress Dependent)>>> buckets,
            GridRange queryRange,
            uint bucketSize,
            bool byRow,
            HashSet<CellAddress> result)
        {
            if (!buckets.TryGetValue(queryRange.Start.Sheet, out var sheetBuckets))
                return;

            var start = byRow ? queryRange.Start.Row : queryRange.Start.Col;
            var end = byRow ? queryRange.End.Row : queryRange.End.Col;
            var startBucket = GetBucket(start, bucketSize);
            var endBucket = GetBucket(end, bucketSize);

            for (var bucket = startBucket; bucket <= endBucket; bucket++)
            {
                if (!sheetBuckets.TryGetValue(bucket, out var list))
                    continue;

                foreach (var (region, dependent) in list)
                {
                    if (region.Overlaps(queryRange))
                        result.Add(dependent);
                }
            }
        }

        private static bool UseRowIndex(GridRange range) =>
            GetBucketCount(range.Start.Row, range.End.Row, RowBucketSize) <=
            GetBucketCount(range.Start.Col, range.End.Col, ColumnBucketSize);

        private static uint GetBucketCount(uint start, uint end, uint bucketSize) =>
            GetBucket(end, bucketSize) - GetBucket(start, bucketSize) + 1;

        private static uint GetBucket(uint value, uint bucketSize) => (value - 1) / bucketSize;
    }

    /// <summary>
    /// Build a <see cref="FormulaDependentsIndex"/> for a multi-level dependents traversal to
    /// reuse across every BFS/recursion step, instead of each step re-scanning the whole workbook
    /// via <see cref="GetDirectDependents(Workbook, GridRange)"/>.
    /// </summary>
    internal static FormulaDependentsIndex BuildDependentsIndex(Workbook workbook) =>
        FormulaDependentsIndex.Build(workbook);

    /// <summary>
    /// Query a pre-built <see cref="FormulaDependentsIndex"/> instead of re-scanning the workbook.
    /// Result ordering matches <see cref="GetDirectDependents(Workbook, GridRange)"/> exactly
    /// (workbook sheet order, then row, then column).
    /// </summary>
    internal static IReadOnlyList<CellAddress> GetDirectDependents(
        Workbook workbook, FormulaDependentsIndex index, GridRange precedentRange)
        => SortByWorkbookOrder(workbook, index.GetDirectDependents(precedentRange)).ToList();
}
