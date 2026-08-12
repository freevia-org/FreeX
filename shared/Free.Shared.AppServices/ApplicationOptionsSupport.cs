using System.Globalization;
using System.Text.Json;

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
/// Common persisted values exposed by the sister apps' basic Options surfaces.
/// </summary>
public interface IBasicApplicationOptions : INormalizableApplicationOptions
{
    int RecentFilesCap { get; set; }

    string DefaultSaveFormat { get; set; }

    string UiLanguage { get; set; }
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
/// Storage boundary for a normalized application-options model. Production hosts use the JSON-backed
/// implementation while isolated windows and tests can use the shared in-memory implementation.
/// </summary>
public interface IApplicationOptionsStore<T>
    where T : class, INormalizableApplicationOptions, new()
{
    string StorePath { get; }

    string? LastError { get; }

    T Load();

    bool Save(T options);
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

/// <summary>
/// Shared facade for app-specific options models. The model stays app-owned; the file name, product-folder
/// resolution, safe load, normalization, and atomic save ceremony are common to the sister apps.
/// </summary>
public sealed class ApplicationOptionsStore<T> : IApplicationOptionsStore<T>
    where T : class, INormalizableApplicationOptions, new()
{
    public const string DefaultFileName = "settings.json";

    private readonly NormalizingJsonSettingsStore<T> _store;

    private ApplicationOptionsStore(NormalizingJsonSettingsStore<T> store) => _store = store;

    /// <summary>The absolute path this store reads from / writes to.</summary>
    public string StorePath => _store.StorePath;

    /// <summary>Last load/save error surfaced by the shared store (null when the last op succeeded).</summary>
    public string? LastError => _store.LastError;

    /// <summary>A store rooted at the ambient app product data folder.</summary>
    public static ApplicationOptionsStore<T> Create(
        IApplicationDataPathProvider? pathProvider = null,
        string? overridePath = null,
        string fileName = DefaultFileName) =>
        new(NormalizingJsonSettingsStore<T>.ForProductFile(fileName, pathProvider, overridePath));

    /// <summary>A store rooted at an explicit absolute path (tests / transient isolated windows).</summary>
    public static ApplicationOptionsStore<T> ForPath(string storePath) =>
        new(NormalizingJsonSettingsStore<T>.ForPath(storePath));

    /// <summary>Loads and normalizes options; missing/corrupt files degrade to defaults.</summary>
    public T Load() => _store.Load();

    /// <summary>Normalizes then atomically saves; returns false (with <see cref="LastError"/>) on failure.</summary>
    public bool Save(T options) => _store.Save(options);
}

/// <summary>
/// Process-local application-options storage for isolated hosts and tests. Values use a JSON snapshot so
/// callers observe the same save/load boundary as the persistent store without creating any files.
/// </summary>
public sealed class InMemoryApplicationOptionsStore<T> : IApplicationOptionsStore<T>
    where T : class, INormalizableApplicationOptions, new()
{
    private byte[] _snapshot;

    public InMemoryApplicationOptionsStore(T? initialOptions = null, string? storePath = null)
    {
        StorePath = storePath ?? $"in-memory://{typeof(T).Name}/{Guid.NewGuid():N}";
        var options = initialOptions ?? new T();
        options.Normalize();
        _snapshot = JsonSerializer.SerializeToUtf8Bytes(options);
    }

    public string StorePath { get; }

    public string? LastError { get; private set; }

    public static InMemoryApplicationOptionsStore<T> ForProductFile(
        IApplicationDataPathProvider? pathProvider = null,
        string? overridePath = null,
        string fileName = ApplicationOptionsStore<T>.DefaultFileName) =>
        new(
            storePath: JsonSettingsStore<T>
                .ForProductFile(fileName, pathProvider, overridePath)
                .StorePath);

    public T Load()
    {
        try
        {
            var options = JsonSerializer.Deserialize<T>(_snapshot) ?? new T();
            options.Normalize();
            LastError = null;
            return options;
        }
        catch (Exception ex)
        {
            LastError = $"Failed to load options from memory: {ex.Message}";
            var options = new T();
            options.Normalize();
            return options;
        }
    }

    public bool Save(T options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Normalize();

        try
        {
            _snapshot = JsonSerializer.SerializeToUtf8Bytes(options);
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Failed to save options to memory: {ex.Message}";
            return false;
        }
    }
}
