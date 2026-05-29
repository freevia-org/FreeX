namespace FreeX.Core.Commands;

internal static class RangeSnapshot
{
    public static Dictionary<uint, double> Capture(Dictionary<uint, double> source, uint start, uint end)
    {
        var snapshot = new Dictionary<uint, double>();
        for (uint i = start; i <= end; i++)
        {
            if (source.TryGetValue(i, out var value))
                snapshot[i] = value;
        }

        return snapshot;
    }

    public static void Restore(Dictionary<uint, double> target, uint start, uint end, Dictionary<uint, double> snapshot)
    {
        for (uint i = start; i <= end; i++)
            target.Remove(i);

        foreach (var (key, value) in snapshot)
            target[key] = value;
    }

    public static HashSet<uint> Capture(HashSet<uint> source, uint start, uint end) =>
        source.Where(i => i >= start && i <= end).ToHashSet();

    public static void Restore(HashSet<uint> target, uint start, uint end, HashSet<uint> snapshot)
    {
        target.RemoveWhere(i => i >= start && i <= end);
        target.UnionWith(snapshot);
    }
}
