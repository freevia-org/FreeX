namespace Freexcel.Core.Commands;

/// <summary>
/// Pure, stateless pattern-detection engine for Flash Fill (Ctrl+E).
/// Given training examples (source → expected output), detects a consistent
/// transformation pattern and applies it to the remaining source values.
/// </summary>
public static partial class FlashFillService
{
    /// <summary>
    /// Given training examples (source → expected output), detect a pattern
    /// and apply it to the remaining source values.
    /// Returns null if no consistent pattern can be found.
    /// </summary>
    public static IReadOnlyList<string>? Fill(
        IReadOnlyList<(string Source, string Expected)> examples,
        IReadOnlyList<string> remaining)
    {
        if (examples.Count == 0)
            return null;

        // Try each pattern in priority order.
        // Non-nullable patterns are widened to Func<string, string?> for uniform handling.
        Func<string, string?>? patternFn =
            TryConstant(examples)
            ?? TryCaseTransform(examples)
            ?? TryInitials(examples)
            ?? TryEmailDisplayName(examples)
            ?? TryExtractByDelimiter(examples)
            ?? TryPrefixTrim(examples)
            ?? TrySuffixTrim(examples)
            ?? TryPrefixAdd(examples)
            ?? TrySuffixAdd(examples)
            ?? TrySubstring(examples);

        if (patternFn is null)
            return null;

        var results = new List<string>(remaining.Count);
        foreach (var src in remaining)
        {
            var filled = patternFn(src);
            if (filled is null) return null;
            results.Add(filled);
        }
        return results;
    }

    public static IReadOnlyList<string>? FillFromColumns(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs,
        IReadOnlyList<IReadOnlyList<string>> remainingSources)
    {
        if (exampleSources.Count == 0 || exampleSources.Count != exampleOutputs.Count)
            return null;

        if (exampleSources.Any(s => s.Count < 2) || remainingSources.Any(s => s.Count < 2))
            return null;

        var patterns = new List<Func<IReadOnlyList<string>, string>>
        {
            s => s[0] + " " + s[1],
            s => s[1] + ", " + s[0],
            s => s[0] + "." + s[1],
            s => GetFirstInitial(s[0]) + GetFirstInitial(s[1]),
            s => GetFirstInitial(s[0]) + ". " + s[1],
            s => (GetFirstInitial(s[0]) + s[1]).ToLowerInvariant(),
            s => s[1] + " " + GetFirstInitial(s[0]) + "."
        };

        if (TryFirstLastEmailPattern(exampleSources, exampleOutputs) is { } emailPattern)
            patterns.Insert(6, emailPattern);

        if (TryFirstInitialLastEmailPattern(exampleSources, exampleOutputs) is { } initialLastEmailPattern)
            patterns.Insert(7, initialLastEmailPattern);

        if (TryLastFirstInitialEmailPattern(exampleSources, exampleOutputs) is { } lastInitialEmailPattern)
            patterns.Add(lastInitialEmailPattern);

        foreach (var pattern in patterns)
        {
            var allExamplesMatch = true;
            for (var i = 0; i < exampleSources.Count; i++)
            {
                if (pattern(exampleSources[i]) != exampleOutputs[i])
                {
                    allExamplesMatch = false;
                    break;
                }
            }

            if (!allExamplesMatch)
                continue;

            return remainingSources.Select(pattern).ToList();
        }

        return null;
    }

    // ── Pattern detectors ─────────────────────────────────────────────────────

    private static Func<IReadOnlyList<string>, string>? TryFirstLastEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs)
    {
        foreach (var separator in new[] { '.', '_', '-' })
        {
            var pattern = TrySharedDomainEmailPattern(
                exampleSources,
                exampleOutputs,
                s => (s[0] + separator + s[1]).ToLowerInvariant());
            if (pattern is not null)
                return pattern;
        }

        return null;
    }

    private static Func<IReadOnlyList<string>, string>? TryFirstInitialLastEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs)
    {
        var compactPattern = TrySharedDomainEmailPattern(
            exampleSources,
            exampleOutputs,
            s => (GetFirstInitial(s[0]) + s[1]).ToLowerInvariant());
        if (compactPattern is not null)
            return compactPattern;

        foreach (var separator in new[] { '.', '_', '-' })
        {
            var pattern = TrySharedDomainEmailPattern(
                exampleSources,
                exampleOutputs,
                s => (GetFirstInitial(s[0]) + separator + s[1]).ToLowerInvariant());
            if (pattern is not null)
                return pattern;
        }

        return null;
    }

    private static Func<IReadOnlyList<string>, string>? TryLastFirstInitialEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs)
    {
        var compactPattern = TrySharedDomainEmailPattern(
            exampleSources,
            exampleOutputs,
            s => (s[1] + GetFirstInitial(s[0])).ToLowerInvariant());
        if (compactPattern is not null)
            return compactPattern;

        foreach (var separator in new[] { '.', '_', '-' })
        {
            var pattern = TrySharedDomainEmailPattern(
                exampleSources,
                exampleOutputs,
                s => (s[1] + separator + GetFirstInitial(s[0])).ToLowerInvariant());
            if (pattern is not null)
                return pattern;
        }

        return null;
    }

    private static Func<IReadOnlyList<string>, string>? TrySharedDomainEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs,
        Func<IReadOnlyList<string>, string> localPart)
    {
        string? domain = null;
        for (var i = 0; i < exampleSources.Count; i++)
        {
            var expectedPrefix = localPart(exampleSources[i]) + "@";
            if (!exampleOutputs[i].StartsWith(expectedPrefix, StringComparison.Ordinal))
                return null;

            var currentDomain = exampleOutputs[i][expectedPrefix.Length..];
            if (string.IsNullOrWhiteSpace(currentDomain) || !currentDomain.Contains('.', StringComparison.Ordinal))
                return null;

            if (domain is null)
                domain = currentDomain;
            else if (!string.Equals(domain, currentDomain, StringComparison.Ordinal))
                return null;
        }

        return domain is null
            ? null
            : s => localPart(s) + "@" + domain;
    }
}
