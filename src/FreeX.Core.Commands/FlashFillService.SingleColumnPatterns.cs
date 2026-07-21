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

    private enum TimeOutputKind
    {
        TwentyFourHour,
        TwelveHour
    }

    private enum TimeMeridiemCasing
    {
        Upper,
        Lower,
        Title
    }

    private enum UsPhoneComponentKind
    {
        AreaCode,
        LocalNumber
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
        DomainStem,
        RootDomainStem
    }

    private readonly record struct DateParts(int Year, int Month, int Day);

    private readonly record struct TimeParts(int Hour, int Minute, int Second);

    private readonly record struct UsAddressParts(string Street, string City, string State, string Zip);

    private readonly record struct WebAddressParts(
        string Host,
        string HostWithoutWww,
        string DomainStem,
        string RootDomainStem);

    private readonly record struct DateOutputPattern(
        DatePartKind First,
        DatePartKind Second,
        DatePartKind Third,
        char Separator,
        int YearWidth,
        int MonthWidth,
        int DayWidth);

    private readonly record struct TimeOutputPattern(
        TimeOutputKind Kind,
        bool IncludeSeconds,
        int HourWidth,
        bool SpaceBeforeMeridiem,
        TimeMeridiemCasing MeridiemCasing);

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
    private static readonly string[] LabelValueSeparators = [":", "=", "->", "=>", "→", "⇒", "-", "–", "—", "/", "|"];
    private static readonly string[] PhoneExtensionMarkers = ["extension", "ext", "x"];
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
        "Mx",
        "Hon",
        "Honorable",
        "Rev",
        "Reverend",
        "Fr",
        "Father",
        "Prof",
        "Professor",
        "Rabbi",
        "Imam",
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
        "JD",
        "J.D",
        "MD",
        "M.D",
        "DO",
        "D.O",
        "CPA",
        "C.P.A",
        "MBA",
        "M.B.A",
        "RN",
        "R.N",
        "PE",
        "P.E",
        "MPH",
        "M.P.H",
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
        "Pte",
        "Pvt",
        "Sdn",
        "Bhd",
        "Berhad",
        "KK",
        "K.K",
        "GmbH",
        "AG",
        "SA",
        "S.A",
        "Sarl",
        "SAS",
        "SL",
        "S.L",
        "SLU",
        "S.L.U",
        "SRL",
        "S.R.L",
        "SpA",
        "S.p.A",
        "BV",
        "NV",
        "Pty",
        "UAB",
        "Zrt",
        "Nyrt",
        "EURL",
        "E.U.R.L",
        "UG",
        "AB",
        "Oy",
        "A/S",
        "ApS",
        "Kft",
        "Sro",
        "S.R.O"
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

    private static Func<string, string?>? TryRemoveLeadingDottedToken(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryRemoveLeadingDottedToken(e.Source, out var remainder) && remainder == e.Expected))
            return null;

        return source => TryRemoveLeadingDottedToken(source, out var remainder) ? remainder : null;
    }

    private static Func<string, string?>? TryExtractMiddleDottedToken(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryGetMiddleDottedToken(e.Source, out var token) && token == e.Expected))
            return null;

        return source => TryGetMiddleDottedToken(source, out var token) ? token : null;
    }

    private static Func<string, string?>? TryExtractFirstDottedToken(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryGetFirstDottedToken(e.Source, out var token) && token == e.Expected))
            return null;

        return source => TryGetFirstDottedToken(source, out var token) ? token : null;
    }

    private static Func<string, string?>? TryRemoveMiddleDottedToken(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryRemoveMiddleDottedToken(e.Source, out var remainder) && remainder == e.Expected))
            return null;

        return source => TryRemoveMiddleDottedToken(source, out var remainder) ? remainder : null;
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

    private static Func<string, string?>? TryRemoveLeadingDelimitedToken(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var delimiter in FinalDelimitedTokenDelimiters)
        {
            if (!examples.All(e => TryRemoveLeadingDelimitedToken(e.Source, delimiter, out var remainder) && remainder == e.Expected))
                continue;

            return source => TryRemoveLeadingDelimitedToken(source, delimiter, out var remainder) ? remainder : null;
        }

        return null;
    }

    private static Func<string, string?>? TryRemoveMiddleDelimitedToken(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var delimiter in FinalDelimitedTokenDelimiters)
        {
            if (!examples.All(e => TryRemoveMiddleDelimitedToken(e.Source, delimiter, out var remainder) && remainder == e.Expected))
                continue;

            return source => TryRemoveMiddleDelimitedToken(source, delimiter, out var remainder) ? remainder : null;
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

    private static Func<string, string?>? TryExtractFileParentDirectoryName(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryGetFileParentDirectoryName(e.Source, out var name) && name == e.Expected))
            return null;

        var cache = new ExtractedSegmentCache();
        return source => TryGetFileParentDirectoryNameRange(source, out var start, out var endExclusive)
            ? cache.GetOrAdd(source, start, endExclusive)
            : null;
    }

    private static Func<string, string?>? TryExtractFileParentDirectoryTitle(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryGetFileParentDirectoryTitle(e.Source, out var title) && title == e.Expected))
            return null;

        return source => TryGetFileParentDirectoryTitle(source, out var title) ? title : null;
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

    private static bool TryRemoveLeadingDottedToken(string source, out string remainder)
    {
        remainder = string.Empty;
        var firstDotIndex = source.IndexOf('.');
        if (firstDotIndex <= 0 || firstDotIndex == source.Length - 1)
            return false;

        var leadingToken = source[..firstDotIndex].Trim();
        if (leadingToken.Length == 0)
            return false;

        remainder = source[(firstDotIndex + 1)..].Trim();
        return remainder.Length > 0;
    }

    private static bool TryGetMiddleDottedToken(string source, out string token)
    {
        token = string.Empty;
        var trimmed = source.Trim();
        var firstDotIndex = trimmed.IndexOf('.');
        if (firstDotIndex <= 0)
            return false;

        var secondDotIndex = trimmed.IndexOf('.', firstDotIndex + 1);
        if (secondDotIndex < 0 || secondDotIndex == trimmed.Length - 1)
            return false;

        if (trimmed.IndexOf('.', secondDotIndex + 1) >= 0)
            return false;

        var first = trimmed[..firstDotIndex].Trim();
        var middle = trimmed[(firstDotIndex + 1)..secondDotIndex].Trim();
        var last = trimmed[(secondDotIndex + 1)..].Trim();
        if (first.Length == 0 || middle.Length == 0 || last.Length == 0)
            return false;

        token = middle;
        return true;
    }

    private static bool TryGetFirstDottedToken(string source, out string token)
    {
        token = string.Empty;
        var trimmed = source.Trim();
        var firstDotIndex = trimmed.IndexOf('.');
        if (firstDotIndex <= 0)
            return false;

        var secondDotIndex = trimmed.IndexOf('.', firstDotIndex + 1);
        if (secondDotIndex < 0 || secondDotIndex == trimmed.Length - 1)
            return false;

        if (trimmed.IndexOf('.', secondDotIndex + 1) >= 0)
            return false;

        var first = trimmed[..firstDotIndex].Trim();
        var middle = trimmed[(firstDotIndex + 1)..secondDotIndex].Trim();
        var last = trimmed[(secondDotIndex + 1)..].Trim();
        if (first.Length == 0 || middle.Length == 0 || last.Length == 0)
            return false;

        token = first;
        return true;
    }

    private static bool TryRemoveMiddleDottedToken(string source, out string remainder)
    {
        remainder = string.Empty;
        var trimmed = source.Trim();
        var firstDotIndex = trimmed.IndexOf('.');
        if (firstDotIndex <= 0)
            return false;

        var secondDotIndex = trimmed.IndexOf('.', firstDotIndex + 1);
        if (secondDotIndex < 0 || secondDotIndex == trimmed.Length - 1)
            return false;

        if (trimmed.IndexOf('.', secondDotIndex + 1) >= 0)
            return false;

        var first = trimmed[..firstDotIndex].Trim();
        var middle = trimmed[(firstDotIndex + 1)..secondDotIndex].Trim();
        var last = trimmed[(secondDotIndex + 1)..].Trim();
        if (first.Length == 0 || middle.Length == 0 || last.Length == 0)
            return false;

        var secondSeparatorStart = secondDotIndex;
        while (secondSeparatorStart > firstDotIndex + 1 && char.IsWhiteSpace(trimmed[secondSeparatorStart - 1]))
            secondSeparatorStart--;

        remainder = first + trimmed[secondSeparatorStart..].TrimEnd();
        return remainder.Length > 0;
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

    private static bool TryRemoveLeadingDelimitedToken(string source, char delimiter, out string remainder)
    {
        remainder = string.Empty;
        var firstDelimiterIndex = source.IndexOf(delimiter);
        if (firstDelimiterIndex <= 0 || firstDelimiterIndex == source.Length - 1)
            return false;

        var leadingToken = source[..firstDelimiterIndex].Trim();
        if (leadingToken.Length == 0)
            return false;

        remainder = source[(firstDelimiterIndex + 1)..].Trim();
        return remainder.Length > 0;
    }

    private static bool TryRemoveMiddleDelimitedToken(string source, char delimiter, out string remainder)
    {
        remainder = string.Empty;
        var trimmed = source.Trim();
        var firstDelimiterIndex = trimmed.IndexOf(delimiter);
        if (firstDelimiterIndex <= 0)
            return false;

        var secondDelimiterIndex = trimmed.IndexOf(delimiter, firstDelimiterIndex + 1);
        if (secondDelimiterIndex < 0 || secondDelimiterIndex == trimmed.Length - 1)
            return false;

        if (trimmed.IndexOf(delimiter, secondDelimiterIndex + 1) >= 0)
            return false;

        var first = trimmed[..firstDelimiterIndex].Trim();
        var middle = trimmed[(firstDelimiterIndex + 1)..secondDelimiterIndex].Trim();
        var last = trimmed[(secondDelimiterIndex + 1)..].Trim();
        if (first.Length == 0 || middle.Length == 0 || last.Length == 0)
            return false;

        var secondSeparatorStart = secondDelimiterIndex;
        while (secondSeparatorStart > firstDelimiterIndex + 1 && char.IsWhiteSpace(trimmed[secondSeparatorStart - 1]))
            secondSeparatorStart--;

        remainder = first + trimmed[secondSeparatorStart..].TrimEnd();
        return remainder.Length > 0;
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

    private static bool TryGetFileParentDirectoryName(string source, out string name)
    {
        name = string.Empty;
        if (!TryGetFileParentDirectoryNameRange(source, out var start, out var endExclusive))
            return false;

        name = SliceSegment(source, start, endExclusive);
        return true;
    }

    private static bool TryGetFileParentDirectoryTitle(string source, out string title)
    {
        title = string.Empty;
        return TryGetFileParentDirectoryName(source, out var name) &&
               TryFormatSlugStemAsTitle(name, out title);
    }

    private static bool TryGetFileParentDirectoryNameRange(
        string source,
        out int parentStart,
        out int parentEndExclusive)
    {
        parentStart = 0;
        parentEndExclusive = 0;
        if (IsHttpOrHttpsUrlCandidate(source))
            return false;

        var end = source.Length - 1;
        TrimTrailingWhitespace(source, ref end);
        if (end < 0)
            return false;

        var lastSeparatorIndex = LastPathSeparatorIndex(source, end);
        if (lastSeparatorIndex <= 0 || lastSeparatorIndex >= end)
            return false;

        var finalStart = lastSeparatorIndex + 1;
        var finalEnd = end;
        TrimSegment(source, ref finalStart, ref finalEnd);
        if (finalStart > finalEnd)
            return false;

        var finalLength = finalEnd - finalStart + 1;
        var dotIndex = source.LastIndexOf('.', finalEnd, finalLength);
        if (dotIndex <= finalStart || dotIndex == finalEnd)
            return false;

        var parentEnd = lastSeparatorIndex - 1;
        TrimSegment(source, ref parentStart, ref parentEnd);
        if (parentStart > parentEnd)
            return false;

        var previousSeparatorIndex = LastPathSeparatorIndex(source, parentEnd);
        parentStart = previousSeparatorIndex + 1;
        TrimSegment(source, ref parentStart, ref parentEnd);
        if (parentStart > parentEnd)
            return false;

        if (source[parentEnd] == ':')
            return false;

        parentEndExclusive = parentEnd + 1;
        return true;
    }

    private static int LastPathSeparatorIndex(string source, int startIndex) =>
        Math.Max(source.LastIndexOf('/', startIndex), source.LastIndexOf('\\', startIndex));

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
        var sawEarlierDigitRun = false;
        foreach (var (source, expected) in examples)
        {
            if (expected.Length == 0 ||
                !expected.All(char.IsDigit) ||
                source.All(char.IsDigit) ||
                !TryGetFinalDigitRunRange(source, out var digitRunStart, out var digitRunEndExclusive) ||
                !source.AsSpan(digitRunStart, digitRunEndExclusive - digitRunStart).SequenceEqual(expected.AsSpan()) ||
                HasMatchingDigitRunBefore(source, expected, digitRunStart))
            {
                return null;
            }

            sawEarlierDigitRun |= HasDigitRunBefore(source, digitRunStart);
        }

        if (!sawEarlierDigitRun)
            return null;

        var cache = new ExtractedSegmentCache();
        return source => TryGetFinalDigitRunRange(source, out var digitRunStart, out var digitRunEndExclusive)
            ? cache.GetOrAdd(source, digitRunStart, digitRunEndExclusive)
            : null;
    }

    private static bool TryGetFinalDigitRunRange(string source, out int digitRunStart, out int digitRunEndExclusive)
    {
        digitRunStart = 0;
        digitRunEndExclusive = 0;
        var end = source.Length - 1;
        while (end >= 0 && !char.IsDigit(source[end]))
            end--;

        if (end < 0)
            return false;

        digitRunStart = end;
        while (digitRunStart >= 0 && char.IsDigit(source[digitRunStart]))
            digitRunStart--;

        digitRunStart++;
        digitRunEndExclusive = end + 1;
        return digitRunStart < digitRunEndExclusive;
    }

    private static bool HasDigitRunBefore(string source, int endExclusive)
    {
        for (var i = 0; i < endExclusive; i++)
        {
            if (char.IsDigit(source[i]))
                return true;
        }

        return false;
    }

    private static bool HasMatchingDigitRunBefore(string source, string expected, int endExclusive)
    {
        var runStart = -1;
        for (var i = 0; i < endExclusive; i++)
        {
            if (char.IsDigit(source[i]))
            {
                if (runStart < 0)
                    runStart = i;

                continue;
            }

            if (runStart >= 0 && DigitRunEquals(source, runStart, i, expected))
                return true;

            runStart = -1;
        }

        return runStart >= 0 && DigitRunEquals(source, runStart, endExclusive, expected);
    }

    private static bool DigitRunEquals(string source, int start, int endExclusive, string expected)
    {
        var length = endExclusive - start;
        return length == expected.Length &&
               source.AsSpan(start, length).SequenceEqual(expected.AsSpan());
    }

    /// <summary>
    /// Extracts the FIRST embedded digit run (e.g. "12" from "Room12-Wing3"), the counterpart
    /// to <see cref="TryExtractFinalDigitRun"/>. Only fires when every example has a digit run
    /// AFTER the leading one being matched, so it never fights with the final-digit-run pattern
    /// when a source contains just a single digit run. Sources that already look like an
    /// embedded date (e.g. "2026-03-05") are left to the dedicated date-component/date-extraction
    /// patterns earlier in the chain, so this generic fallback never overrides their deliberate
    /// null (ambiguous-component) result with an arbitrary "first number" guess.
    /// </summary>
    private static Func<string, string?>? TryExtractFirstDigitRun(IReadOnlyList<(string Source, string Expected)> examples)
    {
        var sawLaterDigitRun = false;
        foreach (var (source, expected) in examples)
        {
            if (expected.Length == 0 ||
                !expected.All(char.IsDigit) ||
                source.All(char.IsDigit) ||
                TryFindEmbeddedDateComponentTokens(source, out _) ||
                !TryGetFirstDigitRunRange(source, out var digitRunStart, out var digitRunEndExclusive) ||
                !source.AsSpan(digitRunStart, digitRunEndExclusive - digitRunStart).SequenceEqual(expected.AsSpan()) ||
                HasMatchingDigitRunAfter(source, expected, digitRunEndExclusive))
            {
                return null;
            }

            sawLaterDigitRun |= HasDigitRunAfter(source, digitRunEndExclusive);
        }

        if (!sawLaterDigitRun)
            return null;

        var cache = new ExtractedSegmentCache();
        return source => TryGetFirstDigitRunRange(source, out var digitRunStart, out var digitRunEndExclusive)
            ? cache.GetOrAdd(source, digitRunStart, digitRunEndExclusive)
            : null;
    }

    private static bool TryGetFirstDigitRunRange(string source, out int digitRunStart, out int digitRunEndExclusive)
    {
        digitRunStart = 0;
        digitRunEndExclusive = 0;
        var start = 0;
        while (start < source.Length && !char.IsDigit(source[start]))
            start++;

        if (start >= source.Length)
            return false;

        var end = start;
        while (end < source.Length && char.IsDigit(source[end]))
            end++;

        digitRunStart = start;
        digitRunEndExclusive = end;
        return digitRunStart < digitRunEndExclusive;
    }

    private static bool HasDigitRunAfter(string source, int startExclusive)
    {
        for (var i = startExclusive; i < source.Length; i++)
        {
            if (char.IsDigit(source[i]))
                return true;
        }

        return false;
    }

    private static bool HasMatchingDigitRunAfter(string source, string expected, int startExclusive)
    {
        var runStart = -1;
        for (var i = startExclusive; i < source.Length; i++)
        {
            if (char.IsDigit(source[i]))
            {
                if (runStart < 0)
                    runStart = i;

                continue;
            }

            if (runStart >= 0 && DigitRunEquals(source, runStart, i, expected))
                return true;

            runStart = -1;
        }

        return runStart >= 0 && DigitRunEquals(source, runStart, source.Length, expected);
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

        // With 2+ examples, require the first tokens to vary; otherwise "always take the
        // last token" is indistinguishable from a coincidental fixed-prefix pattern and we
        // defer to more specific patterns. With exactly one example there is nothing to
        // disambiguate against, so a single training pair is enough to infer "last token"
        // (matching Excel's Flash Fill, which generalizes a single example this way).
        if (examples.Count > 1
            && exampleTokens.Select(tokens => tokens[0]).Distinct(StringComparer.Ordinal).Count() == 1)
        {
            return null;
        }

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
        var tokens = SplitTrailingKnownAffixTokens(source, KnownOrganizationSuffixes);
        if (tokens.Length < 2)
            return false;

        var endExclusive = tokens.Length;
        var removedSuffix = false;
        while (endExclusive > 1)
        {
            var suffix = NormalizeKnownNameAffixToken(tokens[endExclusive - 1]);
            if (!KnownOrganizationSuffixes.Contains(suffix))
                break;

            endExclusive--;
            removedSuffix = true;
        }

        if (!removedSuffix)
            return false;

        var nameTokens = tokens[..endExclusive];
        nameTokens[^1] = nameTokens[^1].TrimEnd(',');
        name = string.Join(' ', nameTokens);
        return name.Length > 0;
    }

    private static bool TryRemoveKnownTrailingNameSuffixes(string source, bool removeAll, out string name)
    {
        name = string.Empty;
        var tokens = SplitTrailingKnownAffixTokens(source, KnownNameSuffixes);
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

    private static string[] SplitTrailingKnownAffixTokens(string source, HashSet<string> knownAffixes)
    {
        var tokens = source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return tokens;

        var lastToken = tokens[^1];
        if (!lastToken.Contains(',', StringComparison.Ordinal))
            return tokens;

        var commaParts = lastToken.Split(',');
        if (commaParts.Length < 2 || commaParts.Any(part => part.Length == 0))
            return tokens;

        var suffixStart = commaParts.Length;
        while (suffixStart > 0 &&
               knownAffixes.Contains(NormalizeKnownNameAffixToken(commaParts[suffixStart - 1])))
        {
            suffixStart--;
        }

        if (suffixStart == commaParts.Length || (suffixStart == 0 && tokens.Length == 1))
            return tokens;

        var splitTokens = new string[tokens.Length - 1 + commaParts.Length];
        Array.Copy(tokens, splitTokens, tokens.Length - 1);
        Array.Copy(commaParts, 0, splitTokens, tokens.Length - 1, commaParts.Length);
        return splitTokens;
    }

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
        var extensionPattern = TryPhoneExtensionExtraction(examples);
        if (extensionPattern is not null)
            return extensionPattern;

        var componentPattern = TryUsPhoneComponentExtraction(examples);
        if (componentPattern is not null)
            return componentPattern;

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

    private static Func<string, string?>? TryUsPhoneComponentExtraction(IReadOnlyList<(string Source, string Expected)> examples)
    {
        UsPhoneComponentKind? kind = null;
        string? mask = null;

        foreach (var (source, expected) in examples)
        {
            if (!TryNormalizeUsPhoneDigits(source, out var phoneDigits))
                return null;

            var expectedDigits = ExtractDigits(expected);
            if (!TryGetUsPhoneComponentKind(phoneDigits, expectedDigits, out var currentKind))
                return null;

            var currentMask = CreateDigitMask(expected);
            if (currentMask == expected || string.IsNullOrWhiteSpace(currentMask))
                return null;

            if (kind is null)
            {
                kind = currentKind;
                mask = currentMask;
            }
            else if (kind.Value != currentKind || mask != currentMask)
            {
                return null;
            }
        }

        if (kind is null || mask is null)
            return null;

        var componentKind = kind.Value;
        var outputMask = mask;
        return source =>
        {
            if (!TryNormalizeUsPhoneDigits(source, out var phoneDigits))
                return null;

            var componentDigits = componentKind == UsPhoneComponentKind.AreaCode
                ? phoneDigits[..3]
                : phoneDigits[3..];

            return ApplyDigitMask(componentDigits, outputMask);
        };
    }

    private static bool TryGetUsPhoneComponentKind(
        string phoneDigits,
        string expectedDigits,
        out UsPhoneComponentKind kind)
    {
        if (expectedDigits.Length == 3 &&
            expectedDigits.AsSpan().SequenceEqual(phoneDigits.AsSpan(0, 3)))
        {
            kind = UsPhoneComponentKind.AreaCode;
            return true;
        }

        if (expectedDigits.Length == 7 &&
            expectedDigits.AsSpan().SequenceEqual(phoneDigits.AsSpan(3, 7)))
        {
            kind = UsPhoneComponentKind.LocalNumber;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool TryNormalizeUsPhoneDigits(string source, out string phoneDigits)
    {
        var digits = ExtractDigits(source);
        if (digits.Length == 10)
        {
            phoneDigits = digits;
            return true;
        }

        if (digits.Length == 11 && digits[0] == '1')
        {
            phoneDigits = digits[1..];
            return true;
        }

        phoneDigits = string.Empty;
        return false;
    }

    private static Func<string, string?>? TryPhoneExtensionExtraction(IReadOnlyList<(string Source, string Expected)> examples)
    {
        foreach (var (source, expected) in examples)
        {
            if (expected.Length == 0 ||
                !expected.All(char.IsDigit) ||
                !TryExtractPhoneExtension(source, out var extension) ||
                extension != expected)
            {
                return null;
            }
        }

        return source => TryExtractPhoneExtension(source, out var extension) ? extension : null;
    }

    private static bool TryExtractPhoneExtension(string source, out string extension)
    {
        extension = string.Empty;
        for (var markerStart = 0; markerStart < source.Length; markerStart++)
        {
            if (!TryMatchPhoneExtensionMarker(source, markerStart, out var markerEnd) ||
                !IsPhoneLikeBeforeExtensionMarker(source, markerStart))
            {
                continue;
            }

            var digitStart = markerEnd;
            while (digitStart < source.Length && char.IsWhiteSpace(source[digitStart]))
                digitStart++;

            if (digitStart >= source.Length || !char.IsDigit(source[digitStart]))
                continue;

            var digitEnd = digitStart + 1;
            while (digitEnd < source.Length && char.IsDigit(source[digitEnd]))
                digitEnd++;

            if (!source.AsSpan(digitEnd).IsWhiteSpace())
                continue;

            extension = source[digitStart..digitEnd];
            return true;
        }

        return false;
    }

    private static bool TryMatchPhoneExtensionMarker(string source, int markerStart, out int markerEnd)
    {
        markerEnd = 0;
        if (!HasPhoneExtensionMarkerBoundaryBefore(source, markerStart))
            return false;

        foreach (var marker in PhoneExtensionMarkers)
        {
            if (!source.AsSpan(markerStart).StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                continue;

            var candidateEnd = markerStart + marker.Length;
            if (marker == "x")
            {
                if (candidateEnd < source.Length && char.IsLetter(source[candidateEnd]))
                    return false;

                markerEnd = candidateEnd;
                return true;
            }

            if (candidateEnd < source.Length && char.IsLetter(source[candidateEnd]))
                return false;

            var consumedPeriod = false;
            if (marker == "ext" && candidateEnd < source.Length && source[candidateEnd] == '.')
            {
                candidateEnd++;
                consumedPeriod = true;
            }

            if (!HasPhoneExtensionMarkerSeparatorAfter(source, candidateEnd, consumedPeriod))
                return false;

            markerEnd = candidateEnd;
            return true;
        }

        return false;
    }

    private static bool HasPhoneExtensionMarkerBoundaryBefore(string source, int markerStart)
    {
        if (markerStart == 0)
            return true;

        var previous = source[markerStart - 1];
        return !char.IsLetter(previous);
    }

    private static bool HasPhoneExtensionMarkerSeparatorAfter(string source, int markerEnd, bool allowImmediateDigit)
    {
        if (markerEnd >= source.Length)
            return false;

        return char.IsWhiteSpace(source[markerEnd]) ||
               (allowImmediateDigit && char.IsDigit(source[markerEnd]));
    }

    private static bool IsPhoneLikeBeforeExtensionMarker(string source, int markerStart)
    {
        var digitCount = 0;
        var sawPhoneSeparator = false;
        for (var i = 0; i < markerStart; i++)
        {
            var current = source[i];
            if (char.IsDigit(current))
            {
                digitCount++;
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                sawPhoneSeparator = true;
                continue;
            }

            if (current is '+' or '(' or ')' or '-' or '.')
            {
                sawPhoneSeparator = true;
                continue;
            }

            return false;
        }

        return digitCount is >= 7 and <= 15 &&
               (sawPhoneSeparator || digitCount is 7 or 10 or 11);
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

        foreach (var (open, close) in PairedDelimiters)
        {
            if (!examples.All(e => TryExtractBetweenLastPairedDelimiters(e.Source, open, close, out var extracted) && extracted == e.Expected))
                continue;

            return source => TryExtractBetweenLastPairedDelimiters(source, open, close, out var extracted)
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

    private static bool TryExtractBetweenLastPairedDelimiters(string source, char open, char close, out string extracted)
    {
        extracted = string.Empty;
        var searchStart = 0;
        while (searchStart < source.Length)
        {
            var openIndex = source.IndexOf(open, searchStart);
            if (openIndex < 0)
                break;

            var closeIndex = source.IndexOf(close, openIndex + 1);
            if (closeIndex < 0)
                break;

            var candidate = source[(openIndex + 1)..closeIndex].Trim();
            if (candidate.Length > 0)
                extracted = candidate;

            searchStart = closeIndex + 1;
        }

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
