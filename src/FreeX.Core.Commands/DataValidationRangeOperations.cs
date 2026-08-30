using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Allocation-conscious range operations shared by data-validation commands. A validation's
/// primary and additional ranges are exposed through indexed access so overlap-only paths do not
/// need temporary arrays or iterator pipelines.
/// </summary>
internal static class DataValidationRangeOperations
{
    private static int RangeCount(DataValidation rule) =>
        1 + rule.AdditionalRanges.Count;

    private static GridRange GetRange(DataValidation rule, int index) =>
        index == 0 ? rule.AppliesTo : rule.AdditionalRanges[index - 1];

    public static List<GridRange> Subtract(DataValidation source, GridRange footprint)
    {
        var remaining = new List<GridRange>(RangeCount(source) * 2);
        for (var index = 0; index < RangeCount(source); index++)
            remaining.AddRange(GridRangeSubtraction.Subtract(GetRange(source, index), footprint));

        return remaining;
    }

    public static List<GridRange> Subtract(DataValidation source, DataValidation footprints)
    {
        var remaining = new List<GridRange>(RangeCount(source));
        for (var index = 0; index < RangeCount(source); index++)
            remaining.Add(GetRange(source, index));

        for (var footprintIndex = 0; footprintIndex < RangeCount(footprints); footprintIndex++)
        {
            if (remaining.Count == 0)
                break;

            var footprint = GetRange(footprints, footprintIndex);
            var next = new List<GridRange>(remaining.Count * 2);
            foreach (var range in remaining)
                next.AddRange(GridRangeSubtraction.Subtract(range, footprint));

            remaining = next;
        }

        return remaining;
    }
}
