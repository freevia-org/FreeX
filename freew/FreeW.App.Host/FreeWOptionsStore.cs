using Free.Shared.AppServices;

namespace FreeW.App.Host;

/// <summary>
/// FreeW's settings store: a thin app-specific façade over the shared, neutral
/// <see cref="NormalizingJsonSettingsStore{T}"/>. Persists <see cref="FreeWOptions"/> as <c>settings.json</c> under
/// FreeW's own data folder (because <c>Program.Main</c> installed <c>AppProduct = "FreeW"</c>, so the
/// shared path planner resolves <c>%APPDATA%\FreeW\settings.json</c>).
///
/// <para>
/// Load is safe (missing/corrupt file → normalized defaults) and save is atomic; both are best-effort and
/// never throw, so a settings hiccup can never block startup or a save. The only FreeW-specific bit here
/// is the model and the post-load <see cref="FreeWOptions.Normalize"/> call — everything else is shared.
/// </para>
/// </summary>
public sealed class FreeWOptionsStore
{
    /// <summary>The settings file name under FreeW's product data folder.</summary>
    public const string FileName = "settings.json";

    private readonly NormalizingJsonSettingsStore<FreeWOptions> _store;

    private FreeWOptionsStore(NormalizingJsonSettingsStore<FreeWOptions> store) => _store = store;

    /// <summary>The absolute path this store reads from / writes to.</summary>
    public string StorePath => _store.StorePath;

    /// <summary>Last load/save error surfaced by the shared store (null when the last op succeeded).</summary>
    public string? LastError => _store.LastError;

    /// <summary>
    /// A store rooted at FreeW's product data folder. <paramref name="pathProvider"/> defaults to the
    /// platform provider; tests pass an explicit one (and/or an <paramref name="overridePath"/>) to keep
    /// settings off the real user profile.
    /// </summary>
    public static FreeWOptionsStore Create(
        IApplicationDataPathProvider? pathProvider = null,
        string? overridePath = null) =>
        new(NormalizingJsonSettingsStore<FreeWOptions>.ForProductFile(FileName, pathProvider, overridePath));

    /// <summary>A store rooted at an explicit absolute path (tests).</summary>
    public static FreeWOptionsStore ForPath(string storePath) =>
        new(NormalizingJsonSettingsStore<FreeWOptions>.ForPath(storePath));

    /// <summary>Loads and normalizes the settings; missing/corrupt files degrade to defaults.</summary>
    public FreeWOptions Load() => _store.Load();

    /// <summary>Normalizes then atomically saves; returns false (with <see cref="LastError"/>) on failure.</summary>
    public bool Save(FreeWOptions options) => _store.Save(options);
}
