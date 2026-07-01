using System.Globalization;

namespace Free.Shared.Shell;

/// <summary>
/// Localized string resolution the neutral backstage planners need (greeting daypart
/// labels, recent-file automation names, relative date formats). Apps supply their own
/// implementation so the shared shell stays free of any single app's resource catalog;
/// FreeX delegates to its <c>UiText</c>, FreeW/Avalonia will provide their own.
/// </summary>
/// <remarks>
/// The surface intentionally mirrors a resource-manager lookup (<see cref="Get"/> /
/// <see cref="Format"/>) rather than a fixed property per string, because the backstage
/// planners reference many keys and apps already own a localized catalog keyed the same way.
/// </remarks>
public interface IBackstageStrings
{
    /// <summary>Resolves the localized string for <paramref name="key"/>.</summary>
    string Get(string key);

    /// <summary>Resolves <paramref name="key"/> and formats it with <paramref name="args"/>.</summary>
    string Format(string key, params object?[] args);
}

/// <summary>
/// Backstage string adapter for app-owned resource catalogs.
/// </summary>
public sealed class ResourceBackstageStrings : IBackstageStrings
{
    private readonly Func<string, string> _get;
    private readonly Func<string, object?[], string> _format;

    public ResourceBackstageStrings(
        Func<string, string> get,
        Func<string, object?[], string>? format = null)
    {
        _get = get ?? throw new ArgumentNullException(nameof(get));
        _format = format ?? FormatResolvedString;
    }

    public string Get(string key) => _get(key);

    public string Format(string key, params object?[] args) =>
        _format(key, args);

    private string FormatResolvedString(string key, object?[] args) =>
        args is { Length: > 0 }
            ? string.Format(CultureInfo.CurrentCulture, _get(key), args)
            : _get(key);
}

/// <summary>
/// Neutral fallback used until an app installs its own <see cref="IBackstageStrings"/> via
/// <see cref="BackstageStrings.Current"/>. Echoes the key (and appends arguments) so the
/// shared backstage planners remain usable standalone without crashing on missing strings.
/// </summary>
public sealed class DefaultBackstageStrings : IBackstageStrings
{
    public static DefaultBackstageStrings Instance { get; } = new();

    public string Get(string key) => key;

    public string Format(string key, params object?[] args) =>
        args is { Length: > 0 }
            ? string.Format(CultureInfo.CurrentCulture, key, args)
            : key;
}

/// <summary>
/// Ambient provider for backstage strings. An app sets <see cref="Current"/> once at startup
/// (before any backstage planner runs). Defaults to <see cref="DefaultBackstageStrings"/>.
/// Mirrors <see cref="ShellStrings"/>.
/// </summary>
public static class BackstageStrings
{
    public static IBackstageStrings Current { get; set; } = DefaultBackstageStrings.Instance;
}
