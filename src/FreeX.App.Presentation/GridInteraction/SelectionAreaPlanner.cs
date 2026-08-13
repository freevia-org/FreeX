using FreeX.Core.Model;

namespace FreeX.App.Presentation.GridInteraction;

/// <summary>
/// Builds the ordered selection-area list used while starting or extending a disjoint selection.
/// </summary>
public static class SelectionAreaPlanner
{
    /// <summary>
    /// Appends <paramref name="activeRange"/> for a fresh Ctrl+click, or replaces the last area
    /// while that same Ctrl+drag gesture is being extended. When a renderer has not yet materialized
    /// an area list, <paramref name="currentActive"/> seeds it so the original selection is retained.
    /// </summary>
    public static IReadOnlyList<GridRange> AppendOrReplaceActiveArea(
        IReadOnlyList<GridRange>? selectedRanges,
        GridRange? currentActive,
        GridRange activeRange,
        bool startNewArea)
    {
        var selectedCount = selectedRanges?.Count ?? 0;
        var seedCurrentActive = selectedCount == 0 && currentActive.HasValue;
        var existingCount = selectedCount + (seedCurrentActive ? 1 : 0);
        var replaceLast = !startNewArea && existingCount > 0;
        var result = new GridRange[existingCount + (replaceLast ? 0 : 1)];

        if (selectedRanges is not null)
        {
            for (var index = 0; index < selectedCount; index++)
                result[index] = selectedRanges[index];
        }

        if (seedCurrentActive)
            result[0] = currentActive!.Value;

        result[replaceLast ? existingCount - 1 : existingCount] = activeRange;
        return result;
    }
}
