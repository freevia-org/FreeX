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
}
