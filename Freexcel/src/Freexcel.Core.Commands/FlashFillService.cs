using System.Globalization;

namespace Freexcel.Core.Commands;

/// <summary>
/// Pure, stateless pattern-detection engine for Flash Fill (Ctrl+E).
/// Given training examples (source → expected output), detects a consistent
/// transformation pattern and applies it to the remaining source values.
/// </summary>
public static class FlashFillService
{
    // Delimiters tried in order for Extract-by-delimiter pattern.
    private static readonly char[] Delimiters = [' ', ',', '-', '_', '@', '/', '\\'];

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

    private static Func<string, string?>? TryConstant(IReadOnlyList<(string Source, string Expected)> examples)
    {
        // Need at least 2 examples to be confident it's truly a constant and not just
        // some other transform on the single available source value.
        if (examples.Count < 2)
            return null;

        var first = examples[0].Expected;
        if (!examples.All(e => e.Expected == first))
            return null;

        // Don't match if the constant is actually a case transform of the source —
        // in that case TryCaseTransform should win.
        bool allUpper = examples.All(e => e.Expected == e.Source.ToUpperInvariant());
        bool allLower = examples.All(e => e.Expected == e.Source.ToLowerInvariant());
        bool allProper = examples.All(e => e.Expected == ToProperCase(e.Source));
        if (allUpper || allLower || allProper)
            return null;

        return _ => first;
    }

    private static Func<string, string?>? TryCaseTransform(IReadOnlyList<(string Source, string Expected)> examples)
    {
        bool isUpper = examples.All(e => e.Expected == e.Source.ToUpperInvariant());
        if (isUpper)
            return s => s.ToUpperInvariant();

        bool isLower = examples.All(e => e.Expected == e.Source.ToLowerInvariant());
        if (isLower)
            return s => s.ToLowerInvariant();

        bool isProper = examples.All(e => e.Expected == ToProperCase(e.Source));
        if (isProper)
            return s => ToProperCase(s);

        return null;
    }

