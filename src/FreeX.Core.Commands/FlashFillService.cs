namespace FreeX.Core.Commands;

/// <summary>
/// Pure, stateless pattern-detection engine for Flash Fill (Ctrl+E).
/// Given training examples (source → expected output), detects a consistent
/// transformation pattern and applies it to the remaining source values.
/// </summary>
public static partial class FlashFillService
{
    private const string EmailSeparators = "._-";

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
            TryTimeComponentExtraction(examples)
            ?? TryEmbeddedTimeComponentExtraction(examples)
            ?? TryEmbeddedTimeRangeEndpointExtraction(examples)
            ?? TryConstant(examples)
            ?? TryCaseTransform(examples)
            ?? TryInitials(examples)
            ?? TryNameAbbreviations(examples)
            ?? TryKnownNameCleanupDerivedPattern(examples)
            ?? TryFullNameMiddleInitialEmailPattern(examples)
            ?? TryFullNameEmailPattern(examples)
            ?? TryKnownTitleAndSuffixRemoval(examples)
            ?? TryKnownTitleRemoval(examples)
            ?? TryKnownNameSuffixRemoval(examples)
            ?? TryKnownOrganizationSuffixRemoval(examples)
            ?? TrySplitPascalCaseWords(examples)
            ?? TryEmailDisplayName(examples)
            ?? TryEmailLocalPartWithoutPlusTag(examples)
            ?? TryEmailDomainStem(examples)
            ?? TryEmailDomainSuffix(examples)
            ?? TryWebAddressCleanup(examples)
            ?? TryUrlTrackingQueryCleanup(examples)
            ?? TryExtractFinalUrlPathSegmentStem(examples)
            ?? TryExtractFinalUrlPathSegmentRawSlugStem(examples)
            ?? TryExtractFinalUrlPathSegmentSlugTitle(examples)
            ?? TryExtractParentUrlPathSegment(examples)
            ?? TryExtractFirstUrlPathSegment(examples)
            ?? TryExtractSecondUrlPathSegment(examples)
            ?? TryExtractParentUrlPathSegmentTitle(examples)
            ?? TryExtractFirstUrlPathSegmentTitle(examples)
            ?? TryExtractSecondUrlPathSegmentTitle(examples)
            ?? TryUrlQueryParameterValue(examples)
            ?? TryDigitMask(examples)
            ?? TryDateNormalization(examples)
            ?? TryEmbeddedDateExtraction(examples)
            ?? TryEmbeddedDateComponentExtraction(examples)
            ?? TryTimeNormalization(examples)
            ?? TryEmbeddedTimeExtraction(examples)
            ?? TryPhoneNumberNormalization(examples)
            ?? TryUsAddressComponentExtraction(examples)
            ?? TryStripThousandSeparators(examples)
            ?? TryExtractDigitsOnly(examples)
            ?? TryDateComponentExtraction(examples)
            ?? TryExtractFinalDigitRun(examples)
            ?? TryThreeTokenNameInitial(examples)
            ?? TryThreeTokenNameDropMiddle(examples)
            ?? TryPairedDelimiterExtraction(examples)
            ?? TryPairedDelimiterRemoval(examples)
            ?? TryLabelValueExtraction(examples)
            ?? TryLabelQualifierRemoval(examples)
            ?? TryDelimitedPartCaseTransform(examples)
            ?? TryDelimitedPartReorder(examples)
            ?? TryFinalWhitespaceToken(examples)
            ?? TryExtractFinalPathSegmentStem(examples)
            ?? TryExtractFileParentDirectoryName(examples)
            ?? TryExtractFileParentDirectoryTitle(examples)
            ?? TryRemoveFinalDottedToken(examples)
            ?? TryExtractFinalDottedToken(examples)
            ?? TryRemoveLeadingDottedToken(examples)
            ?? TryExtractMiddleDottedToken(examples)
            ?? TryExtractFirstDottedToken(examples)
            ?? TryRemoveMiddleDottedToken(examples)
            ?? TryRemoveLeadingDelimitedToken(examples)
            ?? TryRemoveFinalDelimitedToken(examples)
            ?? TryExtractFinalDelimitedToken(examples)
            ?? TryExtractPenultimateDelimitedToken(examples)
            ?? TryRemoveMiddleDelimitedToken(examples)
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

        for (var i = 0; i < exampleSources.Count; i++)
        {
            if (exampleSources[i].Count < 2)
                return null;
        }

        for (var i = 0; i < remainingSources.Count; i++)
        {
            if (remainingSources[i].Count < 2)
                return null;
        }

