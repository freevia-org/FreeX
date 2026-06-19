using Free.Shared.AppServices;

namespace FreeP.App.Host;

/// <summary>
/// FreeP's settings store: a thin app-specific façade over the shared, neutral
/// <see cref="JsonSettingsStore{T}"/>. Persists <see cref="FreePOptions"/> as <c>settings.json</c> under
/// FreeP's own data folder (because <c>Program.Main</c> installed <c>AppProduct = "FreeP"</c>, so the shared
/// path planner resolves <c>%APPDATA%\FreeP\settings.json</c>). Load is safe and save is atomic; both are
/// best-effort and never throw. Mirrors FreeWOptionsStore.
/// </summary>
public sealed class FreePOptionsStore
{
    /// <summary>The settings file name under FreeP's product data folder.</summary>
    public const string FileName = "settings.json";

    private readonly JsonSettingsStore<FreePOptions> _store;

    private FreePOptionsStore(JsonSettingsStore<FreePOptions> store) => _store = store;

    /// <summary>The absolute path this store reads from / writes to.</summary>
    public string StorePath => _store.StorePath;

    /// <summary>Last load/save error surfaced by the shared store (null when the last op succeeded).</summary>
    public string? LastError => _store.LastError;

    /// <summary>A store rooted at FreeP's product data folder (tests pass an explicit provider/override).</summary>
    public static FreePOptionsStore Create(
        IApplicationDataPathProvider? pathProvider = null,
        string? overridePath = null) =>
        new(JsonSettingsStore<FreePOptions>.ForProductFile(FileName, pathProvider, overridePath));

    /// <summary>A store rooted at an explicit absolute path (tests).</summary>
    public static FreePOptionsStore ForPath(string storePath) =>
        new(JsonSettingsStore<FreePOptions>.ForPath(storePath));

    /// <summary>Loads and normalizes the settings; missing/corrupt files degrade to defaults.</summary>
    public FreePOptions Load()
    {
        var options = _store.Load();
        options.Normalize();
        return options;
    }

    /// <summary>Normalizes then atomically saves; returns false (with <see cref="LastError"/>) on failure.</summary>
    public bool Save(FreePOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Normalize();
        return _store.Save(options);
    }
}
