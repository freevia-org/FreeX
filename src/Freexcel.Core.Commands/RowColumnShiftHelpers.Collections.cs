namespace Freexcel.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    internal static void ShiftIndexesUp(Dictionary<uint, double> values, uint start, uint count)
    {
        var shifted = values
            .Where(p => p.Key >= start)
            .OrderByDescending(p => p.Key)
            .ToList();

        foreach (var (key, _) in shifted)
            values.Remove(key);
        foreach (var (key, value) in shifted)
            values[key + count] = value;
    }

    internal static void ShiftIndexesDown(Dictionary<uint, double> values, uint start, uint count)
    {
        var end = start + count - 1;
        var shifted = values
            .Where(p => p.Key > end)
            .OrderBy(p => p.Key)
            .ToList();
        var removed = values.Keys.Where(key => key >= start && key <= end).ToList();

        foreach (var key in removed)
            values.Remove(key);
        foreach (var (key, _) in shifted)
            values.Remove(key);
        foreach (var (key, value) in shifted)
            values[key - count] = value;
    }

    internal static void ShiftSortedSetUp(SortedSet<uint> values, uint start, uint count)
    {
        var shifted = values.Where(value => value >= start).OrderByDescending(value => value).ToList();
        foreach (var value in shifted)
            values.Remove(value);
        foreach (var value in shifted)
            values.Add(value + count);
    }

    internal static void ShiftSortedSetDown(SortedSet<uint> values, uint start, uint count)
    {
        var end = start + count - 1;
        var removed = values.Where(value => value >= start && value <= end).ToList();
        var shifted = values.Where(value => value > end).OrderBy(value => value).ToList();

        foreach (var value in removed)
            values.Remove(value);
        foreach (var value in shifted)
            values.Remove(value);
        foreach (var value in shifted)
            values.Add(value - count);
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

    internal static void RestoreSet(HashSet<uint> target, HashSet<uint>? snapshot)
    {
        if (snapshot is null)
            return;

        target.Clear();
        target.UnionWith(snapshot);
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
}
