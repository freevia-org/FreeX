using static FreeX.Core.Commands.FlashFillTextPrimitives;

namespace FreeX.Core.Commands;

public static partial class FlashFillService
{
    private static Func<string, string?>? TryEmailDisplayName(IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryFormatEmailUserNameDisplayName(e.Source, out var displayName) && displayName == e.Expected))
            return null;

        return s => TryFormatEmailUserNameDisplayName(s, out var displayName) ? displayName : null;
    }

    private static bool TryFormatEmailUserNameDisplayName(string source, out string displayName)
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
            return TryFormatCamelCaseEmailUserName(userName, out displayName);
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

    private static bool TryFormatCamelCaseEmailUserName(string userName, out string displayName)
    {
        displayName = string.Empty;

        if (!TryNormalizeEmailDisplayNamePart(userName, out var normalized) ||
            !normalized.All(char.IsLetter))
        {
            return false;
        }

        var nameParts = new List<string>();
        var wordStart = 0;
        for (var i = 1; i < normalized.Length; i++)
        {
            if (!char.IsUpper(normalized[i]) || !char.IsLower(normalized[i - 1]))
                continue;

            nameParts.Add(ToProperCase(normalized[wordStart..i]));
            wordStart = i;
        }

        if (nameParts.Count == 0)
            return false;

        nameParts.Add(ToProperCase(normalized[wordStart..]));
        displayName = string.Join(' ', nameParts);
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
        var allowDomainStem = true;
        var allowRootDomainStem = true;

        foreach (var (source, expected) in examples)
        {
            if (!TryExtractEmailDomainStem(source, out var domainStem))
                return null;

            allowDomainStem &= expected == domainStem;
            allowRootDomainStem &= TryExtractEmailRootDomainStem(source, out var rootDomainStem) &&
                                   expected == rootDomainStem;

            if (!allowDomainStem && !allowRootDomainStem)
                return null;
        }

        if (allowDomainStem)
            return source => TryExtractEmailDomainStem(source, out var domainStem) ? domainStem : null;

        if (allowRootDomainStem)
            return source => TryExtractEmailRootDomainStem(source, out var rootDomainStem) ? rootDomainStem : null;

        return null;
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

    private static bool TryExtractEmailRootDomainStem(string source, out string rootDomainStem)
    {
        rootDomainStem = string.Empty;

        var atIndex = source.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0 || atIndex == source.Length - 1)
            return false;

        var domain = source[(atIndex + 1)..].Trim();
        if (domain.Length == 0 || domain.Any(char.IsWhiteSpace))
            return false;

        return TryExtractRootDomainStem(domain, out rootDomainStem);
    }

    private static Func<string, string?>? TryWebAddressCleanup(IReadOnlyList<(string Source, string Expected)> examples)
    {
        var allowHost = true;
        var allowHostWithoutWww = true;
        var allowDomainStem = true;
        var allowRootDomainStem = true;

        foreach (var (source, expected) in examples)
        {
            if (!TryExtractWebAddressParts(source, out var parts))
                return null;

            allowHost &= expected == parts.Host;
            allowHostWithoutWww &= expected == parts.HostWithoutWww;
            allowDomainStem &= expected == parts.DomainStem;
            allowRootDomainStem &= expected == parts.RootDomainStem;

            if (!allowHost && !allowHostWithoutWww && !allowDomainStem && !allowRootDomainStem)
                return null;
        }

        WebAddressOutputKind kind;
        if (allowDomainStem)
            kind = WebAddressOutputKind.DomainStem;
        else if (allowRootDomainStem)
            kind = WebAddressOutputKind.RootDomainStem;
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

    private static Func<string, string?>? TryExtractFinalUrlPathSegmentStem(
        IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryGetFinalUrlPathSegmentStem(e.Source, out var stem) && stem == e.Expected))
            return null;

        return source => TryGetFinalUrlPathSegmentStem(source, out var stem) ? stem : null;
    }

    private static Func<string, string?>? TryExtractFinalUrlPathSegmentRawSlugStem(
        IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryGetFinalUrlPathSegmentRawSlugStem(e.Source, out var stem) && stem == e.Expected))
            return null;

        return source => TryGetFinalUrlPathSegmentRawSlugStem(source, out var stem) ? stem : null;
    }

    private static Func<string, string?>? TryExtractFinalUrlPathSegmentSlugTitle(
        IReadOnlyList<(string Source, string Expected)> examples)
    {
        if (!examples.All(e => TryGetFinalUrlPathSegmentSlugTitle(e.Source, out var title) && title == e.Expected))
            return null;

        return source => TryGetFinalUrlPathSegmentSlugTitle(source, out var title) ? title : null;
    }

    private static Func<string, string?>? TryUrlQueryParameterValue(IReadOnlyList<(string Source, string Expected)> examples)
    {
        List<string>? candidateNames = null;

        foreach (var (source, expected) in examples)
        {
            if (expected.Length == 0 ||
                !TryGetMatchingQueryParameterNames(source, expected, out var currentNames) ||
                currentNames.Count == 0)
            {
                return null;
            }

            if (candidateNames is null)
            {
                candidateNames = currentNames;
                continue;
            }

            for (var i = candidateNames.Count - 1; i >= 0; i--)
            {
                if (!currentNames.Contains(candidateNames[i], StringComparer.Ordinal))
                    candidateNames.RemoveAt(i);
            }

            if (candidateNames.Count == 0)
                return null;
        }

        if (candidateNames is null || candidateNames.Count != 1)
            return null;

        var parameterName = candidateNames[0];
        return source => TryGetFirstNonEmptyQueryParameterValue(source, parameterName, out var value) ? value : null;
    }

    private static bool TryGetFinalUrlPathSegmentStem(string source, out string stem)
    {
        stem = string.Empty;
        if (!TryGetFinalUrlPathSegmentStemRange(source, out var start, out var endExclusive))
            return false;

        stem = SliceSegment(source.Trim(), start, endExclusive);
        return true;
    }

    private static bool TryGetFinalUrlPathSegmentStemRange(
        string source,
        out int stemStart,
        out int stemEndExclusive)
    {
        stemStart = 0;
        stemEndExclusive = 0;

        var candidate = source.Trim();
        if (!IsHttpOrHttpsUrlCandidate(candidate) ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            uri.UserInfo.Length > 0 ||
            !TryNormalizeWebHost(uri.Host, out _))
        {
            return false;
        }

        var authorityStart = candidate.IndexOf("://", StringComparison.Ordinal) + 3;
        var pathStart = candidate.IndexOf('/', authorityStart);
        if (pathStart < 0)
            return false;

        var queryIndex = candidate.IndexOf('?', pathStart);
        var fragmentIndex = candidate.IndexOf('#', pathStart);
        var pathEndExclusive = MinPositiveIndexOrLength(candidate.Length, queryIndex, fragmentIndex);
        if (pathEndExclusive <= pathStart + 1)
            return false;

        var segmentEnd = pathEndExclusive - 1;
        if (candidate[segmentEnd] == '/')
            return false;

        var lastSlashIndex = candidate.LastIndexOf('/', segmentEnd, segmentEnd - pathStart + 1);
        var segmentStart = lastSlashIndex + 1;
        TrimSegment(candidate, ref segmentStart, ref segmentEnd);
        if (segmentStart > segmentEnd)
            return false;

        var segmentLength = segmentEnd - segmentStart + 1;
        var dotIndex = candidate.LastIndexOf('.', segmentEnd, segmentLength);
        if (dotIndex <= segmentStart || dotIndex == segmentEnd)
            return false;

        stemStart = segmentStart;
        var stemEnd = dotIndex - 1;
        TrimSegment(candidate, ref stemStart, ref stemEnd);
        if (stemStart > stemEnd)
            return false;

        stemEndExclusive = stemEnd + 1;
        return true;
    }

    private static bool TryGetFinalUrlPathSegmentRawSlugStem(string source, out string stem)
    {
        return TryGetFinalUrlPathSegmentSlugStem(source, out stem, requireExtensionlessSegment: true);
    }

    private static bool TryGetFinalUrlPathSegmentSlugTitle(string source, out string title)
    {
        title = string.Empty;
        if (!TryGetFinalUrlPathSegmentSlugStem(source, out var stem) ||
            !TryFormatSlugStemAsTitle(stem, out title))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetFinalUrlPathSegmentSlugStem(
        string source,
        out string stem,
        bool requireExtensionlessSegment = false)
    {
        stem = string.Empty;

        var candidate = source.Trim();
        if (candidate.Length == 0 ||
            candidate.Any(char.IsWhiteSpace) ||
            !IsHttpOrHttpsUrlCandidate(candidate) ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            uri.UserInfo.Length > 0 ||
            !TryNormalizeWebHost(uri.Host, out _))
        {
            return false;
        }

        var authorityStart = candidate.IndexOf("://", StringComparison.Ordinal) + 3;
        var pathStart = candidate.IndexOf('/', authorityStart);
        if (pathStart < 0)
            return false;

        var queryIndex = candidate.IndexOf('?', pathStart);
        var fragmentIndex = candidate.IndexOf('#', pathStart);
        var pathEndExclusive = MinPositiveIndexOrLength(candidate.Length, queryIndex, fragmentIndex);
        if (pathEndExclusive <= pathStart + 1)
            return false;

        var segmentEnd = pathEndExclusive - 1;
        if (candidate[segmentEnd] == '/')
            return false;

        var lastSlashIndex = candidate.LastIndexOf('/', segmentEnd, segmentEnd - pathStart + 1);
        var segmentStart = lastSlashIndex + 1;
        TrimSegment(candidate, ref segmentStart, ref segmentEnd);
        if (segmentStart > segmentEnd)
            return false;

        var segmentLength = segmentEnd - segmentStart + 1;
        var dotIndex = candidate.LastIndexOf('.', segmentEnd, segmentLength);
        if (requireExtensionlessSegment && dotIndex >= segmentStart)
            return false;

        var stemStart = segmentStart;
        var stemEnd = segmentEnd;
        if (dotIndex >= segmentStart)
        {
            if (dotIndex == segmentStart || dotIndex == segmentEnd)
                return false;

            stemEnd = dotIndex - 1;
            TrimSegment(candidate, ref stemStart, ref stemEnd);
            if (stemStart > stemEnd)
                return false;
        }

        var rawStem = SliceSegment(candidate, stemStart, stemEnd + 1);
        if (!HasValidPercentEscapes(rawStem))
            return false;

        try
        {
            stem = Uri.UnescapeDataString(rawStem);
        }
        catch (UriFormatException)
        {
            stem = string.Empty;
            return false;
        }

        return stem.Length > 0;
    }

    private static bool TryFormatSlugStemAsTitle(string stem, out string title)
    {
        title = string.Empty;

        if (stem.Length == 0 ||
            !stem.Any(char.IsLetter) ||
            stem.All(c => char.IsDigit(c) || c == '-' || c == '_' || c == ' '))
        {
            return false;
        }

        var parts = stem.Split(['-', '_', ' '], StringSplitOptions.None);
        if (parts.Length == 0)
            return false;

        var titleParts = new List<string>(parts.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0 ||
                part.All(char.IsDigit) ||
                !part.Any(char.IsLetter))
            {
                return false;
            }

            for (var j = 0; j < part.Length; j++)
            {
                if (!char.IsLetterOrDigit(part[j]))
                    return false;
            }

            AddSlugTitleWords(part, titleParts);
        }

        title = string.Join(' ', titleParts);
        return title.Length > 0;
    }

    private static void AddSlugTitleWords(string part, List<string> titleParts)
    {
        var wordStart = 0;
        for (var i = 1; i < part.Length; i++)
        {
            if (char.IsUpper(part[i]) && char.IsLower(part[i - 1]))
            {
                titleParts.Add(ToProperCase(part[wordStart..i]));
                wordStart = i;
            }
        }

        titleParts.Add(ToProperCase(part[wordStart..]));
    }

    private static bool HasValidPercentEscapes(string source)
    {
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != '%')
                continue;

            if (i + 2 >= source.Length ||
                !Uri.IsHexDigit(source[i + 1]) ||
                !Uri.IsHexDigit(source[i + 2]))
            {
                return false;
            }

            i += 2;
        }

        return true;
    }

    private static bool TryGetMatchingQueryParameterNames(
        string source,
        string expected,
        out List<string> parameterNames)
    {
        parameterNames = [];
        if (!TryGetDecodedQueryParameters(source, out var parameters))
            return false;

        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, _) in parameters)
        {
            if (!seenNames.Add(name))
                continue;

            if (TryGetFirstNonEmptyQueryParameterValue(parameters, name, out var value) &&
                value == expected)
            {
                parameterNames.Add(name);
            }
        }

        return true;
    }

    private static bool TryGetFirstNonEmptyQueryParameterValue(
        string source,
        string parameterName,
        out string value)
    {
        value = string.Empty;
        return TryGetDecodedQueryParameters(source, out var parameters) &&
               TryGetFirstNonEmptyQueryParameterValue(parameters, parameterName, out value);
    }

    private static bool TryGetFirstNonEmptyQueryParameterValue(
        IReadOnlyList<(string Name, string Value)> parameters,
        string parameterName,
        out string value)
    {
        foreach (var (name, currentValue) in parameters)
        {
            if (name == parameterName && currentValue.Length > 0)
            {
                value = currentValue;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetDecodedQueryParameters(
        string source,
        out List<(string Name, string Value)> parameters)
    {
        parameters = [];

        if (!TryCreateHttpWebUri(source, out var uri) || uri.Query.Length <= 1)
            return false;

        var query = uri.Query[1..];
        foreach (var segment in query.Split('&', StringSplitOptions.None))
        {
            if (segment.Length == 0)
                continue;

            var equalsIndex = segment.IndexOf('=');
            if (equalsIndex <= 0)
                continue;

            var rawName = segment[..equalsIndex];
            var rawValue = segment[(equalsIndex + 1)..];
            if (rawValue.Length == 0)
                continue;

            if (!TryDecodeQueryComponent(rawName, out var name) ||
                !TryDecodeQueryComponent(rawValue, out var value))
            {
                return false;
            }

            if (name.Length == 0 || value.Length == 0)
                continue;

            parameters.Add((name, value));
        }

        return parameters.Count > 0;
    }

    private static bool TryCreateHttpWebUri(string source, out Uri uri)
    {
        uri = null!;

        var candidate = source.Trim();
        if (candidate.Length == 0 || candidate.Any(char.IsWhiteSpace))
            return false;

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
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var parsedUri) ||
            parsedUri is null ||
            (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps) ||
            parsedUri.UserInfo.Length > 0)
        {
            return false;
        }

        uri = parsedUri;
        return TryNormalizeWebHost(uri.Host, out _);
    }

    private static bool TryDecodeQueryComponent(string source, out string decoded)
    {
        try
        {
            decoded = Uri.UnescapeDataString(source.Replace('+', ' '));
            return true;
        }
        catch (UriFormatException)
        {
            decoded = string.Empty;
            return false;
        }
    }

    private static bool IsHttpOrHttpsUrlCandidate(string source)
    {
        var candidate = source.TrimStart();
        return candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static int MinPositiveIndexOrLength(int length, int firstIndex, int secondIndex)
    {
        if (firstIndex < 0)
            return secondIndex < 0 ? length : secondIndex;

        if (secondIndex < 0)
            return firstIndex;

        return Math.Min(firstIndex, secondIndex);
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

        if (!TryExtractRootDomainStem(hostWithoutWww, out var rootDomainStem))
            return false;

        parts = new WebAddressParts(host, hostWithoutWww, domainStem, rootDomainStem);
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

    private static bool TryExtractRootDomainStem(string host, out string rootDomainStem)
    {
        rootDomainStem = string.Empty;

        if (!TryNormalizeWebHost(host, out var normalized))
            return false;

        var labels = normalized.Split('.');
        if (labels.Length < 2)
            return false;

        rootDomainStem = labels[^2];
        return rootDomainStem.Length > 0;
    }

    private static string GetWebAddressOutput(WebAddressParts parts, WebAddressOutputKind kind) =>
        kind switch
        {
            WebAddressOutputKind.Host => parts.Host,
            WebAddressOutputKind.HostWithoutWww => parts.HostWithoutWww,
            WebAddressOutputKind.DomainStem => parts.DomainStem,
            WebAddressOutputKind.RootDomainStem => parts.RootDomainStem,
            _ => parts.Host
        };
}
