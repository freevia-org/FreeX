namespace FreeX.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    internal static List<KeyValuePair<TKey, TValue>>? CaptureDictionary<TKey, TValue>(
        Dictionary<TKey, TValue> source)
        where TKey : notnull
    {
        if (source.Count == 0)
            return null;

        var snapshot = new List<KeyValuePair<TKey, TValue>>(source.Count);
        foreach (var pair in source)
            snapshot.Add(pair);

        return snapshot;
    }

    internal static List<uint>? CaptureSet(HashSet<uint> source)
    {
        if (source.Count == 0)
            return null;

        var snapshot = new List<uint>(source.Count);
        foreach (var value in source)
            snapshot.Add(value);

        return snapshot;
    }

    internal static List<uint>? CaptureSortedSet(SortedSet<uint> source)
    {
        if (source.Count == 0)
            return null;

        var snapshot = new List<uint>(source.Count);
        foreach (var value in source)
            snapshot.Add(value);

        return snapshot;
    }

    internal static void ShiftIndexesUp(Dictionary<uint, double> values, uint start, uint count)
    {
        if (values.Count == 0)
            return;

        List<KeyValuePair<uint, double>>? shifted = null;
        foreach (var pair in values)
        {
            if (pair.Key >= start)
                (shifted ??= new List<KeyValuePair<uint, double>>(values.Count)).Add(pair);
        }

        if (shifted is null)
            return;

        foreach (var (key, _) in shifted)
            values.Remove(key);
        foreach (var (key, value) in shifted)
            values[key + count] = value;
    }

    internal static void ShiftIndexesDown(Dictionary<uint, double> values, uint start, uint count)
    {
        var end = start + count - 1;
        List<KeyValuePair<uint, double>>? shifted = null;
        List<uint>? removed = null;
        foreach (var pair in values)
        {
            if (pair.Key > end)
                (shifted ??= new List<KeyValuePair<uint, double>>(values.Count)).Add(pair);
            else if (pair.Key >= start)
                (removed ??= []).Add(pair.Key);
        }

        if (removed is not null)
        {
            foreach (var key in removed)
                values.Remove(key);
        }
        if (shifted is not null)
        {
            foreach (var (key, _) in shifted)
                values.Remove(key);
            foreach (var (key, value) in shifted)
                values[key - count] = value;
        }
    }

    /// <summary>
    /// Same shift-up semantics as <see cref="ShiftIndexesUp(Dictionary{uint, double}, uint, uint)"/>,
    /// generalized to any value type. Used for column-keyed maps other than widths — e.g.
    /// <c>Sheet.ActiveValueFilterColumns</c> (finding G1) — that must shift the same way ColumnWidths
    /// does on column insert.
    /// </summary>
    internal static void ShiftIndexesUp<TValue>(Dictionary<uint, TValue> values, uint start, uint count)
    {
        if (values.Count == 0)
            return;

        List<KeyValuePair<uint, TValue>>? shifted = null;
        foreach (var pair in values)
        {
            if (pair.Key >= start)
                (shifted ??= new List<KeyValuePair<uint, TValue>>(values.Count)).Add(pair);
        }

        if (shifted is null)
            return;

        foreach (var (key, _) in shifted)
            values.Remove(key);
        foreach (var (key, value) in shifted)
            values[key + count] = value;
    }

    /// <summary>
    /// Same shift-down-with-deletion semantics as
    /// <see cref="ShiftIndexesDown(Dictionary{uint, double}, uint, uint)"/>, generalized to any value
    /// type. Keys within the deleted range [start, start+count-1] are removed (the column/row itself
    /// was deleted); surviving keys above the range shift down by <paramref name="count"/>. Used for
    /// column-keyed maps other than widths — e.g. <c>Sheet.ActiveValueFilterColumns</c> (finding G1).
    /// </summary>
    internal static void ShiftIndexesDown<TValue>(Dictionary<uint, TValue> values, uint start, uint count)
    {
        var end = start + count - 1;
        List<KeyValuePair<uint, TValue>>? shifted = null;
        List<uint>? removed = null;
        foreach (var pair in values)
        {
            if (pair.Key > end)
                (shifted ??= new List<KeyValuePair<uint, TValue>>(values.Count)).Add(pair);
            else if (pair.Key >= start)
                (removed ??= []).Add(pair.Key);
        }

        if (removed is not null)
        {
            foreach (var key in removed)
                values.Remove(key);
        }
        if (shifted is not null)
        {
            foreach (var (key, _) in shifted)
                values.Remove(key);
            foreach (var (key, value) in shifted)
                values[key - count] = value;
        }
    }

    internal static void ShiftSortedSetUp(SortedSet<uint> values, uint start, uint count)
    {
        if (values.Count == 0)
            return;

        List<uint>? shifted = null;
        foreach (var value in values)
        {
            if (value >= start)
                (shifted ??= new List<uint>(values.Count)).Add(value);
        }

        if (shifted is null)
            return;

        foreach (var value in shifted)
            values.Remove(value);
        foreach (var value in shifted)
            values.Add(value + count);
    }

    internal static void ShiftSortedSetDown(SortedSet<uint> values, uint start, uint count)
    {
        var end = start + count - 1;
        List<uint>? removed = null;
        List<uint>? shifted = null;
        foreach (var value in values)
        {
            if (value > end)
                (shifted ??= new List<uint>(values.Count)).Add(value);
            else if (value >= start)
                (removed ??= []).Add(value);
        }

        if (removed is not null)
        {
            foreach (var value in removed)
                values.Remove(value);
        }
        if (shifted is not null)
        {
            foreach (var value in shifted)
                values.Remove(value);
            foreach (var value in shifted)
                values.Add(value - count);
        }
    }

    internal static void ShiftSetUpFrom(HashSet<uint> values, uint start, uint count)
    {
        if (values.Count == 0)
            return;

        List<uint>? shifted = null;
        foreach (var value in values)
        {
            if (value >= start)
                (shifted ??= new List<uint>(values.Count)).Add(value);
        }

        if (shifted is null)
            return;

        foreach (var value in shifted)
            values.Remove(value);
        foreach (var value in shifted)
            values.Add(value + count);
    }

    internal static void ShiftSetDownFrom(HashSet<uint> values, uint start, uint count)
    {
        if (values.Count == 0)
            return;

        List<uint>? shifted = null;
        foreach (var value in values)
        {
            if (value >= start)
                (shifted ??= new List<uint>(values.Count)).Add(value);
        }

        if (shifted is null)
            return;

        foreach (var value in shifted)
            values.Remove(value);
        foreach (var value in shifted)
            values.Add(value - count);
    }

    internal static void DeleteSetRangeAndShiftDown(HashSet<uint> values, uint start, uint count)
    {
        if (values.Count == 0)
            return;

        var end = start + count - 1;
        List<uint>? removed = null;
        List<uint>? shifted = null;
        foreach (var value in values)
        {
            if (value > end)
                (shifted ??= new List<uint>(values.Count)).Add(value);
            else if (value >= start)
                (removed ??= []).Add(value);
        }

        if (removed is not null)
        {
            foreach (var value in removed)
                values.Remove(value);
        }
        if (shifted is not null)
        {
            foreach (var value in shifted)
                values.Remove(value);
            foreach (var value in shifted)
                values.Add(value - count);
        }
    }

    internal static void RestoreSortedSet(SortedSet<uint> target, IReadOnlyCollection<uint>? snapshot)
    {
        if (snapshot is null)
            return;

        target.Clear();
        foreach (var value in snapshot)
            target.Add(value);
    }

    internal static void RestoreDictionary(Dictionary<uint, double> target, Dictionary<uint, double>? snapshot)
    {
        if (snapshot is null)
            return;

        target.Clear();
        foreach (var (key, value) in snapshot)
            target[key] = value;
    }

    internal static void RestoreDictionary(
        Dictionary<uint, double> target,
        IReadOnlyList<KeyValuePair<uint, double>>? snapshot)
    {
        if (snapshot is null)
            return;

        target.Clear();
        foreach (var (key, value) in snapshot)
            target[key] = value;
    }

    internal static void RestoreSet(HashSet<uint> target, IReadOnlyCollection<uint>? snapshot)
    {
        if (snapshot is null)
            return;

        target.Clear();
        foreach (var value in snapshot)
            target.Add(value);
    }

    internal static void RestoreDictionary<TKey, TValue>(
        Dictionary<TKey, TValue> target,
        Dictionary<TKey, TValue>? snapshot)
        where TKey : notnull
    {
        if (snapshot is null)
            return;

        target.Clear();
        foreach (var (key, value) in snapshot)
            target[key] = value;
    }

    internal static void RestoreDictionary<TKey, TValue>(
        Dictionary<TKey, TValue> target,
        IReadOnlyList<KeyValuePair<TKey, TValue>>? snapshot)
        where TKey : notnull
    {
        if (snapshot is null)
            return;

        target.Clear();
        foreach (var (key, value) in snapshot)
            target[key] = value;
    }

    /// <summary>
    /// Deep-clones a column-keyed row-set dictionary (<c>Sheet.ColumnFilterOwnedRows</c>) for
    /// snapshot/restore. The generic <see cref="CaptureDictionary{TKey,TValue}"/> only copies the
    /// key/value pairs — for a <see cref="HashSet{T}"/> value that aliases the same instance a
    /// subsequent row shift then mutates in place, which would silently corrupt the "before"
    /// snapshot. Cloning each column's row set keeps the snapshot independent (finding R13-meta-1).
    /// </summary>
    internal static Dictionary<uint, HashSet<uint>>? CaptureRowSetDictionary(Dictionary<uint, HashSet<uint>> source)
    {
        if (source.Count == 0)
            return null;

        var snapshot = new Dictionary<uint, HashSet<uint>>(source.Count);
        foreach (var (key, rows) in source)
            snapshot[key] = [.. rows];

        return snapshot;
    }

    /// <summary>
    /// Row-insert counterpart of <see cref="ShiftSetUpFrom"/>, applied to every column's owned-row
    /// set in <c>Sheet.ColumnFilterOwnedRows</c> so inserting rows doesn't leave a stale (pre-shift)
    /// row index behind — mirrors the sibling shift of <c>FilterHiddenRows</c> /
    /// <c>ValueFilterHiddenRows</c> (finding R13-meta-1).
    /// </summary>
    internal static void ShiftRowSetDictionaryUpFrom(Dictionary<uint, HashSet<uint>> values, uint start, uint count)
    {
        foreach (var rows in values.Values)
            ShiftSetUpFrom(rows, start, count);
    }

    /// <summary>
    /// Insert-undo counterpart of <see cref="ShiftRowSetDictionaryUpFrom"/> — the exact inverse
    /// shift, mirroring how <c>FilterHiddenRows</c> / <c>ValueFilterHiddenRows</c> are unshifted on
    /// <c>InsertRowsCommand.Revert</c>.
    /// </summary>
    internal static void ShiftRowSetDictionaryDownFrom(Dictionary<uint, HashSet<uint>> values, uint start, uint count)
    {
        foreach (var rows in values.Values)
            ShiftSetDownFrom(rows, start, count);
    }

    /// <summary>
    /// Row-delete counterpart of <see cref="DeleteSetRangeAndShiftDown"/>: removes owned rows that
    /// fall within the deleted range and shifts surviving rows down, per column (finding
    /// R13-meta-1). Deletion undo restores the full pre-delete snapshot instead of un-shifting (see
    /// <c>DeleteRowsCommand</c>, mirroring <c>FilterHiddenRows</c>).
    /// </summary>
    internal static void DeleteRowSetDictionaryRangeAndShiftDown(Dictionary<uint, HashSet<uint>> values, uint start, uint count)
    {
        foreach (var rows in values.Values)
            DeleteSetRangeAndShiftDown(rows, start, count);
    }
}
