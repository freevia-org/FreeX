namespace FreeX.Core.Commands;

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
            ?? TryNameAbbreviations(examples)
            ?? TryKnownNameCleanupDerivedPattern(examples)
            ?? TryFullNameEmailPattern(examples)
            ?? TryKnownTitleAndSuffixRemoval(examples)
            ?? TryKnownTitleRemoval(examples)
            ?? TryKnownNameSuffixRemoval(examples)
            ?? TrySplitPascalCaseWords(examples)
            ?? TryEmailDisplayName(examples)
            ?? TryEmailLocalPartWithoutPlusTag(examples)
            ?? TryEmailDomainStem(examples)
            ?? TryDigitMask(examples)
            ?? TryDateNormalization(examples)
            ?? TryPhoneNumberNormalization(examples)
            ?? TryStripThousandSeparators(examples)
            ?? TryExtractDigitsOnly(examples)
            ?? TryExtractFinalDigitRun(examples)
            ?? TryDateComponentExtraction(examples)
            ?? TryThreeTokenNameInitial(examples)
            ?? TryThreeTokenNameDropMiddle(examples)
            ?? TryPairedDelimiterExtraction(examples)
            ?? TryPairedDelimiterRemoval(examples)
            ?? TryLabelValueExtraction(examples)
            ?? TryLabelQualifierRemoval(examples)
            ?? TryDelimitedPartCaseTransform(examples)
            ?? TryDelimitedPartReorder(examples)
            ?? TryFinalWhitespaceToken(examples)
            ?? TryRemoveFinalDottedToken(examples)
            ?? TryExtractFinalDottedToken(examples)
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
            s => (s[0] + "." + s[1]).ToLowerInvariant(),
            s => GetFirstInitial(s[0]) + GetFirstInitial(s[1]),
            s => GetFirstInitial(s[0]) + ". " + s[1],
            s => (GetFirstInitial(s[0]) + s[1]).ToLowerInvariant(),
            s => s[1] + " " + GetFirstInitial(s[0]) + "."
        };

        var emailPatterns = new List<Func<IReadOnlyList<string>, string>>();
        if (TryFirstLastEmailPattern(exampleSources, exampleOutputs) is { } emailPattern)
            emailPatterns.Add(emailPattern);

        if (TryLastFirstEmailPattern(exampleSources, exampleOutputs) is { } lastFirstEmailPattern)
            emailPatterns.Add(lastFirstEmailPattern);

        if (TryFirstInitialLastEmailPattern(exampleSources, exampleOutputs) is { } initialLastEmailPattern)
            emailPatterns.Add(initialLastEmailPattern);

        if (TryFirstLastInitialEmailPattern(exampleSources, exampleOutputs) is { } firstLastInitialEmailPattern)
            emailPatterns.Add(firstLastInitialEmailPattern);

        if (emailPatterns.Count > 0)
            patterns.InsertRange(6, emailPatterns);

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

    private static Func<string, string?>? TryFullNameEmailPattern(
        IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var separator in new[] { '.', '_', '-' })
        {
            var pattern = TrySharedDomainFullNameEmailPattern(
                examples,
                tokens => (tokens[0] + separator + tokens[1]).ToLowerInvariant());
            if (pattern is not null)
                return pattern;
        }

        foreach (var separator in new[] { '.', '_', '-' })
        {
            var pattern = TrySharedDomainFullNameEmailPattern(
                examples,
                tokens => (tokens[1] + separator + tokens[0]).ToLowerInvariant());
            if (pattern is not null)
                return pattern;
        }

        var firstInitialLastPattern = TrySharedDomainFullNameEmailPattern(
            examples,
            tokens => (GetFirstInitial(tokens[0]) + tokens[1]).ToLowerInvariant());
        if (firstInitialLastPattern is not null)
            return firstInitialLastPattern;

        foreach (var separator in new[] { '.', '_', '-' })
        {
            var pattern = TrySharedDomainFullNameEmailPattern(
                examples,
                tokens => (GetFirstInitial(tokens[0]) + separator + tokens[1]).ToLowerInvariant());
            if (pattern is not null)
                return pattern;
        }

        var firstLastInitialPattern = TrySharedDomainFullNameEmailPattern(
            examples,
            tokens => (tokens[0] + GetFirstInitial(tokens[1])).ToLowerInvariant());
        if (firstLastInitialPattern is not null)
            return firstLastInitialPattern;

        foreach (var separator in new[] { '.', '_', '-' })
        {
            var pattern = TrySharedDomainFullNameEmailPattern(
                examples,
                tokens => (tokens[0] + separator + GetFirstInitial(tokens[1])).ToLowerInvariant());
            if (pattern is not null)
                return pattern;
        }

        var lastFirstInitialPattern = TrySharedDomainFullNameEmailPattern(
            examples,
            tokens => (tokens[1] + GetFirstInitial(tokens[0])).ToLowerInvariant());
        if (lastFirstInitialPattern is not null)
            return lastFirstInitialPattern;

        foreach (var separator in new[] { '.', '_', '-' })
        {
            var pattern = TrySharedDomainFullNameEmailPattern(
                examples,
                tokens => (tokens[1] + separator + GetFirstInitial(tokens[0])).ToLowerInvariant());
            if (pattern is not null)
                return pattern;
        }

        return null;
    }

    private static Func<string, string?>? TrySharedDomainFullNameEmailPattern(
        IReadOnlyList<(string Source, string Expected)> examples,
        Func<string[], string> localPart)
    {
        string? domain = null;
        foreach (var (source, expected) in examples)
        {
            if (!TrySplitFullNameEdgeTokens(source, out var tokens))
                return null;

            var expectedPrefix = localPart(tokens) + "@";
            if (!expected.StartsWith(expectedPrefix, StringComparison.Ordinal))
                return null;

            var currentDomain = expected[expectedPrefix.Length..];
            if (string.IsNullOrWhiteSpace(currentDomain) || !currentDomain.Contains('.', StringComparison.Ordinal))
                return null;

            if (domain is null)
                domain = currentDomain;
            else if (!string.Equals(domain, currentDomain, StringComparison.Ordinal))
                return null;
        }

        return domain is null
            ? null
            : source => TrySplitFullNameEdgeTokens(source, out var tokens)
                ? localPart(tokens) + "@" + domain
                : null;
    }

    private static bool TrySplitFullNameEdgeTokens(string source, out string[] tokens)
    {
        var sourceTokens = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (sourceTokens.Length < 2 || sourceTokens.Any(token => token.Length == 0))
        {
            tokens = [];
            return false;
        }

        tokens = [sourceTokens[0], sourceTokens[^1]];
        return true;
    }

    private static Func<IReadOnlyList<string>, string>? TryFirstLastEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs)
    {
        foreach (var separator in new[] { '.', '_', '-' })
        {
            var pattern = TrySharedDomainEmailPattern(
                exampleSources,
                exampleOutputs,
                s => GetEmailNameToken(s, 0) + separator + GetEmailNameToken(s, 1));
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
            s => GetEmailNameInitial(s, 0) + GetEmailNameToken(s, 1));
        if (compactPattern is not null)
            return compactPattern;

        foreach (var separator in new[] { '.', '_', '-' })
        {
            var pattern = TrySharedDomainEmailPattern(
                exampleSources,
                exampleOutputs,
                s => GetEmailNameInitial(s, 0) + separator + GetEmailNameToken(s, 1));
            if (pattern is not null)
                return pattern;
        }

        return null;
    }

    private static Func<IReadOnlyList<string>, string>? TryLastFirstEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs)
    {
        foreach (var separator in new[] { '.', '_', '-' })
        {
            var pattern = TrySharedDomainEmailPattern(
                exampleSources,
                exampleOutputs,
                s => GetEmailNameToken(s, 1) + separator + GetEmailNameToken(s, 0));
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
            s => GetEmailNameToken(s, 1) + GetEmailNameInitial(s, 0));
        if (compactPattern is not null)
            return compactPattern;

        foreach (var separator in new[] { '.', '_', '-' })
        {
            var pattern = TrySharedDomainEmailPattern(
                exampleSources,
                exampleOutputs,
                s => GetEmailNameToken(s, 1) + separator + GetEmailNameInitial(s, 0));
            if (pattern is not null)
                return pattern;
        }

        return null;
    }

    private static Func<IReadOnlyList<string>, string>? TryFirstLastInitialEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs)
    {
        var compactPattern = TrySharedDomainEmailPattern(
            exampleSources,
            exampleOutputs,
            s => GetEmailNameToken(s, 0) + GetEmailNameInitial(s, 1));
        if (compactPattern is not null)
            return compactPattern;

        foreach (var separator in new[] { '.', '_', '-' })
        {
            var pattern = TrySharedDomainEmailPattern(
                exampleSources,
                exampleOutputs,
                s => GetEmailNameToken(s, 0) + separator + GetEmailNameInitial(s, 1));
            if (pattern is not null)
                return pattern;
        }

        return null;
    }

    private static string GetEmailNameToken(IReadOnlyList<string> source, int index) =>
        source[index].Trim().ToLowerInvariant();

    private static string GetEmailNameInitial(IReadOnlyList<string> source, int index)
    {
        var token = source[index].Trim();
        return token.Length == 0 ? string.Empty : char.ToLowerInvariant(token[0]).ToString();
    }

    private static Func<IReadOnlyList<string>, string>? TrySharedDomainEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs,
        Func<IReadOnlyList<string>, string> localPart)
    {
        string? domain = null;
        for (var i = 0; i < exampleSources.Count; i++)
        {
            var expectedLocalPart = localPart(exampleSources[i]);
            if (expectedLocalPart.Length == 0 || expectedLocalPart.Any(char.IsWhiteSpace))
                return null;

            var expectedPrefix = expectedLocalPart + "@";
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
