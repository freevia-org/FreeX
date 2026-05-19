namespace Freexcel.App.Host;

public static class OutlineGroupingPlanner
{
    public static int GetNextOutlineLevel(uint start, uint end, IReadOnlyDictionary<uint, int> outlineLevels)
    {
        var maxExisting = 0;
        for (var index = start; index <= end; index++)
        {
            if (outlineLevels.TryGetValue(index, out var level) && level > maxExisting)
                maxExisting = level;
        }

        return Math.Min(maxExisting + 1, 8);
    }
}
