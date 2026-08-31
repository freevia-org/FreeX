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
        var exactIndex = -1;
        var hasPrefix = false;
        var hasLongerPrefix = false;
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var keyTip = RibbonKeyTipText.Normalize(keyTipSelector(candidate));
            if (!enabledSelector(candidate) || keyTip is null)
                continue;

            var startsWithSequence = keyTip.StartsWith(normalizedSequence, StringComparison.OrdinalIgnoreCase);
            hasPrefix |= startsWithSequence;
            if (exactIndex < 0 &&
                string.Equals(keyTip, normalizedSequence, StringComparison.OrdinalIgnoreCase))
            {
                exactIndex = index;
            }

            if (!hasLongerPrefix &&
                startsWithSequence &&
                keyTip.Length > normalizedSequence.Length &&
                longerPrefixSelector(candidate))
            {
                hasLongerPrefix = true;
            }
        }

        if (exactIndex >= 0 && !hasLongerPrefix)
            return new(RibbonKeyTipResolutionKind.Exact, exactIndex);

        if (hasPrefix)
            return new(RibbonKeyTipResolutionKind.Prefix);

        return new(RibbonKeyTipResolutionKind.NoMatch);
    }
}
