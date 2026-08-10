namespace FreeX.Core.Model;

/// <summary>
/// Renderer-neutral rules for turning measured row or column sizes into page-axis breaks.
/// Preview, print, and export paths should use this contract so repeat ranges, accumulated
/// breaks, manual breaks, and the count-slicing bypass stay identical across renderers.
/// </summary>
public static class PageAxisPaginationRules
{
    /// <summary>
    /// Sums the visible, non-negative sizes in a repeat range after clipping it to the axis limit.
    /// </summary>
    public static double ComputeRepeatRangeSize(
        WorksheetRepeatRange? repeat,
        uint maxItem,
        Func<uint, bool>? isHidden,
        Func<uint, double> sizeOf)
    {
        if (repeat is not { } range || range.Start == 0 || range.Start > maxItem || range.End < range.Start)
            return 0.0;

        var total = 0.0;
        var end = Math.Min(range.End, maxItem);
        for (var value = range.Start; value <= end; value++)
        {
            if (value >= 1 && isHidden?.Invoke(value) != true)
                total += Math.Max(0.0, sizeOf(value));
        }

        return total;
    }

    /// <summary>
    /// Computes breaks from the accumulated visible body size, guaranteeing at least one body item
    /// per page even when one item is larger than the available size.
    /// </summary>
    public static List<uint> ComputeAccumulationBreakPoints(
        uint startValue,
        uint endValue,
        WorksheetRepeatRange? repeat,
        Func<uint, bool>? isHidden,
        Func<uint, double> sizeOf,
        double availableBodySize)
    {
        var breaks = new List<uint>();
        if (endValue < startValue)
            return breaks;

        var budget = double.IsFinite(availableBodySize) ? Math.Max(1.0, availableBodySize) : double.MaxValue;
        var accumulated = 0.0;
        var pageHasValue = false;
        for (var value = startValue; value <= endValue; value++)
        {
            if (IsWithinRepeatRange(repeat, value) || isHidden?.Invoke(value) == true)
                continue;

            var size = Math.Max(0.0, sizeOf(value));
            if (pageHasValue && accumulated + size > budget)
            {
                breaks.Add(value);
                accumulated = 0.0;
                pageHasValue = false;
            }

            accumulated += size;
            pageHasValue = true;
        }

        return breaks;
    }

    /// <summary>Unions renderer-computed breaks with user-authored manual breaks.</summary>
    public static List<uint> MergeBreaks(IReadOnlyCollection<uint>? userBreaks, List<uint> computedBreaks)
    {
        if (computedBreaks.Count == 0)
            return userBreaks is null ? [] : new List<uint>(userBreaks);

        var merged = new HashSet<uint>(computedBreaks);
        if (userBreaks is not null)
            merged.UnionWith(userBreaks);

        return [.. merged];
    }

    /// <summary>
    /// Returns a capacity larger than the requested axis span so count-based pagination cannot add
    /// breaks alongside the accumulated and manual breaks.
    /// </summary>
    public static uint UnboundedAxisCapacity(uint start, uint end) =>
        end >= start ? (uint)Math.Min(uint.MaxValue - 1L, (long)(end - start) + 2L) : 1u;

    private static bool IsWithinRepeatRange(WorksheetRepeatRange? repeat, uint value) =>
        repeat is { } range && value >= range.Start && value <= range.End;
}
