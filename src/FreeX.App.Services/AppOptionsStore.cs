using System.Text.Json;

namespace FreeX.App.Services;

public static class AppOptionsStore
{
    public const string OptionsPathEnvironmentVariable = "FREEX_OPTIONS_PATH";

    private static readonly JsonSerializerOptions StoreJsonOptions = new()
    {
        WriteIndented = true
    };

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
        try
        {
            if (File.Exists(storePath))
            {
                var json = File.ReadAllText(storePath);
                var options = JsonSerializer.Deserialize<AppOptions>(json) ?? new();
                options.NormalizePersistedCollections();
                return options;
            }
        }
        catch (Exception ex)
        {
            var options = new AppOptions();
            options.SetPersistenceError($"Failed to load options from '{storePath}': {ex.Message}");
            return options;
        }

        return new AppOptions();
    }

    public static bool Save(AppOptions options) => SaveToPath(options, StorePath);

    public static bool SaveToPath(AppOptions options, string storePath)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            options.NormalizePersistedCollections();
            AtomicFileWriter.WriteAllText(
                storePath,
                JsonSerializer.Serialize(options, StoreJsonOptions));
            options.SetPersistenceError(null);
            return true;
        }
        catch (Exception ex)
        {
            options.SetPersistenceError($"Failed to save options to '{storePath}': {ex.Message}");
            return false;
        }
    }
}
