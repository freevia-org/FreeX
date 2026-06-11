using FreeX.Core.Model;

namespace FreeX.Core.Calc;

public sealed partial class ViewportService
{
    private static IReadOnlyList<OutlineGroupRange> BuildRowOutlineGroups(Sheet sheet) =>
        BuildOutlineGroups(
            sheet.RowOutlineLevels,
            sheet.GroupHiddenRows,
            sheet.OutlineSummaryBelow ?? true,
            CellAddress.MaxRow);

    private static IReadOnlyList<OutlineGroupRange> BuildColumnOutlineGroups(Sheet sheet) =>
        BuildOutlineGroups(
            sheet.ColOutlineLevels,
            sheet.GroupHiddenCols,
            sheet.OutlineSummaryRight ?? true,
            CellAddress.MaxCol);

    private static IReadOnlyList<OutlineGroupRange> BuildOutlineGroups(
        IReadOnlyDictionary<uint, int> outlineLevels,
        IReadOnlySet<uint> hiddenByGroup,
        bool summaryAfter,
        uint maxIndex)
    {
        if (outlineLevels.Count == 0)
            return [];

        var maxLevel = 0;
        foreach (var level in outlineLevels.Values)
        {
            if (level > maxLevel)
                maxLevel = Math.Min(level, 8);
        }

        if (maxLevel <= 0)
            return [];

        var indices = new uint[outlineLevels.Count];
        var indexCount = 0;
        foreach (var index in outlineLevels.Keys)
            indices[indexCount++] = index;
        Array.Sort(indices);

        var groups = new List<OutlineGroupRange>();
        for (var level = 1; level <= maxLevel; level++)
            AddOutlineGroupsForLevel(groups, indices, outlineLevels, hiddenByGroup, summaryAfter, maxIndex, level);

        return groups;
    }

    private static void AddOutlineGroupsForLevel(
        List<OutlineGroupRange> groups,
        IReadOnlyList<uint> indices,
        IReadOnlyDictionary<uint, int> outlineLevels,
        IReadOnlySet<uint> hiddenByGroup,
        bool summaryAfter,
        uint maxIndex,
        int level)
    {
        var inRun = false;
        var start = 0u;
        var previous = 0u;
        var allHidden = true;

        foreach (var index in indices)
        {
            var belongsToLevel =
                outlineLevels.TryGetValue(index, out var outlineLevel) &&
                outlineLevel >= level;
            if (!belongsToLevel)
            {
                FlushOutlineGroup(groups, inRun, start, previous, allHidden, summaryAfter, maxIndex, level);
                inRun = false;
                allHidden = true;
                continue;
            }

            if (!inRun || index != previous + 1)
            {
                FlushOutlineGroup(groups, inRun, start, previous, allHidden, summaryAfter, maxIndex, level);
                start = index;
                allHidden = true;
                inRun = true;
            }

            if (!hiddenByGroup.Contains(index))
                allHidden = false;
            previous = index;
        }

        FlushOutlineGroup(groups, inRun, start, previous, allHidden, summaryAfter, maxIndex, level);
    }

    private static void FlushOutlineGroup(
        List<OutlineGroupRange> groups,
        bool inRun,
        uint start,
        uint end,
        bool allHidden,
        bool summaryAfter,
        uint maxIndex,
        int level)
    {
        if (!inRun || end < start)
            return;

        groups.Add(new OutlineGroupRange(
            level,
            start,
            end,
            GetOutlineToggleIndex(start, end, summaryAfter, maxIndex),
            allHidden));
    }

    private static uint GetOutlineToggleIndex(uint start, uint end, bool summaryAfter, uint maxIndex)
    {
        if (summaryAfter)
            return end < maxIndex ? end + 1 : start > 1 ? start - 1 : end;

        return start > 1 ? start - 1 : end < maxIndex ? end + 1 : start;
    }
}
