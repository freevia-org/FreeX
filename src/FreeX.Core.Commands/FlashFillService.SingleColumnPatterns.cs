using System.Globalization;
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
        City,
        State,
        Zip5,
        Zip,
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
        UsAddressComponentKind.City,
        UsAddressComponentKind.State,
        UsAddressComponentKind.Zip5,
        UsAddressComponentKind.Zip,
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

    private static Func<string, string?>? TryEmailDisplayName(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryFormatDottedEmailUserName(e.Source, out var displayName) && displayName == e.Expected))
            return null;

        return s => TryFormatDottedEmailUserName(s, out var displayName) ? displayName : null;
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

    private static bool TryFormatDottedEmailUserName(string source, out string displayName)
    {
        displayName = string.Empty;
        var atIndex = source.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0)
            return false;

        var userName = source[..atIndex];
        var plusIndex = userName.IndexOf('+');
        if (plusIndex >= 0)
            userName = userName[..plusIndex];

        if (userName.Length == 0)
            return false;

        if (!userName.Contains('.', StringComparison.Ordinal) &&
            !userName.Contains('_', StringComparison.Ordinal) &&
            !userName.Contains('-', StringComparison.Ordinal))
        {
            return false;
        }

        var parts = userName.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        var nameParts = new string[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!TryNormalizeEmailDisplayNamePart(parts[i], out nameParts[i]))
                return false;
        }

        displayName = string.Join(' ', nameParts.Select(ToProperCase));
        return true;
    }

    private static bool TryNormalizeEmailDisplayNamePart(string part, out string normalized)
    {
        normalized = string.Empty;
        var endExclusive = part.Length;
        while (endExclusive > 0 && char.IsDigit(part[endExclusive - 1]))
            endExclusive--;

        if (endExclusive == 0)
            return false;

        var candidate = part[..endExclusive];
        if (!candidate.Any(char.IsLetter) || candidate.Any(char.IsDigit))
            return false;

        normalized = candidate;
        return true;
    }

    private static Func<string, string?>? TryEmailLocalPartWithoutPlusTag(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryExtractEmailLocalPartWithoutPlusTag(e.Source, out var localPart) && localPart == e.Expected))
            return null;

        return source => TryExtractEmailLocalPartWithoutPlusTag(source, out var localPart) ? localPart : null;
    }

    private static Func<string, string?>? TryEmailDomainStem(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryExtractEmailDomainStem(e.Source, out var domainStem) && domainStem == e.Expected))
            return null;

        return source => TryExtractEmailDomainStem(source, out var domainStem) ? domainStem : null;
    }

    private static bool TryExtractEmailLocalPartWithoutPlusTag(string source, out string localPart)
    {
        localPart = string.Empty;
        var atIndex = source.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0)
            return false;

        var plusIndex = source.IndexOf('+', 0, atIndex);
        if (plusIndex <= 0)
            return false;

        localPart = source[..plusIndex];
        return localPart.Length > 0;
    }

    private static bool TryExtractEmailDomainStem(string source, out string domainStem)
    {
        domainStem = string.Empty;

        var atIndex = source.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0 || atIndex == source.Length - 1)
            return false;

        var domain = source[(atIndex + 1)..].Trim();
        if (domain.Length == 0 || domain.Any(char.IsWhiteSpace))
            return false;

        var lastDotIndex = domain.LastIndexOf('.');
        if (lastDotIndex <= 0 || lastDotIndex == domain.Length - 1)
            return false;

        domainStem = domain[..lastDotIndex];
        return domainStem.Length > 0;
    }

    private static Func<string, string?>? TryWebAddressCleanup(IReadOnlyList<(string Source, string Expected)> examples)
    {
        var allowHost = true;
        var allowHostWithoutWww = true;
        var allowDomainStem = true;

        foreach (var (source, expected) in examples)
        {
            if (!TryExtractWebAddressParts(source, out var parts))
                return null;

            allowHost &= expected == parts.Host;
            allowHostWithoutWww &= expected == parts.HostWithoutWww;
            allowDomainStem &= expected == parts.DomainStem;

            if (!allowHost && !allowHostWithoutWww && !allowDomainStem)
                return null;
        }

        WebAddressOutputKind kind;
        if (allowDomainStem)
            kind = WebAddressOutputKind.DomainStem;
        else if (allowHostWithoutWww && !allowHost)
            kind = WebAddressOutputKind.HostWithoutWww;
        else if (allowHost)
            kind = WebAddressOutputKind.Host;
        else
            return null;

        return source => TryExtractWebAddressParts(source, out var parts)
            ? GetWebAddressOutput(parts, kind)
            : null;
    }

    private static bool TryExtractWebAddressParts(string source, out WebAddressParts parts)
    {
        parts = default;

        var candidate = source.Trim();
        if (candidate.Length == 0 ||
            candidate.Any(char.IsWhiteSpace) ||
            candidate.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        var hasHttpScheme =
            candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (!hasHttpScheme && candidate.Contains("://", StringComparison.Ordinal))
            return false;

        var isBareWebAddress =
            candidate.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
            LooksLikeBareWebHost(candidate) ||
            candidate.Contains('/', StringComparison.Ordinal) ||
            candidate.Contains('?', StringComparison.Ordinal) ||
            candidate.Contains('#', StringComparison.Ordinal);
        if (!hasHttpScheme && !isBareWebAddress)
            return false;

        var uriText = hasHttpScheme
            ? candidate
            : "https://" + candidate;
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            uri.UserInfo.Length > 0)
        {
            return false;
        }

        if (!TryNormalizeWebHost(uri.Host, out var host))
            return false;

        var hostWithoutWww = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? host[4..]
            : host;
        if (!TryRemoveFinalDottedToken(hostWithoutWww, out var domainStem))
            return false;

        parts = new WebAddressParts(host, hostWithoutWww, domainStem);
        return true;
    }

    private static bool LooksLikeBareWebHost(string candidate)
    {
        var lastDotIndex = candidate.LastIndexOf('.');
        if (lastDotIndex <= 0 || lastDotIndex == candidate.Length - 1)
            return false;

        for (var i = 0; i < candidate.Length; i++)
        {
            var c = candidate[i];
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '.')
                return false;
        }

        var suffix = candidate[(lastDotIndex + 1)..];
        if (suffix.Length == 2)
            return suffix.All(char.IsLetter);

        return suffix.Equals("com", StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals("org", StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals("net", StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals("edu", StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals("gov", StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals("io", StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals("co", StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals("biz", StringComparison.OrdinalIgnoreCase) ||
               suffix.Equals("info", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeWebHost(string host, out string normalized)
    {
        normalized = string.Empty;

        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 ||
            !host.Contains('.', StringComparison.Ordinal) ||
            !host.Any(char.IsLetter))
        {
            return false;
        }

        var labels = host.Split('.');
        if (labels.Length < 2)
            return false;

        foreach (var label in labels)
        {
            if (label.Length == 0 ||
                label[0] == '-' ||
                label[^1] == '-' ||
                !label.Any(char.IsLetterOrDigit))
            {
                return false;
            }

            for (var i = 0; i < label.Length; i++)
            {
                var c = label[i];
                if (!char.IsLetterOrDigit(c) && c != '-')
                    return false;
            }
        }

        normalized = host;
        return true;
    }

    private static string GetWebAddressOutput(WebAddressParts parts, WebAddressOutputKind kind) =>
        kind switch
        {
            WebAddressOutputKind.Host => parts.Host,
            WebAddressOutputKind.HostWithoutWww => parts.HostWithoutWww,
            WebAddressOutputKind.DomainStem => parts.DomainStem,
            _ => parts.Host
        };

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

    private static Func<string, string?>? TryUsAddressComponentExtraction(
        IReadOnlyList<(string Source, string Expected)> examples)
    {
        UsAddressComponentKind? componentKind = null;

        foreach (var (source, expected) in examples)
        {
            if (!TryParseUsAddress(source, out var parts) ||
                !TryFindUsAddressComponent(parts, expected, out var currentKind))
            {
                return null;
            }

            if (componentKind is null)
                componentKind = currentKind;
            else if (componentKind.Value != currentKind)
                return null;
        }

        if (componentKind is null)
            return null;

        var kind = componentKind.Value;
        return source =>
            TryParseUsAddress(source, out var parts) &&
            TryGetUsAddressComponent(parts, kind, out var component)
                ? component
                : null;
    }

    private static bool TryParseUsAddress(string source, out UsAddressParts parts)
    {
        parts = default;
        var segments = source.Split(',', StringSplitOptions.TrimEntries);
        if (segments.Length != 3 ||
            segments[0].Length == 0 ||
            segments[1].Length == 0 ||
            segments[2].Length == 0)
        {
            return false;
        }

        var stateZipTokens = segments[2].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (stateZipTokens.Length != 2 ||
            !IsUsStateAbbreviation(stateZipTokens[0]) ||
            !IsUsZipCode(stateZipTokens[1]))
        {
            return false;
        }

        parts = new UsAddressParts(segments[0], segments[1], stateZipTokens[0], stateZipTokens[1]);
        return true;
    }

    private static bool TryFindUsAddressComponent(
        UsAddressParts parts,
        string expected,
        out UsAddressComponentKind componentKind)
    {
        componentKind = default;
        var matched = false;

        foreach (var candidateKind in UsAddressComponentKinds)
        {
            if (!TryGetUsAddressComponent(parts, candidateKind, out var component) ||
                component != expected)
            {
                continue;
            }

            if (matched)
                return false;

            componentKind = candidateKind;
            matched = true;
        }

        return matched;
    }

    private static bool TryGetUsAddressComponent(
        UsAddressParts parts,
        UsAddressComponentKind kind,
        out string component)
    {
        component = kind switch
        {
            UsAddressComponentKind.Street => parts.Street,
            UsAddressComponentKind.City => parts.City,
            UsAddressComponentKind.State => parts.State,
            UsAddressComponentKind.Zip5 => parts.Zip[..5],
            UsAddressComponentKind.Zip when parts.Zip.Contains('-', StringComparison.Ordinal) => parts.Zip,
            UsAddressComponentKind.StateZip => parts.State + " " + parts.Zip,
            _ => string.Empty
        };

        return component.Length > 0;
    }

    private static bool IsUsStateAbbreviation(string value) =>
        value.Length == 2 && value.All(char.IsLetter);

    private static bool IsUsZipCode(string value) =>
        IsFiveDigitZipCode(value) ||
        value.Length == 10 &&
        value[5] == '-' &&
        IsFiveDigitZipCode(value[..5]) &&
        value[6..].All(char.IsDigit);

    private static bool IsFiveDigitZipCode(string value) =>
        value.Length == 5 && value.All(char.IsDigit);

    private static Func<string, string?>? TryDateComponentExtraction(IReadOnlyList<(string Source, string Expected)> examples)
    {
        int? partIndex = null;

        foreach (var (source, expected) in examples)
        {
            if (!TrySplitDateLikeComponents(source, out var components))
                return null;

            var matches = new List<int>(1);
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] == expected)
                    matches.Add(i);
            }

            if (matches.Count != 1)
                return null;

            if (partIndex is null)
                partIndex = matches[0];
            else if (partIndex.Value != matches[0])
                return null;
        }

        if (partIndex is null)
            return null;

        var idx = partIndex.Value;
        return source => TrySplitDateLikeComponents(source, out var components)
            ? components[idx]
            : null;
    }

    private static bool TrySplitDateLikeComponents(string source, out string[] components)
    {
        foreach (var separator in DateComponentSeparators)
        {
            var parts = source.Split(separator, StringSplitOptions.TrimEntries);
            if (parts.Length != 3 ||
                parts.Any(part => part.Length == 0 || part.Any(c => !char.IsDigit(c))))
            {
                continue;
            }

            var yearIndex = Array.FindIndex(parts, part => part.Length == 4);
            if (yearIndex < 0 ||
                Array.FindLastIndex(parts, part => part.Length == 4) != yearIndex ||
                (yearIndex != 0 && yearIndex != 2))
            {
                continue;
            }

            if (!int.TryParse(parts[yearIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
                year < 1000)
            {
                continue;
            }

            var dateish = true;
            for (var i = 0; i < parts.Length; i++)
            {
                if (i == yearIndex)
                    continue;

                if (parts[i].Length is < 1 or > 2 ||
                    !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
                    value is < 1 or > 31)
                {
                    dateish = false;
                    break;
                }
            }

            if (!dateish)
                continue;

            components = parts;
            return true;
        }

        components = [];
        return false;
    }

    private static Func<string, string?>? TryEmbeddedDateExtraction(IReadOnlyList<(string Source, string Expected)> examples)
    {
        DateOutputPattern? outputPattern = null;
        var changedAny = false;

        foreach (var (source, expected) in examples)
        {
            if (!TryFindEmbeddedDateLikeValue(source, out var sourceDate, out _) ||
                !TryParseDateLikeValue(expected, out var expectedDate, out var currentPattern) ||
                sourceDate != expectedDate)
            {
                return null;
            }

            if (outputPattern is null)
                outputPattern = currentPattern;
            else if (outputPattern.Value != currentPattern)
                return null;

            changedAny |= !string.Equals(source, expected, StringComparison.Ordinal);
        }

        if (outputPattern is null || !changedAny)
            return null;

        var pattern = outputPattern.Value;
        return source => TryFindEmbeddedDateLikeValue(source, out var date, out _)
            ? FormatDateParts(date, pattern)
            : null;
    }

    private static Func<string, string?>? TryDateNormalization(IReadOnlyList<(string Source, string Expected)> examples)
    {
        DateOutputPattern? outputPattern = null;
        var changedAny = false;

        foreach (var (source, expected) in examples)
        {
            if (!TryParseDateLikeValue(source, out var sourceDate, out _) ||
                !TryParseDateLikeValue(expected, out var expectedDate, out var currentPattern) ||
                sourceDate != expectedDate)
            {
                return null;
            }

            if (outputPattern is null)
                outputPattern = currentPattern;
            else if (outputPattern.Value != currentPattern)
                return null;

            changedAny |= !string.Equals(source, expected, StringComparison.Ordinal);
        }

        if (outputPattern is null || !changedAny)
            return null;

        var pattern = outputPattern.Value;
        return source => TryParseDateLikeValue(source, out var date, out _)
            ? FormatDateParts(date, pattern)
            : null;
    }

    private static bool TryParseDateLikeValue(
        string source,
        out DateParts date,
        out DateOutputPattern pattern)
    {
        date = default;
        pattern = default;

        if (!TrySplitDateLikeText(source, out var parts, out var separator))
            return false;

        var yearIndex = Array.FindIndex(parts, part => part.Length == 4);
        if (yearIndex < 0 ||
            Array.FindLastIndex(parts, part => part.Length == 4) != yearIndex ||
            (yearIndex != 0 && yearIndex != 2))
        {
            return false;
        }

        var order = yearIndex == 0
            ? new[] { DatePartKind.Year, DatePartKind.Month, DatePartKind.Day }
            : new[] { DatePartKind.Month, DatePartKind.Day, DatePartKind.Year };

        if (!TryCreateDateParts(parts, order, out date))
            return false;

        pattern = new DateOutputPattern(
            order[0],
            order[1],
            order[2],
            separator,
            parts[Array.IndexOf(order, DatePartKind.Year)].Length,
            parts[Array.IndexOf(order, DatePartKind.Month)].Length,
            parts[Array.IndexOf(order, DatePartKind.Day)].Length);
        return true;
    }

    private static bool TrySplitDateLikeText(string source, out string[] parts, out char separator)
    {
        foreach (var candidate in DateComponentSeparators)
        {
            var split = source.Split(candidate, StringSplitOptions.TrimEntries);
            if (split.Length == 3 &&
                split.All(part => part.Length > 0 && part.All(char.IsDigit)))
            {
                parts = split;
                separator = candidate;
                return true;
            }
        }

        parts = [];
        separator = default;
        return false;
    }

    private static bool TryFindEmbeddedDateLikeValue(
        string source,
        out DateParts date,
        out DateOutputPattern pattern)
    {
        date = default;
        pattern = default;

        var found = false;
        for (var start = 0; start < source.Length; start++)
        {
            if (!char.IsDigit(source[start]) ||
                (start > 0 && char.IsDigit(source[start - 1])))
            {
                continue;
            }

            foreach (var separator in DateComponentSeparators)
            {
                if (!TryReadDateTokenAt(source, start, separator, out var endExclusive) ||
                    !HasDateTokenBoundary(source, start, endExclusive, separator))
                {
                    continue;
                }

                var token = source[start..endExclusive];
                if (!TryParseDateLikeValue(token, out var currentDate, out var currentPattern))
                    continue;

                if (found)
                    return false;

                date = currentDate;
                pattern = currentPattern;
                found = true;
            }
        }

        return found;
    }

    private static bool TryReadDateTokenAt(
        string source,
        int start,
        char separator,
        out int endExclusive)
    {
        endExclusive = start;
        var index = start;

        for (var group = 0; group < 3; group++)
        {
            var groupStart = index;
            while (index < source.Length && char.IsDigit(source[index]))
                index++;

            if (index == groupStart)
                return false;

            if (group < 2)
            {
                if (index >= source.Length || source[index] != separator)
                    return false;

                index++;
            }
        }

        endExclusive = index;
        return true;
    }

    private static bool HasDateTokenBoundary(
        string source,
        int start,
        int endExclusive,
        char separator)
    {
        if (start > 0 && char.IsLetterOrDigit(source[start - 1]))
            return false;

        if (endExclusive >= source.Length)
            return true;

        var next = source[endExclusive];
        if (char.IsLetterOrDigit(next))
            return false;

        return next != separator ||
            endExclusive == source.Length - 1 ||
            !char.IsDigit(source[endExclusive + 1]);
    }

    private static bool TryCreateDateParts(string[] parts, DatePartKind[] order, out DateParts date)
    {
        date = default;
        if (parts.Length != 3 || order.Length != 3)
            return false;

        if (!TryGetDatePartValue(parts, order, DatePartKind.Year, out var year) ||
            !TryGetDatePartValue(parts, order, DatePartKind.Month, out var month) ||
            !TryGetDatePartValue(parts, order, DatePartKind.Day, out var day) ||
            year is < 1000 or > 9999 ||
            month is < 1 or > 12 ||
            day < 1 ||
            day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateParts(year, month, day);
        return true;
    }

    private static bool TryGetDatePartValue(
        string[] parts,
        DatePartKind[] order,
        DatePartKind kind,
        out int value)
    {
        value = 0;
        var index = Array.IndexOf(order, kind);
        return index >= 0 &&
            int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatDateParts(DateParts date, DateOutputPattern pattern) =>
        string.Join(
            pattern.Separator,
            new[]
            {
                FormatDatePart(date, pattern.First, pattern),
                FormatDatePart(date, pattern.Second, pattern),
                FormatDatePart(date, pattern.Third, pattern)
            });

    private static string FormatDatePart(DateParts date, DatePartKind kind, DateOutputPattern pattern)
    {
        var value = kind switch
        {
            DatePartKind.Year => date.Year,
            DatePartKind.Month => date.Month,
            _ => date.Day
        };

        var width = kind switch
        {
            DatePartKind.Year => pattern.YearWidth,
            DatePartKind.Month => pattern.MonthWidth,
            _ => pattern.DayWidth
        };

        return value.ToString("D" + width.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
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