    private static Func<string, string?>? TryExtractByDelimiter(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var delimiter in Delimiters)
        {
            // Find the part index that is consistent across all examples
            // All examples must split into the same number of parts (or at least contain the delimiter)
            // and the example output must equal one specific part index.
            int? partIndex = null;

            bool allMatch = true;
            foreach (var (source, expected) in examples)
            {
                var parts = source.Split(delimiter);
                if (parts.Length < 2)
                {
                    allMatch = false;
                    break;
                }

                int foundIndex = -1;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] == expected)
                    {
                        foundIndex = i;
                        break;
                    }
                }

                if (foundIndex < 0)
                {
                    allMatch = false;
                    break;
                }

                if (partIndex is null)
                    partIndex = foundIndex;
                else if (partIndex != foundIndex)
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch && partIndex is not null)
            {
                var idx = partIndex.Value;
                var d = delimiter;
                return s =>
                {
                    var parts = s.Split(d);
                    return idx < parts.Length ? parts[idx] : s;
                };
            }
        }

        return null;
    }

    private static Func<string, string?>? TryEmailDisplayName(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryFormatDottedEmailUserName(e.Source, out var displayName) && displayName == e.Expected))
            return null;

        return s => TryFormatDottedEmailUserName(s, out var displayName) ? displayName : null;
    }

    private static bool TryFormatDottedEmailUserName(string source, out string displayName)
    {
        displayName = string.Empty;
        var atIndex = source.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0)
            return false;

        var userName = source[..atIndex];
        var parts = userName.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts.Any(part => part.Any(char.IsDigit)))
            return false;

        displayName = string.Join(' ', parts.Select(ToProperCase));
        return true;
    }

    private static Func<string, string?>? TryInitials(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var delimiter in Delimiters)
        {
            if (examples.All(e => TryGetDelimitedInitials(e.Source, delimiter, out var initials) && initials == e.Expected))
            {
                return s => TryGetDelimitedInitials(s, delimiter, out var initials) ? initials : null;
            }
        }

        return null;
    }

    private static Func<string, string?>? TryPrefixTrim(IReadOnlyList<(string Source, string Expected)> examples)
    {
        // Prefix trim: source = prefix + expected  =>  expected = source without leading prefix
        var first = examples[0];

        // Compute candidate prefix length: prefix = source[..^expected.Length]
        if (first.Source.Length <= first.Expected.Length)
            return null;

        int prefixLen = first.Source.Length - first.Expected.Length;
        var prefix = first.Source[..prefixLen];

        // The expected value must be the suffix of source (i.e. source = prefix + expected)
        if (first.Source[prefixLen..] != first.Expected)
            return null;

        // Verify against all other examples
        if (!examples.Skip(1).All(e =>
                e.Source.Length > prefixLen &&
                e.Source[..prefixLen] == prefix &&
                e.Source[prefixLen..] == e.Expected))
            return null;

        return s => s.StartsWith(prefix, StringComparison.Ordinal)
            ? s[prefix.Length..]
            : s;
    }

    private static Func<string, string?>? TrySuffixTrim(IReadOnlyList<(string Source, string Expected)> examples)
    {
        var first = examples[0];
        if (first.Source.Length <= first.Expected.Length)
            return null;

        int suffixLen = first.Source.Length - first.Expected.Length;
        var suffix = first.Source[^suffixLen..];

        // The expected value must be the prefix of source (i.e. source = expected + suffix)
        if (first.Source[..^suffixLen] != first.Expected)
            return null;

        if (!examples.Skip(1).All(e =>
                e.Source.Length > suffixLen &&
                e.Source[^suffixLen..] == suffix &&
                e.Source[..^suffixLen] == e.Expected))
            return null;

        return s => s.EndsWith(suffix, StringComparison.Ordinal)
            ? s[..^suffix.Length]
            : s;
    }

    private static Func<string, string?>? TryPrefixAdd(IReadOnlyList<(string Source, string Expected)> examples)
    {
        // Prefix add: expected = prefix + source
        var first = examples[0];

        if (first.Expected.Length <= first.Source.Length)
            return null;

        // Expected must end with source
        if (!first.Expected.EndsWith(first.Source, StringComparison.Ordinal))
            return null;

        int prefixLen = first.Expected.Length - first.Source.Length;
        var prefix = first.Expected[..prefixLen];

        if (!examples.Skip(1).All(e =>
                e.Expected.Length > e.Source.Length &&
                e.Expected.Length - e.Source.Length == prefixLen &&
                e.Expected[..prefixLen] == prefix &&
                e.Expected[prefixLen..] == e.Source))
            return null;

        return s => prefix + s;
    }

    private static Func<string, string?>? TrySuffixAdd(IReadOnlyList<(string Source, string Expected)> examples)
    {
        var first = examples[0];
        // suffix add means: expected = source + suffix
        if (!first.Expected.StartsWith(first.Source, StringComparison.Ordinal))
            return null;

        int suffixLen = first.Expected.Length - first.Source.Length;
        if (suffixLen <= 0)
            return null;

        var suffix = first.Expected[first.Source.Length..];

        if (!examples.Skip(1).All(e =>
                e.Expected.StartsWith(e.Source, StringComparison.Ordinal) &&
                e.Expected.Length - e.Source.Length == suffixLen &&
                e.Expected[e.Source.Length..] == suffix))
            return null;

        return s => s + suffix;
    }

    private static Func<string, string?>? TrySubstring(IReadOnlyList<(string Source, string Expected)> examples)
    {
        var first = examples[0];
        int sourceLen = first.Source.Length;
        int expectedLen = first.Expected.Length;

        if (expectedLen == 0 || expectedLen >= sourceLen)
            return null;

        // Find the start index of expected within source
        int startIndex = first.Source.IndexOf(first.Expected, StringComparison.Ordinal);
        if (startIndex < 0)
            return null;

        // Verify that the same startIndex and length work for all examples
        if (!examples.Skip(1).All(e =>
        {
            if (e.Expected.Length != expectedLen)
                return false;
            if (e.Source.Length < startIndex + expectedLen)
                return false;
            return e.Source.Substring(startIndex, expectedLen) == e.Expected;
        }))
            return null;

        return s => s.Length >= startIndex + expectedLen
            ? s.Substring(startIndex, expectedLen)
            : null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ToProperCase(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return textInfo.ToTitleCase(s.ToLowerInvariant());
    }

    private static bool TryGetDelimitedInitials(string source, char delimiter, out string initials)
    {
        var parts = source.Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            initials = string.Empty;
            return false;
        }

        initials = string.Concat(parts.Select(GetFirstInitial));
        return true;
    }

    private static string GetFirstInitial(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value[0].ToString();

    private static Func<IReadOnlyList<string>, string>? TryFirstLastEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs)
    {
        return TrySharedDomainEmailPattern(
            exampleSources,
            exampleOutputs,
            s => (s[0] + "." + s[1]).ToLowerInvariant());
    }

    private static Func<IReadOnlyList<string>, string>? TryFirstInitialLastEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs)
    {
        return TrySharedDomainEmailPattern(
            exampleSources,
            exampleOutputs,
            s => (GetFirstInitial(s[0]) + s[1]).ToLowerInvariant());
    }

    private static Func<IReadOnlyList<string>, string>? TryLastFirstInitialEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs)
    {
        return TrySharedDomainEmailPattern(
            exampleSources,
            exampleOutputs,
            s => (s[1] + GetFirstInitial(s[0])).ToLowerInvariant());
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
