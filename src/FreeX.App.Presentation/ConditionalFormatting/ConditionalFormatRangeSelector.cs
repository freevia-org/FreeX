using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// Portable range-level selection for conditional-format rules whose decision depends on the
/// whole range rather than a single value: top/bottom-N (and top/bottom-percent) selection, and
/// duplicate / unique detection. The engine pre-computes these into per-cell match sets; this
/// type reproduces the same selection math over caller-supplied value sequences.
/// </summary>
public static class ConditionalFormatRangeSelector
{
    /// <summary>A numeric range entry: an opaque cell key plus its value, in range order.</summary>
    public readonly record struct ValueEntry<TKey>(TKey Key, double Value);

    /// <summary>
    /// Select the cells matched by a Top10 rule. <see cref="ConditionalFormat.AboveAverage"/> is
    /// reused by the model as the "is top" flag (true = top N, false = bottom N). Percent rules
    /// take ceil(count · rank / 100); both forms clamp the take to [1, count]. Ties are broken by
    /// range order, matching the engine's stable selection.
    /// </summary>
    public static HashSet<TKey> SelectTopBottom<TKey>(
        ConditionalFormat rule,
        IReadOnlyList<ValueEntry<TKey>> entries)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(entries);

        var result = new HashSet<TKey>();
        if (entries.Count == 0)
            return result;

        var take = Math.Clamp(
            rule.TopBottomPercent
                ? (int)Math.Ceiling(entries.Count * Math.Max(1, rule.TopBottomRank) / 100d)
                : rule.TopBottomRank,
            1,
            entries.Count);

        // Order by value (desc for top, asc for bottom), ties broken by original index.
        var indexed = new (TKey Key, double Value, int Index)[entries.Count];
        for (var i = 0; i < entries.Count; i++)
            indexed[i] = (entries[i].Key, entries[i].Value, i);

        var isTop = rule.AboveAverage;
        Array.Sort(indexed, (left, right) =>
        {
            var valueOrder = isTop
                ? right.Value.CompareTo(left.Value)
                : left.Value.CompareTo(right.Value);
            return valueOrder != 0 ? valueOrder : left.Index.CompareTo(right.Index);
        });

        for (var i = 0; i < take; i++)
            result.Add(indexed[i].Key);

        return result;
    }

    /// <summary>
    /// Decide whether <paramref name="normalizedValue"/> is a duplicate (<paramref name="duplicate"/>
    /// = true) or unique (false) within the range, given the occurrence counts of all
    /// non-blank normalized values. Blank values never match, matching the engine.
    /// </summary>
    public static bool MatchesDuplicateState(
        string? normalizedValue,
        IReadOnlyDictionary<string, int> occurrenceCounts,
        bool duplicate)
    {
        ArgumentNullException.ThrowIfNull(occurrenceCounts);

        if (string.IsNullOrEmpty(normalizedValue))
            return false;

        var occurrences = occurrenceCounts.GetValueOrDefault(normalizedValue);
        return duplicate ? occurrences > 1 : occurrences == 1;
    }

    /// <summary>
    /// Build the case-insensitive occurrence-count map used by
    /// <see cref="MatchesDuplicateState"/>. Blank / empty values are skipped. Values are trimmed
    /// before counting, matching the engine's normalization.
    /// </summary>
    public static Dictionary<string, int> BuildOccurrenceCounts(IEnumerable<string?> normalizedValues)
    {
        ArgumentNullException.ThrowIfNull(normalizedValues);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in normalizedValues)
        {
            if (string.IsNullOrEmpty(raw))
                continue;

            var key = raw.Trim();
            if (key.Length == 0)
                continue;

            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return counts;
    }
}
