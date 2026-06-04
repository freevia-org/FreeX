using static FreeX.Core.Commands.FlashFillTextPrimitives;

namespace FreeX.Core.Commands;

public static partial class FlashFillService
{
    private delegate bool NameCleaner(string source, out string name);

    private enum DatePartKind
    {
        Year,
        Month,
        Day
    }

    private enum UsAddressComponentKind
    {
        Street,
        StreetNumber,
        StreetName,
        StreetWithoutUnit,
        UnitSuffix,
        UnitIdentifier,
        City,
        State,
        Zip5,
        Zip,
        Zip4,
        StateZip
    }

    private enum WebAddressOutputKind
    {
        Host,
        HostWithoutWww,
        DomainStem
    }

    private readonly record struct DateParts(int Year, int Month, int Day);

    private readonly record struct UsAddressParts(string Street, string City, string State, string Zip);

    private readonly record struct WebAddressParts(string Host, string HostWithoutWww, string DomainStem);

    private readonly record struct DateOutputPattern(
        DatePartKind First,
        DatePartKind Second,
        DatePartKind Third,
        char Separator,
        int YearWidth,
        int MonthWidth,
        int DayWidth);

    private sealed class ExtractedSegmentCache
    {
        private const int MaxCachedSegments = 16;
        private List<string>? _segments;

        public string GetOrAdd(string source, int start, int endExclusive)
        {
            var length = endExclusive - start;
            if (length == 0)
                return string.Empty;

            var span = source.AsSpan(start, length);
            if (_segments is { } segments)
            {
                for (var i = 0; i < segments.Count; i++)
                {
                    var segment = segments[i];
                    if (segment.Length == length && span.SequenceEqual(segment.AsSpan()))
                        return segment;
                }
            }
            else
            {
                _segments = segments = new List<string>(4);
            }

            var extracted = SliceSegment(source, start, endExclusive);
            if (segments.Count < MaxCachedSegments)
                segments.Add(extracted);

            return extracted;
        }
    }

