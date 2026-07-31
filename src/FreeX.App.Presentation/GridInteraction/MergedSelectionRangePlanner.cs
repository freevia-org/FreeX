using FreeX.Core.Model;

namespace FreeX.App.Presentation.GridInteraction;

/// <summary>Expands a selection until it no longer bisects an intersecting merged region.</summary>
public static class MergedSelectionRangePlanner
{
    public static GridRange ExpandToFullyContainMerges(Sheet? sheet, GridRange range)
    {
        if (sheet is not { MergedRegions.Count: > 0 })
            return range;

        bool expanded;
        do
        {
            expanded = false;
            foreach (var merge in sheet.MergedRegions)
            {
                if (merge.Start.Sheet != range.Start.Sheet ||
                    !range.Overlaps(merge) ||
                    range.Contains(merge))
                {
                    continue;
                }

                range = new GridRange(
                    new CellAddress(
                        range.Start.Sheet,
                        Math.Min(range.Start.Row, merge.Start.Row),
                        Math.Min(range.Start.Col, merge.Start.Col)),
                    new CellAddress(
                        range.Start.Sheet,
                        Math.Max(range.End.Row, merge.End.Row),
                        Math.Max(range.End.Col, merge.End.Col)));
                expanded = true;
            }
        } while (expanded);

        return range;
    }
}
