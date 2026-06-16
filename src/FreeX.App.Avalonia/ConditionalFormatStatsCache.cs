using FreeX.App.Presentation.ConditionalFormatting;

namespace FreeX.App.Avalonia;

/// <summary>
/// Per-render-pass cache of <see cref="ConditionalFormatStatistics"/> keyed by conditional-format
/// range. Range-dependent rules (data bars, color scales, icon sets, above/below average) need the
/// same min/max/average/sorted aggregates for every cell in a range; building those aggregates is
/// O(range size), so without caching a viewport refresh would recompute them once per visible cell.
/// This cache builds the statistics lazily on first use for a range and reuses them for the rest of
/// the pass. Construct a fresh instance for each render pass so stale aggregates never leak across
/// edits — the engine's own context cache (keyed by content + rule version) handles cross-pass reuse.
/// </summary>
public sealed class ConditionalFormatStatsCache
{
    private readonly Dictionary<object, ConditionalFormatStatistics> _byRangeKey = new();

    /// <summary>Number of distinct ranges whose statistics have been materialized this pass.</summary>
    public int BuiltRangeCount => _byRangeKey.Count;

    /// <summary>
    /// Return the cached statistics for <paramref name="rangeKey"/>, building them from
    /// <paramref name="valueFactory"/> on first request. <paramref name="valueFactory"/> is invoked
    /// at most once per distinct key for the lifetime of this cache, so the caller's per-range value
    /// enumeration runs only on a cache miss.
    /// </summary>
    public ConditionalFormatStatistics GetOrAdd(object rangeKey, Func<IEnumerable<double>> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(rangeKey);
        ArgumentNullException.ThrowIfNull(valueFactory);

        if (_byRangeKey.TryGetValue(rangeKey, out var cached))
            return cached;

        var stats = ConditionalFormatStatistics.FromValues(valueFactory());
        _byRangeKey[rangeKey] = stats;
        return stats;
    }
}
