namespace Free.Shared.Localization;

/// <summary>
/// Resolves localized UI text while keeping app-owned English fallbacks out of the visible UI when
/// a resource key has not landed yet.
/// </summary>
public static class LocalizedFallbackTextResolver
{
    public static string Resolve(
        string key,
        string fallback,
        Func<string, string?>? getText,
        bool stripMnemonics = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(fallback);

        var resolved = getText?.Invoke(key);
        var text = RequiresFallback(key, resolved) ? fallback : resolved!;

        return stripMnemonics ? StripMnemonicMarkers(text) : text;
    }

    public static bool RequiresFallback(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return string.IsNullOrEmpty(value) ||
            string.Equals(value, key, StringComparison.Ordinal) ||
            IsMissingResourceToken(value, key);
    }

    public static bool IsMissingResourceToken(string? value, string? key = null)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        if (!string.IsNullOrEmpty(key))
            return string.Equals(value, LocalizedTextCatalog.CreateMissingText(key), StringComparison.Ordinal);

        return value.StartsWith("[[", StringComparison.Ordinal) &&
            value.EndsWith("]]", StringComparison.Ordinal);
    }

    public static string StripMnemonicMarkers(string? text) =>
        text?.Replace("_", string.Empty, StringComparison.Ordinal) ?? string.Empty;
}
