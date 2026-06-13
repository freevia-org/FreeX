using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum OutlineGroupingAxis
{
    Rows,
    Columns
}

public static class OutlineGroupingService
{
    public static void ValidateOutlineLevel(int level)
    {
        if (level is < 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(level), "Outline level must be 0–8.");
    }

    public static OutlineGroupingAxis GetGroupingAxis(GridRange range) =>
        SelectionRangeService.IsWholeColumnSelection(range)
            ? OutlineGroupingAxis.Columns
            : OutlineGroupingAxis.Rows;

    public static int GetGroupedOutlineLevel(int previousLevel, int requestedLevel, bool preserveExistingHierarchy)
    {
        ValidateOutlineLevel(requestedLevel);
        if (requestedLevel == 0)
            return 0;

        if (!preserveExistingHierarchy)
            return requestedLevel;

        var normalizedPrevious = Math.Clamp(previousLevel, 0, 8);
        return normalizedPrevious > 0
            ? Math.Min(normalizedPrevious + 1, 8)
            : 1;
    }
}
