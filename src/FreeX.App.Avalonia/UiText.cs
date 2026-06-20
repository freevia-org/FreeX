using FreeX.App.Localization;

namespace FreeX.App.Avalonia;

/// <summary>
/// Thin Avalonia-shell accessor over the portable <see cref="Loc"/> localization provider.
/// Keeps call sites short (<c>UiText.Get("Key")</c>) and gives the shell a single seam to route
/// UI text through, so macOS inherits culture-aware strings from the shared catalog.
/// </summary>
internal static class UiText
{
    public static string Get(string key) => Loc.Get(key);

    public static string Format(string key, params object?[] args) => Loc.Format(key, args);

    public static string GetNeutral(string key) => Loc.GetNeutral(key);
}
