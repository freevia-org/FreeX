namespace FreeX.Core.Model;

public sealed partial class Sheet
{
    /// <summary>Merged cell regions on this sheet. Each region's top-left cell holds the display value.</summary>
    public IReadOnlyList<GridRange> MergedRegions => _mergedRegions;

    /// <summary>Add a merged region and invalidate the merge index.</summary>
    public void AddMergedRegion(GridRange region) { _mergedRegions.Add(region); _mergeIndex = null; }

    /// <summary>Remove a merged region and invalidate the merge index.</summary>
    public bool RemoveMergedRegion(GridRange region) { var removed = _mergedRegions.Remove(region); if (removed) _mergeIndex = null; return removed; }

    /// <summary>Replace the entire merged-regions list and invalidate the merge index.</summary>
    public void ReplaceMergedRegions(IEnumerable<GridRange> regions)
    {
        // Materialize before clearing to guard against callers passing a lazy LINQ query
        // over MergedRegions itself (would otherwise enumerate an already-emptied list).
        var list = regions is List<GridRange> l ? l : regions.ToList();
        _mergedRegions.Clear();
        _mergedRegions.AddRange(list);
        _mergeIndex = null;
    }

    private void EnsureMergeIndex()
    {
        if (_mergeIndex is not null) return;
        _mergeIndex = MergeRegionIndex.Create(_mergedRegions);
    }

    /// <summary>Returns the merged region that contains <paramref name="addr"/>, or null if not merged.</summary>
    public GridRange? GetMergeRegion(CellAddress addr)
    {
        EnsureMergeIndex();
        return _mergeIndex!.Find(addr.Row, addr.Col);
    }

    /// <summary>True if <paramref name="addr"/> is inside any merged region.</summary>
    public bool IsMerged(CellAddress addr) => GetMergeRegion(addr) is not null;

    /// <summary>
    /// Answers "which merged region (if any) contains (row, col)" via a row-bucket/column-bucket
    /// index, mirroring the dual-axis bucketing approach RangeDependencyIndex/CandidateIndex
    /// already use in FreeX.Core.Calc.DependencyGraph for the exact same shape of problem (range
    /// membership queries against a mix of tall/narrow and short/wide rectangles).
    ///
    /// A prior implementation sorted regions by start row and used a running prefix-max of
    /// End.Row, answering Find() by binary-searching to the last region starting at-or-before
    /// `row` and then scanning backward until the prefix max fell below `row`. Because the
    /// prefix max is monotonically non-decreasing, a single region with a very large End.Row
    /// (e.g. spanning nearly the whole sheet) kept every later region's prefix-max entry at or
    /// above that End.Row for the remainder of the array -- so a query landing in that region's
    /// row-shadow but matching no intervening region had to walk backward through every one of
    /// those intervening regions before it could even reach (or rule out) the large region. That
    /// turned a single lookup into an O(total merged regions on the sheet) scan instead of the
    /// intended O(log n + k).
    ///
    /// Each region is registered under whichever axis (row bands or column bands) it spans FEWER
    /// buckets of -- exactly like RangeDependencyIndex.UseRowIndex. A region that is tall but
    /// narrow (e.g. a single merged column running the full height of the sheet) spans thousands
    /// of row-bands but only one or two column-bands, so it is filed under column buckets instead;
    /// this keeps index build cost/memory bounded by min(row bands, column bands) spanned rather
    /// than blowing up for any one very tall or very wide region. Find() only has to scan the
    /// (typically few) regions filed under the query row's band plus the (typically few) regions
    /// filed under the query column's band, independent of how many unrelated regions exist
    /// elsewhere on the sheet.
    /// </summary>
    private sealed class MergeRegionIndex
    {
        private const uint RowBucketSize = 256;
        private const uint ColumnBucketSize = 16;

        private readonly Dictionary<uint, List<GridRange>> _rowBuckets;
        private readonly Dictionary<uint, List<GridRange>> _columnBuckets;

        private MergeRegionIndex(Dictionary<uint, List<GridRange>> rowBuckets, Dictionary<uint, List<GridRange>> columnBuckets)
        {
            _rowBuckets = rowBuckets;
            _columnBuckets = columnBuckets;
        }

        public static MergeRegionIndex Create(IReadOnlyList<GridRange> regions)
        {
            var rowBuckets = new Dictionary<uint, List<GridRange>>();
            var columnBuckets = new Dictionary<uint, List<GridRange>>();

            foreach (var region in regions)
            {
                if (UseRowIndex(region))
                    AddToBuckets(rowBuckets, region, region.Start.Row, region.End.Row, RowBucketSize);
                else
                    AddToBuckets(columnBuckets, region, region.Start.Col, region.End.Col, ColumnBucketSize);
            }

            return new MergeRegionIndex(rowBuckets, columnBuckets);
        }

        public GridRange? Find(uint row, uint col)
        {
            if (_rowBuckets.TryGetValue(GetBucket(row, RowBucketSize), out var rowCandidates))
            {
                foreach (var region in rowCandidates)
                {
                    if (Contains(region, row, col))
                        return region;
                }
            }

            if (_columnBuckets.TryGetValue(GetBucket(col, ColumnBucketSize), out var columnCandidates))
            {
                foreach (var region in columnCandidates)
                {
                    if (Contains(region, row, col))
                        return region;
                }
            }

            return null;
        }

        private static bool Contains(GridRange region, uint row, uint col) =>
            region.Start.Row <= row &&
            region.End.Row >= row &&
            region.Start.Col <= col &&
            region.End.Col >= col;

        private static bool UseRowIndex(GridRange region) =>
            GetBucketCount(region.Start.Row, region.End.Row, RowBucketSize) <=
            GetBucketCount(region.Start.Col, region.End.Col, ColumnBucketSize);

        private static void AddToBuckets(
            Dictionary<uint, List<GridRange>> buckets,
            GridRange region,
            uint start,
            uint end,
            uint bucketSize)
        {
            var startBucket = GetBucket(start, bucketSize);
            var endBucket = GetBucket(end, bucketSize);

            for (var bucket = startBucket; bucket <= endBucket; bucket++)
            {
                if (!buckets.TryGetValue(bucket, out var bucketRegions))
                {
                    bucketRegions = [];
                    buckets[bucket] = bucketRegions;
                }

                bucketRegions.Add(region);
            }
        }

        private static uint GetBucketCount(uint start, uint end, uint bucketSize) =>
            GetBucket(end, bucketSize) - GetBucket(start, bucketSize) + 1;

        private static uint GetBucket(uint value, uint bucketSize) => (value - 1) / bucketSize;
    }
}
