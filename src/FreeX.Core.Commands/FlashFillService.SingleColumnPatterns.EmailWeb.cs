using static FreeX.Core.Commands.FlashFillTextPrimitives;

namespace FreeX.Core.Commands;

public static partial class FlashFillService
{
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
}
