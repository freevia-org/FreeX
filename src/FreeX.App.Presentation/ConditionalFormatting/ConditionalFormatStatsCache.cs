namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// Caches range statistics for one conditional-format render pass.
/// </summary>
public sealed class ConditionalFormatStatsCache
{
    private readonly Dictionary<object, ConditionalFormatStatistics> _byRangeKey = new();

    public int BuiltRangeCount => _byRangeKey.Count;

    public ConditionalFormatStatistics GetOrAdd(
        object rangeKey,
        Func<IEnumerable<double>> valueFactory)
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
