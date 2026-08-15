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

    /// <summary>
    /// Returns the style-only override for an empty cell, or null if none exists. Falls back to the
    /// whole-row default style, then the whole-column default style, when the cell carries no
    /// per-cell style-only entry of its own -- matching Excel's cell &gt; row &gt; column resolution
    /// order (R136-io-worksheet-props-col-row-default-style). A tombstoned entry (a per-cell
    /// style-only run explicitly cleared for this address) does NOT fall through: an explicit clear
    /// is itself a cell-level override that beats the row/column default, same as Excel.
    /// </summary>
    public StyleId? GetStyleOnly(uint row, uint col)
    {
        var key = (row, col);
        if (_styleOnly.TryGetValue(key, out var styleId))
            return styleId;

        if (_styleOnlyRunTombstones?.Contains(key) == true)
            return null;

        if (TryGetStyleOnlyRun(row, col) is { } runStyleId)
            return runStyleId;

        if (RowStyles.Count != 0 && RowStyles.TryGetValue(row, out var rowStyleId))
            return rowStyleId;

        if (ColumnStyles.Count != 0 && ColumnStyles.TryGetValue(col, out var colStyleId))
            return colStyleId;

        return null;
    }

    /// <summary>Sets a style-only override for an empty cell.</summary>
    public void SetStyleOnly(uint row, uint col, StyleId styleId)
    {
        var key = (row, col);
        var runStyleId = TryGetStyleOnlyRun(row, col);
        if (runStyleId.HasValue)
        {
            _styleOnlyRunTombstones?.Remove(key);
            if (runStyleId.Value == styleId)
            {
                _styleOnly.Remove(key);
                return;
            }
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

        // R92-render-cellstyle-inheritance-5-3: the row/column-format provenance tag (if any)
        // describes THIS style-only entry -- once the entry itself is gone (superseded by a real
        // cell, or explicitly cleared), the tag must not survive to be misread against whatever
        // unrelated style-only entry a later command creates at the same address.
        ClearStyleOnlySource(row, col);
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

        RemoveRedundantStyleOnlyRunOverlays();
        RecalculateStyleOnlyOverlayNewCellCount();
    }

    /// <summary>Enumerates all style-only entries (for empty cells that have been styled).</summary>
    public IEnumerable<((uint Row, uint Col) Key, StyleId StyleId)> GetStyleOnlyEntries()
    {
        if (_styleOnlyRuns is { Count: > 0 } runs)
        {
            foreach (var run in runs)
            {
                var col = run.StartCol;
                while (true)
                {
                    var key = (run.Row, col);
                    if (_styleOnly.TryGetValue(key, out var overlayStyleId))
                    {
                        yield return (key, overlayStyleId);
                    }
                    else if (_styleOnlyRunTombstones?.Contains(key) != true)
                    {
                        yield return (key, run.StyleId);
                    }

                    if (col == run.EndCol)
                        break;

                    col++;
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

    /// <summary>
    /// Returns compressed style-only runs when there are no mutable overlays or tombstones.
    /// Callers that need exact cell-level output should use <see cref="GetStyleOnlyEntries"/>.
    /// </summary>
    public bool TryGetCompressedStyleOnlyRuns(out IReadOnlyList<StyleOnlyRun> runs)
    {
        runs = _styleOnlyRuns ?? [];
        return _styleOnly.Count == 0 &&
               (_styleOnlyRunTombstones is null || _styleOnlyRunTombstones.Count == 0);
    }

    private void RemoveRedundantStyleOnlyRunOverlays()
    {
        if (_styleOnly.Count == 0 || _styleOnlyRuns is not { Count: > 0 })
            return;

        List<(uint Row, uint Col)>? redundantKeys = null;
        foreach (var (key, styleId) in _styleOnly)
        {
            if (TryGetStyleOnlyRun(key.Row, key.Col) != styleId)
                continue;

            redundantKeys ??= [];
            redundantKeys.Add(key);
        }

        if (redundantKeys is null)
            return;

        foreach (var key in redundantKeys)
            _styleOnly.Remove(key);
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