        if (TryFillFromThreeNameColumns(exampleSources, exampleOutputs, remainingSources) is { } threeColumnResults)
            return threeColumnResults;

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

            var results = new List<string>(remainingSources.Count);
            for (var i = 0; i < remainingSources.Count; i++)
                results.Add(pattern(remainingSources[i]));

            return results;
        }

        return null;
    }

    // ── Pattern detectors ─────────────────────────────────────────────────────

    private static IReadOnlyList<string>? TryFillFromThreeNameColumns(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs,
        IReadOnlyList<IReadOnlyList<string>> remainingSources)
    {
        if (!HasThreeNameSources(exampleSources) || !HasThreeNameSources(remainingSources))
            return null;

        var emailPatterns = new List<Func<IReadOnlyList<string>, string>>();
        if (TryThreeColumnFirstLastEmailPattern(exampleSources, exampleOutputs) is { } emailPattern)
            emailPatterns.Add(emailPattern);

        if (TryThreeColumnFirstMiddleInitialLastEmailPattern(exampleSources, exampleOutputs) is { } middleInitialEmailPattern)
            emailPatterns.Add(middleInitialEmailPattern);

        if (TryThreeColumnFirstInitialLastEmailPattern(exampleSources, exampleOutputs) is { } initialLastEmailPattern)
            emailPatterns.Add(initialLastEmailPattern);

        if (TryThreeColumnFirstLastInitialEmailPattern(exampleSources, exampleOutputs) is { } firstLastInitialEmailPattern)
            emailPatterns.Add(firstLastInitialEmailPattern);

        var patterns = new List<Func<IReadOnlyList<string>, string>>
        {
            s => GetNameToken(s, 0) + " " + GetNameToken(s, 1) + " " + GetNameToken(s, 2),
            s => GetNameToken(s, 2) + ", " + GetNameToken(s, 0) + " " + GetNameToken(s, 1),
            s => GetNameToken(s, 0) + " " + GetNameInitial(s, 1) + ". " + GetNameToken(s, 2),
            s => GetNameInitial(s, 0) + ". " + GetNameInitial(s, 1) + ". " + GetNameToken(s, 2),
            s => GetNameToken(s, 2) + ", " + GetNameToken(s, 0) + " " + GetNameInitial(s, 1) + ".",
            s => GetNameToken(s, 2) + ", " + GetNameInitial(s, 0) + ". " + GetNameInitial(s, 1) + ".",
            s => GetNameToken(s, 0) + " " + GetNameToken(s, 1) + " " + GetNameInitial(s, 2) + ".",
            s => GetNameInitial(s, 0) + ". " + GetNameInitial(s, 1) + ". " + GetNameInitial(s, 2) + "."
        };
        if (emailPatterns.Count > 0)
            patterns.InsertRange(0, emailPatterns);

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

            var results = new List<string>(remainingSources.Count);
            for (var i = 0; i < remainingSources.Count; i++)
                results.Add(pattern(remainingSources[i]));

            return results;
        }

        return null;
    }

    private static Func<IReadOnlyList<string>, string>? TryThreeColumnFirstLastEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs)
    {
        foreach (var separator in EmailSeparators)
        {
            var pattern = TrySharedDomainEmailPattern(
                exampleSources,
                exampleOutputs,
                s => GetEmailNameToken(s, 0) + separator + GetEmailNameToken(s, 2),
                domain => s => CreateLowerTokenPairEmail(s, 0, separator, 2, domain));
            if (pattern is not null)
                return pattern;
        }

        return null;
    }

    private static Func<IReadOnlyList<string>, string>? TryThreeColumnFirstMiddleInitialLastEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs)
    {
        foreach (var separator in EmailSeparators)
        {
            var pattern = TrySharedDomainEmailPattern(
                exampleSources,
                exampleOutputs,
                s => GetEmailNameToken(s, 0) + separator + GetEmailNameInitial(s, 1) + separator + GetEmailNameToken(s, 2));
            if (pattern is not null)
                return pattern;
        }

        var firstMiddleInitialLastPattern = TrySharedDomainEmailPattern(
            exampleSources,
            exampleOutputs,
            s => GetEmailNameToken(s, 0) + GetEmailNameInitial(s, 1) + GetEmailNameToken(s, 2));
        if (firstMiddleInitialLastPattern is not null)
            return firstMiddleInitialLastPattern;

        var firstInitialMiddleInitialLastPattern = TrySharedDomainEmailPattern(
            exampleSources,
            exampleOutputs,
            s => GetEmailNameInitial(s, 0) + GetEmailNameInitial(s, 1) + GetEmailNameToken(s, 2));
        if (firstInitialMiddleInitialLastPattern is not null)
            return firstInitialMiddleInitialLastPattern;

        return null;
    }

    private static Func<IReadOnlyList<string>, string>? TryThreeColumnFirstInitialLastEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs)
    {
        return TrySharedDomainEmailPattern(
            exampleSources,
            exampleOutputs,
            s => GetEmailNameInitial(s, 0) + GetEmailNameToken(s, 2));
    }

    private static Func<IReadOnlyList<string>, string>? TryThreeColumnFirstLastInitialEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs)
    {
        return TrySharedDomainEmailPattern(
            exampleSources,
            exampleOutputs,
            s => GetEmailNameToken(s, 0) + GetEmailNameInitial(s, 2));
    }

    private static bool HasThreeNameSources(IReadOnlyList<IReadOnlyList<string>> sources)
    {
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i].Count < 3 ||
                string.IsNullOrWhiteSpace(sources[i][0]) ||
                string.IsNullOrWhiteSpace(sources[i][1]) ||
                string.IsNullOrWhiteSpace(sources[i][2]))
                return false;
        }

        return true;
    }

    private static string GetNameToken(IReadOnlyList<string> source, int index) =>
        source[index].Trim();

    private static string GetNameInitial(IReadOnlyList<string> source, int index) =>
        GetFirstInitial(GetNameToken(source, index));

    private static Func<string, string?>? TryFullNameEmailPattern(
        IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var separator in EmailSeparators)
        {
            var pattern = TrySharedDomainFullNameEmailPattern(
                examples,
                tokens => (tokens[0] + separator + tokens[1]).ToLowerInvariant());
            if (pattern is not null)
                return pattern;
        }

        foreach (var separator in EmailSeparators)
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

        foreach (var separator in EmailSeparators)
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

        foreach (var separator in EmailSeparators)
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

        foreach (var separator in EmailSeparators)
        {
            var pattern = TrySharedDomainFullNameEmailPattern(
                examples,
                tokens => (tokens[1] + separator + GetFirstInitial(tokens[0])).ToLowerInvariant());
            if (pattern is not null)
                return pattern;
        }

        return null;
    }

    private static Func<string, string?>? TryFullNameMiddleInitialEmailPattern(
        IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var separator in EmailSeparators)
        {
            var pattern = TrySharedDomainThreeTokenFullNameEmailPattern(
                examples,
                tokens => (tokens[0] + separator + GetFirstInitial(tokens[1]) + separator + tokens[2]).ToLowerInvariant());
            if (pattern is not null)
                return pattern;
        }

        var firstMiddleInitialLastPattern = TrySharedDomainThreeTokenFullNameEmailPattern(
            examples,
            tokens => (tokens[0] + GetFirstInitial(tokens[1]) + tokens[2]).ToLowerInvariant());
        if (firstMiddleInitialLastPattern is not null)
            return firstMiddleInitialLastPattern;

        var firstInitialMiddleInitialLastPattern = TrySharedDomainThreeTokenFullNameEmailPattern(
            examples,
            tokens => (GetFirstInitial(tokens[0]) + GetFirstInitial(tokens[1]) + tokens[2]).ToLowerInvariant());
        if (firstInitialMiddleInitialLastPattern is not null)
            return firstInitialMiddleInitialLastPattern;

        foreach (var separator in EmailSeparators)
        {
            var pattern = TrySharedDomainThreeTokenFullNameEmailPattern(
                examples,
                tokens => (tokens[2] + separator + tokens[0] + separator + GetFirstInitial(tokens[1])).ToLowerInvariant());
            if (pattern is not null)
                return pattern;
        }

        var lastFirstInitialMiddleInitialPattern = TrySharedDomainThreeTokenFullNameEmailPattern(
            examples,
            tokens => (tokens[2] + GetFirstInitial(tokens[0]) + GetFirstInitial(tokens[1])).ToLowerInvariant());
        if (lastFirstInitialMiddleInitialPattern is not null)
            return lastFirstInitialMiddleInitialPattern;

        var lastMiddleInitialFirstInitialPattern = TrySharedDomainThreeTokenFullNameEmailPattern(
            examples,
            tokens => (tokens[2] + GetFirstInitial(tokens[1]) + GetFirstInitial(tokens[0])).ToLowerInvariant());
        if (lastMiddleInitialFirstInitialPattern is not null)
            return lastMiddleInitialFirstInitialPattern;

        return null;
    }

    private static Func<string, string?>? TrySharedDomainThreeTokenFullNameEmailPattern(
        IReadOnlyList<(string Source, string Expected)> examples,
        Func<string[], string> localPart)
    {
        string? domain = null;
        foreach (var (source, expected) in examples)
        {
            if (!TrySplitThreeTokenFullName(source, out var tokens))
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
            : source => TrySplitThreeTokenFullName(source, out var tokens)
                ? localPart(tokens) + "@" + domain
                : null;
    }

    private static bool TrySplitThreeTokenFullName(string source, out string[] tokens)
    {
        var sourceTokens = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (sourceTokens.Length != 3 || sourceTokens.Any(token => token.Length == 0))
        {
            tokens = [];
            return false;
        }

        tokens = [sourceTokens[0], sourceTokens[1], sourceTokens[2]];
        return true;
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
        foreach (var separator in EmailSeparators)
        {
            var pattern = TrySharedDomainEmailPattern(
                exampleSources,
                exampleOutputs,
                s => GetEmailNameToken(s, 0) + separator + GetEmailNameToken(s, 1),
                domain => s => CreateLowerTokenPairEmail(s, 0, separator, 1, domain));
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

        foreach (var separator in EmailSeparators)
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
        foreach (var separator in EmailSeparators)
        {
            var pattern = TrySharedDomainEmailPattern(
                exampleSources,
                exampleOutputs,
                s => GetEmailNameToken(s, 1) + separator + GetEmailNameToken(s, 0),
                domain => s => CreateLowerTokenPairEmail(s, 1, separator, 0, domain));
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

        foreach (var separator in EmailSeparators)
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

        foreach (var separator in EmailSeparators)
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

    private static string CreateLowerTokenPairEmail(
        IReadOnlyList<string> source,
        int firstIndex,
        char separator,
        int secondIndex,
        string domain)
    {
        var first = source[firstIndex];
        var second = source[secondIndex];
        GetTrimmedRange(first, out var firstStart, out var firstLength);
        GetTrimmedRange(second, out var secondStart, out var secondLength);

        var localLength = firstLength + 1 + secondLength;
        var state = new LowerTokenPairEmailState(
            first,
            firstStart,
            firstLength,
            second,
            secondStart,
            secondLength,
            separator,
            domain);
        return string.Create(
            localLength + 1 + domain.Length,
            state,
            static (destination, state) =>
            {
                CopyLowerInvariant(
                    state.First.AsSpan(state.FirstStart, state.FirstLength),
                    destination);
                destination[state.FirstLength] = state.Separator;

                var secondOffset = state.FirstLength + 1;
                CopyLowerInvariant(
                    state.Second.AsSpan(state.SecondStart, state.SecondLength),
                    destination[secondOffset..]);

                var atOffset = secondOffset + state.SecondLength;
                destination[atOffset] = '@';
                state.Domain.AsSpan().CopyTo(destination[(atOffset + 1)..]);
            });
    }

    private static void GetTrimmedRange(string value, out int start, out int length)
    {
        start = 0;
        var end = value.Length - 1;
        while (start <= end && char.IsWhiteSpace(value[start]))
            start++;

        while (end >= start && char.IsWhiteSpace(value[end]))
            end--;

        length = end - start + 1;
    }

    private static void CopyLowerInvariant(ReadOnlySpan<char> source, Span<char> destination)
    {
        source.ToLowerInvariant(destination);
    }

    private static Func<IReadOnlyList<string>, string>? TrySharedDomainEmailPattern(
        IReadOnlyList<IReadOnlyList<string>> exampleSources,
        IReadOnlyList<string> exampleOutputs,
        Func<IReadOnlyList<string>, string> localPart,
        Func<string, Func<IReadOnlyList<string>, string>>? resultFactory = null)
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
            : resultFactory?.Invoke(domain) ?? (s => localPart(s) + "@" + domain);
    }

    private readonly struct LowerTokenPairEmailState(
        string first,
        int firstStart,
        int firstLength,
        string second,
        int secondStart,
        int secondLength,
        char separator,
        string domain)
    {
        public string First { get; } = first;
        public int FirstStart { get; } = firstStart;
        public int FirstLength { get; } = firstLength;
        public string Second { get; } = second;
        public int SecondStart { get; } = secondStart;
        public int SecondLength { get; } = secondLength;
        public char Separator { get; } = separator;
        public string Domain { get; } = domain;
    }
}
