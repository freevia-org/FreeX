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

    private sealed class MergeRegionIndex
    {
        private readonly GridRange[] _regionsByStartRow;
        private readonly uint[] _prefixMaxEndRows;

        private MergeRegionIndex(GridRange[] regionsByStartRow, uint[] prefixMaxEndRows)
        {
            _regionsByStartRow = regionsByStartRow;
            _prefixMaxEndRows = prefixMaxEndRows;
        }

        public static MergeRegionIndex Create(IReadOnlyList<GridRange> regions)
        {
            var regionsByStartRow = new GridRange[regions.Count];
            for (var i = 0; i < regions.Count; i++)
                regionsByStartRow[i] = regions[i];

            Array.Sort(regionsByStartRow, static (left, right) =>
            {
                var rowComparison = left.Start.Row.CompareTo(right.Start.Row);
                return rowComparison != 0 ? rowComparison : left.Start.Col.CompareTo(right.Start.Col);
            });

            var prefixMaxEndRows = new uint[regionsByStartRow.Length];
            var maxEndRow = 0u;
            for (var i = 0; i < regionsByStartRow.Length; i++)
            {
                maxEndRow = Math.Max(maxEndRow, regionsByStartRow[i].End.Row);
                prefixMaxEndRows[i] = maxEndRow;
            }

            return new MergeRegionIndex(regionsByStartRow, prefixMaxEndRows);
        }

        public GridRange? Find(uint row, uint col)
        {
            var index = LastRegionStartingAtOrBefore(row);
            while (index >= 0 && _prefixMaxEndRows[index] >= row)
            {
                var region = _regionsByStartRow[index];
                if (region.Start.Row <= row &&
                    region.End.Row >= row &&
                    region.Start.Col <= col &&
                    region.End.Col >= col)
                {
                    return region;
                }

                index--;
            }

            return null;
        }

        private int LastRegionStartingAtOrBefore(uint row)
        {
            var low = 0;
            var high = _regionsByStartRow.Length - 1;
            var result = -1;

            while (low <= high)
            {
                var mid = low + ((high - low) / 2);
                if (_regionsByStartRow[mid].Start.Row <= row)
                {
                    result = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return result;
        }
    }
}