    // Delimiters tried in order for extract-by-delimiter, token casing, and initials patterns.
    private static readonly char[] Delimiters = [' ', ',', ';', ':', '|', '-', '_', '@', '.', '/', '\\'];
    private static readonly char[] FinalDelimitedTokenDelimiters = [',', ';', ':', '|', '-', '_', '/', '\\'];
    private static readonly char[] DateComponentSeparators = ['/', '-', '.'];
    private static readonly string[] LabelValueSeparators = [":", "=", "->", "=>", "-", "/", "|"];
    private static readonly UsAddressComponentKind[] UsAddressComponentKinds =
    [
        UsAddressComponentKind.Street,
        UsAddressComponentKind.StreetNumber,
        UsAddressComponentKind.StreetName,
        UsAddressComponentKind.StreetWithoutUnit,
        UsAddressComponentKind.UnitSuffix,
        UsAddressComponentKind.UnitIdentifier,
        UsAddressComponentKind.City,
        UsAddressComponentKind.State,
        UsAddressComponentKind.Zip5,
        UsAddressComponentKind.Zip,
        UsAddressComponentKind.Zip4,
        UsAddressComponentKind.StateZip
    ];
    private static readonly HashSet<string> KnownNameTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mr",
        "Mrs",
        "Ms",
        "Miss",
        "Dr",
        "Prof",
        "Professor",
        "Sir",
        "Dame"
    };
    private static readonly HashSet<string> KnownNameSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Jr",
        "Junior",
        "Sr",
        "Senior",
        "II",
        "III",
        "IV",
        "V",
        "PhD",
        "Ph.D",
        "MD",
        "M.D",
        "CPA",
        "C.P.A",
        "MBA",
        "M.B.A",
        "DDS",
        "D.D.S",
        "DVM",
        "D.V.M",
        "Esq"
    };
    private static readonly HashSet<string> KnownOrganizationSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Inc",
        "Incorporated",
        "LLC",
        "L.L.C",
        "Ltd",
        "Limited",
        "Corp",
        "Corporation",
        "Co",
        "Company",
        "PLC",
        "LLP",
        "LP",
        "GmbH",
        "AG",
        "SA",
        "S.A",
        "BV",
        "NV",
        "Pty"
    };

    private static readonly (char Open, char Close)[] PairedDelimiters =
        [('(', ')'), ('[', ']'), ('{', '}'), ('"', '"'), ('\'', '\''), ('<', '>')];

    private static Func<string, string?>? TryConstant(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (examples.Count < 2)
            return null;

        var first = examples[0].Expected;
        if (!examples.All(e => e.Expected == first))
            return null;

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
            int? partIndex = null;

            bool allMatch = true;
            foreach (var (source, expected) in examples)
            {
                if (source.IndexOf(delimiter) < 0)
                {
                    allMatch = false;
                    break;
                }

                if (!TryFindDelimitedPartIndex(source, delimiter, expected, out var foundIndex))
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
                    TryGetDelimitedPart(s, d, idx, out var part)
                        ? part
                        : null;
            }
        }

        return null;
    }

    private static Func<string, string?>? TryExtractFinalDottedToken(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryGetFinalDottedToken(e.Source, out var token) && token == e.Expected))
            return null;

        var cache = new ExtractedSegmentCache();
        return source => TryGetFinalDottedTokenRange(source, out var start, out var endExclusive)
            ? cache.GetOrAdd(source, start, endExclusive)
            : null;
    }

    private static Func<string, string?>? TryExtractFinalDelimitedToken(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var delimiter in FinalDelimitedTokenDelimiters)
        {
            if (!examples.All(e => TryGetFinalDelimitedToken(e.Source, delimiter, out var token) && token == e.Expected))
                continue;

            var d = delimiter;
            var cache = new ExtractedSegmentCache();
            return source => TryGetFinalDelimitedTokenRange(source, d, out var start, out var endExclusive)
                ? cache.GetOrAdd(source, start, endExclusive)
                : null;
        }

        return null;
    }

    private static Func<string, string?>? TryExtractPenultimateDelimitedToken(
        IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var delimiter in FinalDelimitedTokenDelimiters)
        {
            if (!examples.All(e => TryGetPenultimateDelimitedToken(e.Source, delimiter, out var token) && token == e.Expected))
                continue;

            var d = delimiter;
            var cache = new ExtractedSegmentCache();
            return source => TryGetPenultimateDelimitedTokenRange(source, d, out var start, out var endExclusive)
                ? cache.GetOrAdd(source, start, endExclusive)
                : null;
        }

        return null;
    }

    private static Func<string, string?>? TryRemoveFinalDottedToken(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryRemoveFinalDottedToken(e.Source, out var stem) && stem == e.Expected))
            return null;

        return source => TryRemoveFinalDottedToken(source, out var stem) ? stem : null;
    }

    private static Func<string, string?>? TryRemoveFinalDelimitedToken(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var delimiter in FinalDelimitedTokenDelimiters)
        {
            if (!examples.All(e => TryRemoveFinalDelimitedToken(e.Source, delimiter, out var stem) && stem == e.Expected))
                continue;

            return source => TryRemoveFinalDelimitedToken(source, delimiter, out var stem) ? stem : null;
        }

        return null;
    }

    private static Func<string, string?>? TryExtractFinalPathSegmentStem(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryGetFinalPathSegmentStem(e.Source, out var stem) && stem == e.Expected))
            return null;

        var cache = new ExtractedSegmentCache();
        return source => TryGetFinalPathSegmentStemRange(source, out var start, out var endExclusive)
            ? cache.GetOrAdd(source, start, endExclusive)
            : null;
    }

    private static bool TryRemoveFinalDottedToken(string source, out string stem)
    {
        stem = string.Empty;
        var lastDotIndex = source.LastIndexOf('.');
        if (lastDotIndex <= 0 || lastDotIndex == source.Length - 1)
            return false;

        stem = source[..lastDotIndex].Trim();
        return stem.Length > 0;
    }

    private static bool TryRemoveFinalDelimitedToken(string source, char delimiter, out string stem)
    {
        stem = string.Empty;
        var lastDelimiterIndex = source.LastIndexOf(delimiter);
        if (lastDelimiterIndex <= 0 || lastDelimiterIndex == source.Length - 1)
            return false;

        stem = source[..lastDelimiterIndex].Trim();
        return stem.Length > 0;
    }

    private static bool TryGetFinalDottedToken(string source, out string token)
    {
        token = string.Empty;
        if (!TryGetFinalDottedTokenRange(source, out var tokenStart, out var tokenEndExclusive))
            return false;

        token = SliceSegment(source, tokenStart, tokenEndExclusive);
        return true;
    }

    private static bool TryGetFinalDottedTokenRange(string source, out int tokenStart, out int tokenEndExclusive)
    {
        tokenStart = 0;
        tokenEndExclusive = 0;
        var end = source.Length - 1;
        TrimTrailingWhitespace(source, ref end);

        while (end >= 0)
        {
            var dotIndex = source.LastIndexOf('.', end);
            if (dotIndex < 0)
                return false;

            var currentTokenStart = dotIndex + 1;
            var currentTokenEnd = end;
            TrimSegment(source, ref currentTokenStart, ref currentTokenEnd);
            if (currentTokenStart <= currentTokenEnd && HasNonEmptyPartBeforeDelimiter(source, dotIndex, '.'))
            {
                tokenStart = currentTokenStart;
                tokenEndExclusive = currentTokenEnd + 1;
                return true;
            }

            end = dotIndex - 1;
            TrimTrailingWhitespace(source, ref end);
        }

        return false;
    }

    private static bool TryGetFinalPathSegmentStem(string source, out string stem)
    {
        stem = string.Empty;
        if (!TryGetFinalPathSegmentStemRange(source, out var start, out var endExclusive))
            return false;

        stem = SliceSegment(source, start, endExclusive);
        return true;
    }

    private static bool TryGetFinalPathSegmentStemRange(string source, out int stemStart, out int stemEndExclusive)
    {
        stemStart = 0;
        stemEndExclusive = 0;
        if (IsHttpOrHttpsUrlCandidate(source))
            return false;

        var lastSeparatorIndex = Math.Max(source.LastIndexOf('/'), source.LastIndexOf('\\'));
        if (lastSeparatorIndex < 0 || lastSeparatorIndex == source.Length - 1)
            return false;

        var segmentStart = lastSeparatorIndex + 1;
        var segmentEnd = source.Length - 1;
        TrimSegment(source, ref segmentStart, ref segmentEnd);
        if (segmentStart > segmentEnd)
            return false;

        var segmentLength = segmentEnd - segmentStart + 1;
        var dotIndex = source.LastIndexOf('.', segmentEnd, segmentLength);
        if (dotIndex <= segmentStart || dotIndex == segmentEnd)
            return false;

        stemStart = segmentStart;
        var stemEnd = dotIndex - 1;
        TrimSegment(source, ref stemStart, ref stemEnd);
        if (stemStart > stemEnd)
            return false;

        stemEndExclusive = stemEnd + 1;
        return true;
    }

    private static bool TryFindDelimitedPartIndex(
        string source,
        char delimiter,
        string expected,
        out int foundIndex)
    {
        foundIndex = -1;
        var partIndex = 0;
        var start = 0;
        while (true)
        {
            var delimiterIndex = source.IndexOf(delimiter, start);
            var endExclusive = delimiterIndex < 0 ? source.Length : delimiterIndex;
            if (TrimmedSegmentEquals(source, start, endExclusive, expected))
            {
                foundIndex = partIndex;
                return true;
            }

            if (delimiterIndex < 0)
                return false;

            start = delimiterIndex + 1;
            partIndex++;
        }
    }

    private static bool TryGetDelimitedPart(string source, char delimiter, int partIndex, out string part)
    {
        part = string.Empty;
        if (partIndex < 0)
            return false;

        var currentPartIndex = 0;
        var start = 0;
        while (true)
        {
            var delimiterIndex = source.IndexOf(delimiter, start);
            var endExclusive = delimiterIndex < 0 ? source.Length : delimiterIndex;
            if (currentPartIndex == partIndex)
            {
                part = SliceTrimmedSegment(source, start, endExclusive);
                return true;
            }

            if (delimiterIndex < 0)
                return false;

            start = delimiterIndex + 1;
            currentPartIndex++;
        }
    }

    private static bool TryGetFinalDelimitedToken(string source, char delimiter, out string token)
    {
        token = string.Empty;
        if (!TryGetFinalDelimitedTokenRange(source, delimiter, out var tokenStart, out var tokenEndExclusive))
            return false;

        token = SliceSegment(source, tokenStart, tokenEndExclusive);
        return true;
    }

    private static bool TryGetFinalDelimitedTokenRange(string source, char delimiter, out int tokenStart, out int tokenEndExclusive)
    {
        tokenStart = 0;
        tokenEndExclusive = 0;
        var lastDelimiterIndex = source.LastIndexOf(delimiter);
        if (lastDelimiterIndex < 0 || lastDelimiterIndex == source.Length - 1)
            return false;

        var start = lastDelimiterIndex + 1;
        var end = source.Length - 1;
        TrimSegment(source, ref start, ref end);
        if (start > end)
            return false;

        tokenStart = start;
        tokenEndExclusive = end + 1;
        return true;
    }

    private static bool TryGetPenultimateDelimitedToken(string source, char delimiter, out string token)
    {
        token = string.Empty;
        if (!TryGetPenultimateDelimitedTokenRange(source, delimiter, out var tokenStart, out var tokenEndExclusive))
            return false;

        token = SliceSegment(source, tokenStart, tokenEndExclusive);
        return true;
    }

    private static bool TryGetPenultimateDelimitedTokenRange(
        string source,
        char delimiter,
        out int tokenStart,
        out int tokenEndExclusive)
    {
        tokenStart = 0;
        tokenEndExclusive = 0;

        var finalEnd = source.Length - 1;
        TrimTrailingWhitespace(source, ref finalEnd);
        if (finalEnd < 0)
            return false;

        var lastDelimiterIndex = source.LastIndexOf(delimiter, finalEnd);
        if (lastDelimiterIndex < 0)
            return false;

        var finalStart = lastDelimiterIndex + 1;
        TrimSegment(source, ref finalStart, ref finalEnd);
        if (finalStart > finalEnd)
            return false;

        var penultimateEnd = lastDelimiterIndex - 1;
        TrimTrailingWhitespace(source, ref penultimateEnd);
        if (penultimateEnd < 0)
            return false;

        var previousDelimiterIndex = source.LastIndexOf(delimiter, penultimateEnd);
        var penultimateStart = previousDelimiterIndex < 0 ? 0 : previousDelimiterIndex + 1;
        TrimSegment(source, ref penultimateStart, ref penultimateEnd);
        if (penultimateStart > penultimateEnd)
            return false;

        tokenStart = penultimateStart;
        tokenEndExclusive = penultimateEnd + 1;
        return true;
    }

    private static Func<string, string?>? TrySplitPascalCaseWords(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TrySplitPascalCaseWords(e.Source, out var words) && words == e.Expected))
            return null;

        return source => TrySplitPascalCaseWords(source, out var words) ? words : null;
    }

    private static bool TrySplitPascalCaseWords(string source, out string words)
    {
        words = string.Empty;
        if (source.Length < 2 || source.Any(char.IsWhiteSpace))
            return false;

        var split = new List<char>(source.Length + 4);
        var insertedSeparator = false;
        for (var i = 0; i < source.Length; i++)
        {
            var current = source[i];
            if (i > 0 &&
                char.IsUpper(current) &&
                char.IsLower(source[i - 1]))
            {
                split.Add(' ');
                insertedSeparator = true;
            }

            split.Add(current);
        }

        if (!insertedSeparator)
            return false;

        words = new string(split.ToArray());
        return words.Length > source.Length;
    }

    private static Func<string, string?>? TryStripThousandSeparators(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var (source, expected) in examples)
        {
            if (!source.Contains(',', StringComparison.Ordinal))
                return null;
            if (source.Replace(",", string.Empty) != expected)
                return null;
        }

        return s => s.Replace(",", string.Empty);
    }

    private static Func<string, string?>? TryExtractDigitsOnly(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var (source, expected) in examples)
        {
            if (source.All(char.IsDigit))
                return null;
            var digits = ExtractDigits(source);
            if (digits.Length == 0 || digits != expected)
                return null;
        }

        return s =>
        {
            var digits = ExtractDigits(s);
            return digits.Length > 0 ? digits : null;
        };
    }

    private static Func<string, string?>? TryExtractFinalDigitRun(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var (source, expected) in examples)
        {
            if (!TryGetFinalDigitRun(source, out var digitRun) || digitRun != expected)
                return null;
        }

        return source => TryGetFinalDigitRun(source, out var digitRun) ? digitRun : null;
    }

    private static bool TryGetFinalDigitRun(string source, out string digitRun)
    {
        digitRun = string.Empty;
        var end = source.Length - 1;
        while (end >= 0 && !char.IsDigit(source[end]))
            end--;

        if (end < 0)
            return false;

        var start = end;
        while (start >= 0 && char.IsDigit(source[start]))
            start--;

        digitRun = source[(start + 1)..(end + 1)];
        return digitRun.Length > 0;
    }

    private static Func<string, string?>? TryDelimitedPartCaseTransform(
        IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var delimiter in Delimiters.Where(delimiter => delimiter != ' ').Append(' '))
        {
            foreach (var transform in new Func<string, string>[]
                     {
                         s => s.ToUpperInvariant(),
                         s => s.ToLowerInvariant(),
                         ToProperCase
                     })
            {
                if (TryDelimitedPartCaseTransform(examples, delimiter, transform, out var pattern))
                    return pattern;
            }
        }

        return null;
    }

    private static bool TryDelimitedPartCaseTransform(
        IReadOnlyList<(string Source, string Expected)> examples,
        char delimiter,
        Func<string, string> transform,
        out Func<string, string?>? pattern)
    {
        pattern = null;
        int? partIndex = null;
        var changedAny = false;

        foreach (var (source, expected) in examples)
        {
            var parts = source.Split(delimiter, StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
                return false;

            var matches = new List<int>(1);
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0 && transform(parts[i]) == expected)
                    matches.Add(i);
            }

            if (matches.Count != 1)
                return false;

            var currentPartIndex = matches[0];
            if (partIndex is null)
                partIndex = currentPartIndex;
            else if (partIndex.Value != currentPartIndex)
                return false;

            changedAny |= parts[currentPartIndex] != expected;
        }

        if (partIndex is null || !changedAny)
            return false;

        var idx = partIndex.Value;
        pattern = source =>
        {
            var parts = source.Split(delimiter, StringSplitOptions.TrimEntries);
            return idx < parts.Length && parts[idx].Length > 0
                ? transform(parts[idx])
                : null;
        };
        return true;
    }

    private static Func<string, string?>? TryDelimitedPartReorder(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var sourceDelimiter in Delimiters)
        {
            if (!TryDelimitedPartReorder(examples, sourceDelimiter, s => s[1] + ", " + s[0], out var commaFirstPattern))
                continue;

            return commaFirstPattern;
        }

        if (TryDelimitedPartReorder(examples, ',', s => s[1] + " " + s[0], out var firstLastPattern))
            return firstLastPattern;

        return null;
    }

    private static Func<string, string?>? TryThreeTokenNameDropMiddle(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (examples.All(e => TrySplitWhitespaceTokens(e.Source, out var tokens) && e.Expected == tokens[1] + " " + tokens[2]))
            return source => TrySplitWhitespaceTokens(source, out var tokens) ? tokens[1] + " " + tokens[2] : null;

        if (examples.All(e => TrySplitWhitespaceTokens(e.Source, out var tokens) && e.Expected == tokens[0] + " " + tokens[1]))
            return source => TrySplitWhitespaceTokens(source, out var tokens) ? tokens[0] + " " + tokens[1] : null;

        if (examples.All(e => TrySplitWhitespaceTokens(e.Source, out var tokens) && e.Expected == tokens[0] + " " + tokens[2]))
            return source => TrySplitWhitespaceTokens(source, out var tokens) ? tokens[0] + " " + tokens[2] : null;

        if (examples.All(e => TrySplitWhitespaceTokens(e.Source, out var tokens) && e.Expected == tokens[2] + ", " + tokens[0]))
            return source => TrySplitWhitespaceTokens(source, out var tokens) ? tokens[2] + ", " + tokens[0] : null;

        if (examples.All(e => TrySplitWhitespaceTokens(e.Source, out var tokens) && e.Expected == tokens[2] + ", " + tokens[0] + " " + tokens[1]))
            return source => TrySplitWhitespaceTokens(source, out var tokens) ? tokens[2] + ", " + tokens[0] + " " + tokens[1] : null;

        return null;
    }

    private static Func<string, string?>? TryThreeTokenNameInitial(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (examples.All(e => TrySplitWhitespaceTokens(e.Source, out var tokens) && e.Expected == GetFirstInitial(tokens[1]) + "."))
            return source => TrySplitWhitespaceTokens(source, out var tokens) ? GetFirstInitial(tokens[1]) + "." : null;

        return null;
    }

    private static Func<string, string?>? TryFinalWhitespaceToken(IReadOnlyList<(string Source, string Expected)> examples)
    {
        var exampleTokens = new List<string[]>(examples.Count);
        foreach (var (source, expected) in examples)
        {
            if (!TrySplitVariableWhitespaceTokens(source, out var tokens) || expected != tokens[^1])
                return null;

            exampleTokens.Add(tokens);
        }

        if (exampleTokens.Select(tokens => tokens[0]).Distinct(StringComparer.Ordinal).Count() == 1)
            return null;

        return source => TrySplitVariableWhitespaceTokens(source, out var tokens)
            ? tokens[^1]
            : null;
    }

    private static Func<string, string?>? TryKnownTitleRemoval(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryRemoveKnownNameTitle(e.Source, out var name) && name == e.Expected))
            return null;

        return source => TryRemoveKnownNameTitle(source, out var name) ? name : null;
    }

    private static Func<string, string?>? TryKnownTitleAndSuffixRemoval(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryRemoveKnownNameTitleAndSuffix(e.Source, out var name) && name == e.Expected))
        {
            return null;
        }

        return source => TryRemoveKnownNameTitleAndSuffix(source, out var name) ? name : null;
    }

    private static Func<string, string?>? TryKnownNameCleanupDerivedPattern(
        IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var cleaner in new NameCleaner[]
                 {
                     TryRemoveKnownNameTitleAndSuffix,
                     TryRemoveKnownNameTitle,
                     TryRemoveKnownNameSuffix
                 })
        {
            var cleanedExamples = new List<(string Source, string Expected)>(examples.Count);
            foreach (var (source, expected) in examples)
            {
                if (!cleaner(source, out var cleaned))
                {
                    cleanedExamples.Clear();
                    break;
                }

                cleanedExamples.Add((cleaned, expected));
            }

            if (cleanedExamples.Count != examples.Count ||
                cleanedExamples.All(e => e.Source == e.Expected))
            {
                continue;
            }

            var patternFn =
                TryNameAbbreviations(cleanedExamples)
                ?? TryFullNameEmailPattern(cleanedExamples)
                ?? TryThreeTokenNameInitial(cleanedExamples)
                ?? TryThreeTokenNameDropMiddle(cleanedExamples)
                ?? TryDelimitedPartReorder(cleanedExamples)
                ?? TryFinalWhitespaceToken(cleanedExamples)
                ?? TryExtractByDelimiter(cleanedExamples);

            if (patternFn is null)
                continue;

            return source => cleaner(source, out var cleaned) ? patternFn(cleaned) : null;
        }

        return null;
    }

    private static bool TryRemoveKnownNameTitleAndSuffix(string source, out string name)
    {
        name = string.Empty;
        if (!TryRemoveKnownNameTitle(source, out var withoutTitle) ||
            !TryRemoveKnownTrailingNameSuffixes(withoutTitle, removeAll: true, out name))
        {
            return false;
        }

        return true;
    }

    private static bool TryRemoveKnownNameTitle(string source, out string name)
    {
        name = string.Empty;
        var tokens = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
            return false;

        var title = NormalizeKnownNameAffixToken(tokens[0]);
        if (!KnownNameTitles.Contains(title))
            return false;

        name = string.Join(' ', tokens.Skip(1));
        return name.Length > 0;
    }

    private static Func<string, string?>? TryKnownNameSuffixRemoval(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryRemoveKnownNameSuffix(e.Source, out var name) && name == e.Expected))
            return null;

        return source => TryRemoveKnownNameSuffix(source, out var name) ? name : null;
    }

    private static bool TryRemoveKnownNameSuffix(string source, out string name)
    {
        return TryRemoveKnownTrailingNameSuffixes(source, removeAll: false, out name);
    }

    private static Func<string, string?>? TryKnownOrganizationSuffixRemoval(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryRemoveKnownOrganizationSuffix(e.Source, out var name) && name == e.Expected))
            return null;

        return source => TryRemoveKnownOrganizationSuffix(source, out var name) ? name : null;
    }

    private static bool TryRemoveKnownOrganizationSuffix(string source, out string name)
    {
        name = string.Empty;
        var tokens = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
            return false;

        var suffix = NormalizeKnownNameAffixToken(tokens[^1]);
        if (!KnownOrganizationSuffixes.Contains(suffix))
            return false;

        var nameTokens = tokens[..^1];
        nameTokens[^1] = nameTokens[^1].TrimEnd(',');
        name = string.Join(' ', nameTokens);
        return name.Length > 0;
    }

    private static bool TryRemoveKnownTrailingNameSuffixes(string source, bool removeAll, out string name)
    {
        name = string.Empty;
        var tokens = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
            return false;

        var endExclusive = tokens.Length;
        var removedSuffix = false;
        while (endExclusive > 1)
        {
            var suffix = NormalizeKnownNameAffixToken(tokens[endExclusive - 1]);
            if (!KnownNameSuffixes.Contains(suffix))
                break;

            endExclusive--;
            removedSuffix = true;
            if (!removeAll)
                break;
        }

        if (!removedSuffix)
            return false;

        var nameTokens = tokens[..endExclusive];
        nameTokens[^1] = nameTokens[^1].TrimEnd(',');
        name = string.Join(' ', nameTokens);
        return name.Length > 0;
    }

    private static string NormalizeKnownNameAffixToken(string token) =>
        token.TrimEnd('.', ',');

    private static Func<string, string?>? TryDigitMask(IReadOnlyList<(string Source, string Expected)> examples)
    {
        string? mask = null;
        int? digitCount = null;

        foreach (var (source, expected) in examples)
        {
            if (source.Length == 0 || source.Any(c => !char.IsDigit(c)))
                return null;

            var expectedDigits = ExtractDigits(expected);
            if (source != expectedDigits)
                return null;

            var currentMask = CreateDigitMask(expected);
            if (currentMask == expected || string.IsNullOrWhiteSpace(currentMask))
                return null;

            if (mask is null)
            {
                mask = currentMask;
                digitCount = source.Length;
            }
            else if (mask != currentMask || digitCount != source.Length)
            {
                return null;
            }
        }

        if (mask is null || digitCount is null)
            return null;

        return source =>
        {
            if (source.Length != digitCount.Value || source.Any(c => !char.IsDigit(c)))
                return null;

            return ApplyDigitMask(source, mask);
        };
    }

    private static Func<string, string?>? TryPhoneNumberNormalization(IReadOnlyList<(string Source, string Expected)> examples)
    {
        string? mask = null;
        int? digitCount = null;
        var sawFormattedSource = false;

        foreach (var (source, expected) in examples)
        {
            var sourceDigits = ExtractDigits(source);
            var expectedDigits = ExtractDigits(expected);
            if (sourceDigits.Length == 0 ||
                expectedDigits.Length is < 7 or > 15 ||
                !sourceDigits.EndsWith(expectedDigits, StringComparison.Ordinal))
            {
                return null;
            }

            var currentMask = CreateDigitMask(expected);
            if (currentMask == expected || string.IsNullOrWhiteSpace(currentMask))
                return null;

            if (mask is null)
            {
                mask = currentMask;
                digitCount = expectedDigits.Length;
            }
            else if (mask != currentMask || digitCount != expectedDigits.Length)
            {
                return null;
            }

            sawFormattedSource |= source.Any(c => !char.IsDigit(c));
        }

        if (mask is null || digitCount is null || !sawFormattedSource)
            return null;

        return source =>
        {
            var digits = ExtractDigits(source);
            if (digits.Length < digitCount.Value)
                return null;

            return ApplyDigitMask(digits[^digitCount.Value..], mask);
        };
    }

    private static bool TryDelimitedPartReorder(
        IReadOnlyList<(string Source, string Expected)> examples,
        char sourceDelimiter,
        Func<string[], string> formatter,
        out Func<string, string?>? pattern)
    {
        pattern = null;
        foreach (var (source, expected) in examples)
        {
            if (!TrySplitTwoParts(source, sourceDelimiter, out var parts) ||
                formatter(parts) != expected)
            {
                return false;
            }
        }

        pattern = source =>
            TrySplitTwoParts(source, sourceDelimiter, out var parts)
                ? formatter(parts)
                : null;
        return true;
    }

    private static bool TrySplitTwoParts(string source, char delimiter, out string[] parts)
    {
        parts = source.Split(delimiter, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts.All(part => part.Length > 0);
    }

    private static Func<string, string?>? TryPairedDelimiterExtraction(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var (open, close) in PairedDelimiters)
        {
            if (!examples.All(e => TryExtractBetweenPairedDelimiters(e.Source, open, close, out var extracted) && extracted == e.Expected))
                continue;

            return source => TryExtractBetweenPairedDelimiters(source, open, close, out var extracted)
                ? extracted
                : null;
        }

        return null;
    }

    private static bool TryExtractBetweenPairedDelimiters(string source, char open, char close, out string extracted)
    {
        extracted = string.Empty;
        var openIndex = source.IndexOf(open);
        if (openIndex < 0)
            return false;

        var closeIndex = source.IndexOf(close, openIndex + 1);
        if (closeIndex <= openIndex + 1)
            return false;

        extracted = source[(openIndex + 1)..closeIndex].Trim();
        return extracted.Length > 0;
    }

    private static Func<string, string?>? TryPairedDelimiterRemoval(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var (open, close) in PairedDelimiters)
        {
            if (!examples.All(e => TryRemovePairedDelimiterText(e.Source, open, close, out var removed) && removed == e.Expected))
                continue;

            return source => TryRemovePairedDelimiterText(source, open, close, out var removed)
                ? removed
                : null;
        }

        return null;
    }

    private static bool TryRemovePairedDelimiterText(string source, char open, char close, out string removed)
    {
        removed = string.Empty;
        var openIndex = source.IndexOf(open);
        if (openIndex < 0)
            return false;

        var closeIndex = source.IndexOf(close, openIndex + 1);
        if (closeIndex <= openIndex)
            return false;

        removed = (source[..openIndex] + source[(closeIndex + 1)..]).Trim();
        while (removed.Contains("  ", StringComparison.Ordinal))
            removed = removed.Replace("  ", " ", StringComparison.Ordinal);

        return removed.Length > 0 && !string.Equals(removed, source, StringComparison.Ordinal);
    }

    private static Func<string, string?>? TryLabelValueExtraction(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var separator in LabelValueSeparators)
        {
            if (!examples.All(e => TryExtractLabelValue(e.Source, separator, out var extracted) && extracted == e.Expected))
                continue;

            return source => TryExtractLabelValue(source, separator, out var extracted)
                ? extracted
                : null;
        }

        return null;
    }

    private static bool TryExtractLabelValue(string source, string separator, out string extracted)
    {
        extracted = string.Empty;
        if (!TryFindLabelValueSeparator(source, separator, out _, out var separatorEnd))
            return false;

        extracted = source[separatorEnd..].Trim();
        return extracted.Length > 0;
    }

    private static Func<string, string?>? TryLabelQualifierRemoval(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var separator in LabelValueSeparators)
        {
            if (!examples.All(e => TryRemoveLabelValue(e.Source, separator, out var removed) && removed == e.Expected))
                continue;

            return source => TryRemoveLabelValue(source, separator, out var removed)
                ? removed
                : null;
        }

        return null;
    }

    private static bool TryRemoveLabelValue(string source, string separator, out string removed)
    {
        removed = string.Empty;
        if (!TryFindLabelValueSeparator(source, separator, out var separatorStart, out _))
            return false;

        removed = source[..separatorStart].Trim();
        return removed.Length > 0;
    }

    private static bool TryFindLabelValueSeparator(
        string source,
        string separator,
        out int separatorStart,
        out int separatorEnd)
    {
        separatorStart = -1;
        separatorEnd = -1;

        var searchStart = 0;
        while (searchStart < source.Length)
        {
            var tokenIndex = source.IndexOf(separator, searchStart, StringComparison.Ordinal);
            if (tokenIndex < 0)
                return false;

            if ((separator == "-" || separator == "=") &&
                tokenIndex + 1 < source.Length &&
                source[tokenIndex + 1] == '>')
            {
                searchStart = tokenIndex + separator.Length;
                continue;
            }

            separatorStart = tokenIndex;
            while (separatorStart > 0 && char.IsWhiteSpace(source[separatorStart - 1]))
                separatorStart--;

            separatorEnd = tokenIndex + separator.Length;
            while (separatorEnd < source.Length && char.IsWhiteSpace(source[separatorEnd]))
                separatorEnd++;

            return separatorStart > 0 && separatorEnd < source.Length;
        }

        return false;
    }

    private static bool TrySplitWhitespaceTokens(string source, out string[] tokens)
    {
        tokens = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 3 && tokens.All(token => token.Length > 0);
    }

    private static bool TrySplitVariableWhitespaceTokens(string source, out string[] tokens)
    {
        tokens = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 2 && tokens.All(token => token.Length > 0);
    }

    private static Func<string, string?>? TryInitials(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var delimiter in Delimiters)
        {
            if (examples.All(e => TryGetDelimitedInitials(e.Source, delimiter, out var initials) && initials == e.Expected))
            {
                return s => TryGetDelimitedInitials(s, delimiter, out var initials) ? initials : null;
            }

            if (examples.All(e => TryGetDelimitedUpperInitials(e.Source, delimiter, out var initials) && initials == e.Expected))
            {
                return s => TryGetDelimitedUpperInitials(s, delimiter, out var initials) ? initials : null;
            }
        }

        return null;
    }

    private static Func<string, string?>? TryPrefixTrim(IReadOnlyList<(string Source, string Expected)> examples)
    {
        var first = examples[0];
        if (first.Source.Length <= first.Expected.Length)
            return null;

        int prefixLen = first.Source.Length - first.Expected.Length;
        var prefix = first.Source[..prefixLen];
        if (first.Source[prefixLen..] != first.Expected)
            return null;

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
        var first = examples[0];
        if (first.Expected.Length <= first.Source.Length)
            return null;

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

        int startIndex = first.Source.IndexOf(first.Expected, StringComparison.Ordinal);
        if (startIndex < 0)
            return null;

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

    private static bool TryGetDelimitedUpperInitials(string source, char delimiter, out string initials)
    {
        var parts = source.Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            initials = string.Empty;
            return false;
        }

        initials = string.Concat(parts.Select(GetUpperInitial));
        return true;
    }

    private static Func<string, string?>? TryNameAbbreviations(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (TryNameAbbreviation(examples, 2, tokens => GetFirstInitial(tokens[0]) + ". " + tokens[1], out var firstInitialLast))
            return firstInitialLast;

        if (TryNameAbbreviation(examples, 2, tokens => GetFirstInitial(tokens[0]) + ". " + GetFirstInitial(tokens[1]) + ".", out var twoPartInitials))
            return twoPartInitials;

        if (TryNameAbbreviation(examples, 2, tokens => GetUpperInitial(tokens[0]) + ". " + GetUpperInitial(tokens[1]) + ".", out var twoPartUpperInitials))
            return twoPartUpperInitials;

        if (TryNameAbbreviation(examples, 2, tokens => tokens[0] + " " + GetFirstInitial(tokens[1]) + ".", out var firstLastInitial))
            return firstLastInitial;

        if (TryNameAbbreviation(examples, 2, tokens => tokens[1] + " " + GetFirstInitial(tokens[0]) + ".", out var lastFirstInitial))
            return lastFirstInitial;

        if (TryNameAbbreviation(examples, 2, tokens => tokens[1] + ", " + GetFirstInitial(tokens[0]) + ".", out var lastCommaFirstInitial))
            return lastCommaFirstInitial;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[0] + " " + GetFirstInitial(tokens[1]) + ". " + tokens[2], out var middleInitial))
            return middleInitial;

        if (TryNameAbbreviation(examples, 3, tokens => GetFirstInitial(tokens[0]) + ". " + tokens[2], out var firstInitialLastFromThreeParts))
            return firstInitialLastFromThreeParts;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[0] + " " + GetFirstInitial(tokens[2]) + ".", out var firstLastInitialFromThreeParts))
            return firstLastInitialFromThreeParts;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[2] + " " + GetFirstInitial(tokens[0]) + ".", out var lastFirstInitialFromThreeParts))
            return lastFirstInitialFromThreeParts;

        if (TryNameAbbreviation(examples, 3, tokens => GetFirstInitial(tokens[1]) + ". " + tokens[2], out var middleInitialLastFromThreeParts))
            return middleInitialLastFromThreeParts;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[1] + " " + GetFirstInitial(tokens[2]) + ".", out var middleLastInitialFromThreeParts))
            return middleLastInitialFromThreeParts;

        if (TryNameAbbreviation(examples, 3, tokens => GetFirstInitial(tokens[0]) + ". " + GetFirstInitial(tokens[1]) + ". " + tokens[2], out var firstMiddleInitialsLast))
            return firstMiddleInitialsLast;

        if (TryNameAbbreviation(examples, 3, tokens => GetFirstInitial(tokens[0]) + ". " + GetFirstInitial(tokens[1]) + ". " + GetFirstInitial(tokens[2]) + ".", out var threePartInitials))
            return threePartInitials;

        if (TryNameAbbreviation(examples, 3, tokens => GetUpperInitial(tokens[0]) + ". " + GetUpperInitial(tokens[1]) + ". " + GetUpperInitial(tokens[2]) + ".", out var threePartUpperInitials))
            return threePartUpperInitials;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[0] + " " + GetFirstInitial(tokens[1]) + ".", out var firstMiddleInitialOnly))
            return firstMiddleInitialOnly;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[0] + " " + tokens[1] + " " + GetFirstInitial(tokens[2]) + ".", out var firstMiddleLastInitial))
            return firstMiddleLastInitial;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[2] + ", " + tokens[0] + " " + GetFirstInitial(tokens[1]) + ".", out var lastCommaFirstMiddleInitial))
            return lastCommaFirstMiddleInitial;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[2] + ", " + GetFirstInitial(tokens[0]) + ". " + GetFirstInitial(tokens[1]) + ".", out var lastCommaFirstMiddleInitials))
            return lastCommaFirstMiddleInitials;

        if (TryNameAbbreviation(examples, 3, tokens => tokens[2] + " " + GetFirstInitial(tokens[0]) + ". " + GetFirstInitial(tokens[1]) + ".", out var lastFirstMiddleInitials))
            return lastFirstMiddleInitials;

        return null;
    }

    private static bool TryNameAbbreviation(
        IReadOnlyList<(string Source, string Expected)> examples,
        int tokenCount,
        Func<string[], string> formatter,
        out Func<string, string?>? pattern)
    {
        pattern = null;
        foreach (var (source, expected) in examples)
        {
            if (!TrySplitWhitespaceTokens(source, tokenCount, out var tokens) || formatter(tokens) != expected)
                return false;
        }

        pattern = source =>
            TrySplitWhitespaceTokens(source, tokenCount, out var tokens)
                ? formatter(tokens)
                : null;
        return true;
    }

    private static bool TrySplitWhitespaceTokens(string source, int tokenCount, out string[] tokens)
    {
        tokens = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == tokenCount && tokens.All(token => token.Length > 0);
    }

    private static string GetFirstInitial(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value[0].ToString();
}
