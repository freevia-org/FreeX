using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>How the areas of a copyable multiple selection combine into one block.</summary>
public enum MultiRangeCopyOrientation
{
    /// <summary>All areas share the same rows; they combine left-to-right.</summary>
    SideBySideColumns,

    /// <summary>All areas share the same columns; they combine top-to-bottom.</summary>
    StackedRows
}

public sealed record MultiRangeCopyLayout(
    MultiRangeCopyOrientation Orientation,
    IReadOnlyList<GridRange> OrderedAreas);

/// <summary>
/// Decides whether a multiple-area selection can be copied as a single block, matching
/// Excel: a multi-area copy is allowed only when every area shares the same rows (combining
/// side by side) or the same columns (combining stacked). Anything else is rejected, just as
/// Excel rejects it with "this action won't work on multiple selections". Cut is never allowed
/// on a multiple selection and is handled separately.
/// </summary>
public static class MultiRangeCopyPlanner
{
    public static bool TryPlan(IReadOnlyList<GridRange> ranges, out MultiRangeCopyLayout? layout)
    {
        layout = null;
        if (ranges is null || ranges.Count < 2)
            return false;

        var sheet = ranges[0].Start.Sheet;
        foreach (var range in ranges)
        {
            if (range.Start.Sheet != sheet || range.End.Sheet != sheet)
                return false;
        }

        var first = ranges[0];
        var sameRows = ranges.All(r => r.Start.Row == first.Start.Row && r.End.Row == first.End.Row);
        var sameColumns = ranges.All(r => r.Start.Col == first.Start.Col && r.End.Col == first.End.Col);

        // Identical row and column spans means the areas overlap/duplicate; Excel cannot copy that.
        if (sameRows && sameColumns)
            return false;

        if (sameRows)
        {
            var ordered = ranges.OrderBy(r => r.Start.Col).ToArray();
            for (var i = 1; i < ordered.Length; i++)
            {
                if (ordered[i].Start.Col <= ordered[i - 1].End.Col)
                    return false; // overlapping columns
            }

            layout = new MultiRangeCopyLayout(MultiRangeCopyOrientation.SideBySideColumns, ordered);
            return true;
        }

        if (sameColumns)
        {
            var ordered = ranges.OrderBy(r => r.Start.Row).ToArray();
            for (var i = 1; i < ordered.Length; i++)
            {
                if (ordered[i].Start.Row <= ordered[i - 1].End.Row)
                    return false; // overlapping rows
            }

            layout = new MultiRangeCopyLayout(MultiRangeCopyOrientation.StackedRows, ordered);
            return true;
        }

        return false;
    }
}
