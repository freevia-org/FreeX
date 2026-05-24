namespace Freexcel.Core.Model;

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
        _mergeIndex = new Dictionary<(uint, uint), GridRange>(_mergedRegions.Count * 4);
        foreach (var region in _mergedRegions)
            for (var r = region.Start.Row; r <= region.End.Row; r++)
                for (var c = region.Start.Col; c <= region.End.Col; c++)
                    _mergeIndex[(r, c)] = region;
    }

    /// <summary>Returns the merged region that contains <paramref name="addr"/>, or null if not merged.</summary>
    public GridRange? GetMergeRegion(CellAddress addr)
    {
        EnsureMergeIndex();
        return _mergeIndex!.TryGetValue((addr.Row, addr.Col), out var r) ? r : null;
    }

    /// <summary>True if <paramref name="addr"/> is inside any merged region.</summary>
    public bool IsMerged(CellAddress addr) => GetMergeRegion(addr) is not null;
}
