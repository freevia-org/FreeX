using System.Globalization;

namespace Free.Shared.AppServices;

/// <summary>
/// Contract for app-owned option models that can repair loaded or user-entered values before use.
/// </summary>
public interface INormalizableApplicationOptions
{
    /// <summary>Clamps and defaults the option model into its valid runtime shape.</summary>
    void Normalize();
}

/// <summary>
/// Common normalization rules for the sister apps' small persisted option models.
/// </summary>
public static class ApplicationOptionsNormalizer
{
    public const int DefaultRecentFilesCap = 15;
    public const int MinRecentFilesCap = 0;
    public const int MaxRecentFilesCap = RecentFilesStore.MaxRecentEntries;
    public const string SystemDefaultLanguage = "";

    public static int NormalizeRecentFilesCap(int value) =>
        Math.Clamp(value, MinRecentFilesCap, MaxRecentFilesCap);

    public static string NormalizeDefaultSaveFormat(string? value, string defaultFormat) =>
        string.IsNullOrWhiteSpace(value) ? defaultFormat : value;

    public static string NormalizeUiLanguage(string? value) =>
        value?.Trim() ?? SystemDefaultLanguage;

    public static bool TryParseRecentFilesCap(string? text, out int cap)
    {
        if (int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out cap)
            && cap >= MinRecentFilesCap
            && cap <= MaxRecentFilesCap)
        {
            return true;
        }

        cap = 0;
        return false;
    }
}

/// <summary>
/// Thin normalizing wrapper over <see cref="JsonSettingsStore{T}"/> for app-specific option models.
/// </summary>
public sealed class NormalizingJsonSettingsStore<T>
    where T : class, INormalizableApplicationOptions, new()
{
    private readonly JsonSettingsStore<T> _store;

    private NormalizingJsonSettingsStore(JsonSettingsStore<T> store) => _store = store;

    public string StorePath => _store.StorePath;

    public string? LastError => _store.LastError;

    public static NormalizingJsonSettingsStore<T> ForProductFile(
        string fileName,
        IApplicationDataPathProvider? pathProvider = null,
        string? overridePath = null) =>
        new(JsonSettingsStore<T>.ForProductFile(fileName, pathProvider, overridePath));

    public static NormalizingJsonSettingsStore<T> ForPath(string storePath) =>
        new(JsonSettingsStore<T>.ForPath(storePath));

    public T Load()
    {
        var options = _store.Load();
        options.Normalize();
        return options;
    }

    public bool Save(T options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Normalize();
        return _store.Save(options);
    }
}
