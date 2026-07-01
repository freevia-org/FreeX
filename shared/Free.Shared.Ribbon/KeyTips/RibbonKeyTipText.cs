namespace Free.Shared.Ribbon.KeyTips;

public static class RibbonKeyTipText
{
    private const string FallbackAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public static string? Normalize(string? keyTip) =>
        string.IsNullOrWhiteSpace(keyTip) ? null : keyTip.Trim().ToUpperInvariant();

    public static string NormalizeOrEmpty(string? keyTip) =>
        Normalize(keyTip) ?? "";

    public static string? ApplyScopePrefix(string? keyTip, string? scopePrefix)
    {
        var normalizedKeyTip = Normalize(keyTip);
        if (normalizedKeyTip is null)
            return null;

        var normalizedPrefix = Normalize(scopePrefix);
        if (normalizedPrefix is { Length: > 0 } &&
            normalizedKeyTip.Length > normalizedPrefix.Length &&
            normalizedKeyTip.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedKeyTip[normalizedPrefix.Length..];
        }

        return normalizedKeyTip;
    }

    public static string CreateUniqueKeyTip(string? header, IReadOnlyCollection<string> used)
    {
        foreach (var character in EnumerateCandidateCharacters(header))
        {
            var candidate = NormalizeOrEmpty(character.ToString());
            if (IsAvailable(candidate, used))
                return candidate;
        }

        for (var index = 1; index <= 99; index++)
        {
            var candidate = index.ToString();
            if (IsAvailable(candidate, used))
                return candidate;
        }

        foreach (var candidate in EnumerateFallbackKeyTips())
        {
            if (IsAvailable(candidate, used))
                return candidate;
        }

        throw new InvalidOperationException("Unable to assign a unique menu keytip.");
    }

    public static bool IsTypeableKeyTip(string? keyTip) =>
        Normalize(keyTip) is { Length: > 0 } normalized &&
        normalized.All(IsTypeableKeyTipCharacter);

    public static bool IsAvailable(string? candidate, IEnumerable<string> used)
    {
        var normalizedCandidate = Normalize(candidate);
        if (normalizedCandidate is null)
            return false;

        return used
            .Select(Normalize)
            .Where(existing => existing is { Length: > 0 })
            .All(existing =>
                !string.Equals(existing, normalizedCandidate, StringComparison.OrdinalIgnoreCase) &&
                !existing!.StartsWith(normalizedCandidate, StringComparison.OrdinalIgnoreCase) &&
                !normalizedCandidate.StartsWith(existing, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<char> EnumerateCandidateCharacters(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            yield break;

        foreach (var character in EnumerateAccessKeyCharacters(header))
            yield return character;

        foreach (var character in header)
        {
            if (IsTypeableKeyTipCharacter(character))
                yield return character;
        }
    }

    private static IEnumerable<char> EnumerateAccessKeyCharacters(string header)
    {
        for (var index = 0; index < header.Length - 1; index++)
        {
            if (header[index] != '_')
                continue;

            index++;
            if (header[index] == '_')
                continue;

            if (IsTypeableKeyTipCharacter(header[index]))
                yield return header[index];
        }
    }

    private static bool IsTypeableKeyTipCharacter(char character) =>
        character is >= '0' and <= '9' or
            >= 'A' and <= 'Z' or
            >= 'a' and <= 'z';

    private static IEnumerable<string> EnumerateFallbackKeyTips()
    {
        foreach (var first in FallbackAlphabet)
        {
            foreach (var second in FallbackAlphabet)
                yield return $"{first}{second}";
        }

        foreach (var first in FallbackAlphabet)
        {
            foreach (var second in FallbackAlphabet)
            {
                foreach (var third in FallbackAlphabet)
                    yield return $"{first}{second}{third}";
            }
        }
    }
}
