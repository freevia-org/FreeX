namespace FreeX.Core.Model;

public readonly record struct StyleOnlyRun(uint Row, uint StartCol, uint EndCol, StyleId StyleId);

public sealed partial class Sheet
{
    /// <summary>Whether any empty cells carry style-only overrides.</summary>
    public bool HasStyleOnlyCells => StyleOnlyCellCount != 0;

    /// <summary>Number of empty cells that carry style-only overrides.</summary>
    public int StyleOnlyCellCount =>
        _styleOnlyRunCellCount -
        (_styleOnlyRunTombstones?.Count ?? 0) +
        _styleOnlyOverlayNewCellCount;

    /// <summary>Pre-size style-only storage for bulk importers.</summary>
    public void EnsureStyleOnlyCapacity(int capacity)
    {
        if (capacity > _styleOnly.Count)
            _styleOnly.EnsureCapacity(capacity);
    }

    /// <summary>Returns the style-only override for an empty cell, or null if none exists.</summary>
    public StyleId? GetStyleOnly(uint row, uint col)
    {
        var key = (row, col);
        if (_styleOnly.TryGetValue(key, out var styleId))
            return styleId;

        if (_styleOnlyRunTombstones?.Contains(key) == true)
            return null;

        return TryGetStyleOnlyRun(row, col);
    }

    /// <summary>Sets a style-only override for an empty cell.</summary>
    public void SetStyleOnly(uint row, uint col, StyleId styleId)
    {
        var key = (row, col);
        var isRunBacked = TryGetStyleOnlyRun(row, col).HasValue;
        if (isRunBacked)
        {
            _styleOnlyRunTombstones?.Remove(key);
        }
        else if (!_styleOnly.ContainsKey(key))
        {
            _styleOnlyOverlayNewCellCount++;
        }

        _styleOnly[key] = styleId;
    }

    /// <summary>Removes the style-only override for an empty cell.</summary>
    public void ClearStyleOnly(uint row, uint col)
    {
        var key = (row, col);
        var isRunBacked = TryGetStyleOnlyRun(row, col).HasValue;
        if (_styleOnly.Remove(key) && !isRunBacked)
            _styleOnlyOverlayNewCellCount--;

        if (isRunBacked)
        {
            _styleOnlyRunTombstones ??= [];
            _styleOnlyRunTombstones.Add(key);
        }
    }

    /// <summary>Removes all style-only overrides for empty cells.</summary>
    public void ClearStyleOnlyEntries()
    {
        _styleOnly.Clear();
        _styleOnlyRuns = null;
        _styleOnlyRunTombstones?.Clear();
        _styleOnlyRunCellCount = 0;
        _styleOnlyOverlayNewCellCount = 0;
    }

    /// <summary>Replaces the compressed style-only base runs used by bulk importers.</summary>
    public void SetStyleOnlyRuns(IReadOnlyList<StyleOnlyRun> runs)
    {
        _styleOnlyRuns = NormalizeStyleOnlyRuns(runs);
        _styleOnlyRunTombstones?.Clear();
        _styleOnlyRunCellCount = 0;
        if (_styleOnlyRuns is not null)
        {
            foreach (var run in _styleOnlyRuns)
            {
                checked
                {
                    _styleOnlyRunCellCount += (int)(run.EndCol - run.StartCol + 1);
                }
            }
        }

        RecalculateStyleOnlyOverlayNewCellCount();
    }

    /// <summary>Enumerates all style-only entries (for empty cells that have been styled).</summary>
    public IEnumerable<((uint Row, uint Col) Key, StyleId StyleId)> GetStyleOnlyEntries()
    {
        if (_styleOnlyRuns is { Count: > 0 } runs)
        {
            foreach (var run in runs)
            {
                for (var col = run.StartCol;; col++)
                {
                    var key = (run.Row, col);
                    if (_styleOnly.TryGetValue(key, out var overlayStyleId))
                    {
                        yield return (key, overlayStyleId);
                        continue;
                    }

                    if (_styleOnlyRunTombstones?.Contains(key) == true)
                        continue;

                    yield return (key, run.StyleId);

                    if (col == run.EndCol)
                        break;
                }
            }
        }

        foreach (var entry in _styleOnly)
        {
            if (TryGetStyleOnlyRun(entry.Key.Row, entry.Key.Col).HasValue)
                continue;

            yield return (entry.Key, entry.Value);
        }
    }

    private void RecalculateStyleOnlyOverlayNewCellCount()
    {
        _styleOnlyOverlayNewCellCount = 0;
        foreach (var ((row, col), _) in _styleOnly)
        {
            if (!TryGetStyleOnlyRun(row, col).HasValue)
                _styleOnlyOverlayNewCellCount++;
        }
    }

    private StyleId? TryGetStyleOnlyRun(uint row, uint col)
    {
        var runs = _styleOnlyRuns;
        if (runs is not { Count: > 0 })
            return null;

        var low = 0;
        var high = runs.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var run = runs[mid];
            if (row < run.Row || row == run.Row && col < run.StartCol)
            {
                high = mid - 1;
                continue;
            }

            if (row > run.Row || row == run.Row && col > run.EndCol)
            {
                low = mid + 1;
                continue;
            }

            return run.StyleId;
        }

        return null;
    }

    private static List<StyleOnlyRun>? NormalizeStyleOnlyRuns(IReadOnlyList<StyleOnlyRun> runs)
    {
        if (runs.Count == 0)
            return null;

        var sorted = new List<StyleOnlyRun>(runs.Count);
        foreach (var run in runs)
        {
            if (run.EndCol >= run.StartCol)
                sorted.Add(run);
        }

        if (sorted.Count == 0)
            return null;

        sorted.Sort(static (left, right) =>
        {
            var rowCompare = left.Row.CompareTo(right.Row);
            return rowCompare != 0 ? rowCompare : left.StartCol.CompareTo(right.StartCol);
        });

        var merged = new List<StyleOnlyRun>(sorted.Count);
        foreach (var run in sorted)
        {
            if (merged.Count > 0)
            {
                var last = merged[^1];
                if (last.Row == run.Row &&
                    last.StyleId == run.StyleId &&
                    last.EndCol != uint.MaxValue &&
                    run.StartCol <= last.EndCol + 1)
                {
                    merged[^1] = last with { EndCol = Math.Max(last.EndCol, run.EndCol) };
                    continue;
                }
            }

            merged.Add(run);
        }

        return merged;
    }
}
