namespace FreeX.App.Services;

/// <summary>
/// FreeX's <see cref="AppOptions"/> persistence. The generic JSON load/save/atomic-write/error-capture
/// plumbing is delegated to the shared <see cref="JsonSettingsStore{T}"/>; this type owns only the
/// FreeX-specific behaviour: the <c>options.json</c> store-path resolution, the
/// <see cref="AppOptions.NormalizePersistedCollections"/> step (run on every load and save), the
/// user-facing "options" wording in error messages, and surfacing failures via
/// <see cref="AppOptions.SetPersistenceError"/> (cleared on a successful save).
/// </summary>
public static class AppOptionsStore
{
    public const string OptionsPathEnvironmentVariable = "FREEX_OPTIONS_PATH";

    // FreeX surfaces errors as "Failed to load/save options …" (not the shared default "settings").
    private const string PersistenceNoun = "options";

    public static string StorePath =>
        ResolveStorePath(
            PlatformApplicationDataPathProvider.Instance,
            Environment.GetEnvironmentVariable(OptionsPathEnvironmentVariable));

    public static string GetDefaultStorePath(IApplicationDataPathProvider pathProvider) =>
        AppStoragePathPlanner.GetOptionsFilePath(pathProvider);

    public static string ResolveStorePath(
        IApplicationDataPathProvider pathProvider,
        string? overridePath) =>
        AppStoragePathPlanner.ResolveOptionsFilePath(pathProvider, overridePath);

    public static AppOptions Load() => LoadFromPath(StorePath);

    public static AppOptions Load(
        IApplicationDataPathProvider pathProvider,
        string? overridePath = null) =>
        LoadFromPath(ResolveStorePath(pathProvider, overridePath));

    public static AppOptions LoadFromPath(string storePath)
    {
        var (options, error) = JsonSettingsStore<AppOptions>.LoadFromPath(storePath, noun: PersistenceNoun);
        if (error is not null)
        {
            options.SetPersistenceError(error);
            return options;
        }

        options.NormalizePersistedCollections();
        return options;
    }

    public static bool Save(AppOptions options) => SaveToPath(options, StorePath);

    public static bool SaveToPath(AppOptions options, string storePath)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.NormalizePersistedCollections();
        var error = JsonSettingsStore<AppOptions>.SaveToPath(options, storePath, noun: PersistenceNoun);
        options.SetPersistenceError(error);
        return error is null;
    }
}
