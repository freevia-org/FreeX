namespace Free.Shared.Ribbon.KeyTips;

public enum RibbonKeyTipResolutionKind
{
    NoMatch,
    Prefix,
    Exact,
}

public readonly record struct RibbonKeyTipResolution(
    RibbonKeyTipResolutionKind Kind,
    int ExactIndex = -1);

/// <summary>
/// Applies the WPF key-tip rule that an exact enabled leaf is immediate unless the same
/// sequence is also an enabled prefix for a longer key tip.
/// </summary>
public static class RibbonKeyTipResolutionPlanner
{
    public static RibbonKeyTipResolution Resolve<T>(
        IReadOnlyList<T> candidates,
        string? sequence,
        Func<T, string?> keyTipSelector,
        Func<T, bool>? enabledSelector = null,
        Func<T, bool>? longerPrefixSelector = null)
    {
        var normalizedSequence = RibbonKeyTipText.Normalize(sequence);
        if (normalizedSequence is null)
            return new(RibbonKeyTipResolutionKind.NoMatch);

        enabledSelector ??= static _ => true;
        longerPrefixSelector ??= static _ => true;
        var normalized = candidates
            .Select((candidate, index) =>
                (candidate, index, keyTip: RibbonKeyTipText.Normalize(keyTipSelector(candidate))))
            .Where(entry => enabledSelector(entry.candidate) && entry.keyTip is not null)
            .ToArray();

        var exact = normalized.FirstOrDefault(entry =>
            string.Equals(entry.keyTip, normalizedSequence, StringComparison.OrdinalIgnoreCase));
        var hasLongerPrefix = normalized.Any(entry =>
            longerPrefixSelector(entry.candidate) &&
            entry.keyTip!.Length > normalizedSequence.Length &&
            entry.keyTip.StartsWith(normalizedSequence, StringComparison.OrdinalIgnoreCase));

        if (exact.keyTip is not null && !hasLongerPrefix)
            return new(RibbonKeyTipResolutionKind.Exact, exact.index);

        if (hasLongerPrefix || normalized.Any(entry =>
                entry.keyTip!.StartsWith(normalizedSequence, StringComparison.OrdinalIgnoreCase)))
            return new(RibbonKeyTipResolutionKind.Prefix);

        return new(RibbonKeyTipResolutionKind.NoMatch);
    }
}
