using System.Text.Json;

namespace Free.Shared.AppServices;

/// <summary>
/// Generic, app-neutral persistence for a settings POCO as a JSON file. Centralizes the
/// load/save ceremony that each app would otherwise hand-roll: a safe load that falls back to
/// defaults when the file is missing or corrupt, an atomic save through <see cref="AtomicFileWriter"/>,
/// and a store path derived from the ambient <see cref="AppProduct"/> plus a caller-supplied file name
/// (so each app's settings land under its own data folder, e.g. <c>%APPDATA%\FreeW\settings.json</c>).
///
/// <para>
/// Pure <c>net10.0</c>: no WPF, no UI. The settings <em>model</em> stays app-specific; only this
/// persistence mechanism is shared. <typeparamref name="T"/> must be JSON round-trippable and have a
/// public parameterless constructor so a fresh default can be produced.
/// </para>
///
/// <para>
/// A store is constructed against a concrete store path (see <see cref="ForProductFile"/> /
/// <see cref="ForPath"/>); load/save are instance methods so a caller can hold one store and read/write
/// repeatedly. The static <see cref="LoadFromPath"/> / <see cref="SaveToPath"/> helpers cover one-shot use.
/// </para>
/// </summary>
public sealed class JsonSettingsStore<T>
    where T : class, new()
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>The absolute path this store loads from and saves to.</summary>
    public string StorePath { get; }

    /// <summary>Last load/save failure message (null when the last operation succeeded).</summary>
    public string? LastError { get; private set; }

    // r191: set when Load could not read an existing file. See Save.
    private bool _loadFailed;

    private JsonSettingsStore(string storePath, JsonSerializerOptions? jsonOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);

        StorePath = storePath;
        _jsonOptions = jsonOptions ?? DefaultJsonOptions;
    }

    /// <summary>
    /// A store rooted at the ambient app product's data folder (<see cref="AppProduct.Current"/>) with the
    /// given file name. An optional <paramref name="overridePath"/> (e.g. from an env var / CLI flag) wins
    /// when set. Respects whatever <see cref="AppProduct"/> the host installed at startup.
    /// </summary>
    public static JsonSettingsStore<T> ForProductFile(
        string fileName,
        IApplicationDataPathProvider? pathProvider = null,
        string? overridePath = null,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var resolvedPath = !string.IsNullOrWhiteSpace(overridePath)
            ? overridePath
            : GetProductFilePath(fileName, pathProvider ?? PlatformApplicationDataPathProvider.Instance);

        return new JsonSettingsStore<T>(resolvedPath, jsonOptions);
    }

    /// <summary>A store rooted at an explicit absolute path (tests, custom locations).</summary>
    public static JsonSettingsStore<T> ForPath(string storePath, JsonSerializerOptions? jsonOptions = null) =>
        new(storePath, jsonOptions);

    /// <summary>
    /// The default settings path for <paramref name="fileName"/> under the ambient product's data folder:
    /// <c>{appDataDir}/{ProductDirectoryName}/{fileName}</c>.
    /// </summary>
    public static string GetProductFilePath(string fileName, IApplicationDataPathProvider pathProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(pathProvider);

        return Path.Combine(
            pathProvider.GetApplicationDataDirectory(),
            AppStoragePathPlanner.ProductDirectoryName,
            fileName);
    }

    /// <summary>
    /// Loads the settings from <see cref="StorePath"/>. A missing file yields a fresh default; a corrupt
    /// or unreadable file also yields a fresh default and records <see cref="LastError"/> (never throws).
    /// </summary>
    public T Load()
    {
        var result = LoadFromPath(StorePath, _jsonOptions);
        LastError = result.Error;
        // r191: remembered so the first Save can move the unreadable file aside instead of writing
        // the empty default over it. See Save.
        _loadFailed = result.Error is not null;
        return result.Value;
    }

    /// <summary>
    /// Atomically writes the settings to <see cref="StorePath"/>, creating the directory if needed.
    /// Returns true on success; on failure returns false and records <see cref="LastError"/> (never throws).
    /// </summary>
    public bool Save(T settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // r191 (backlog item 24): a failed Load returns a fresh default and records LastError -- and
        // no caller in any of the three apps reads LastError. So a settings file that could not be
        // parsed (a hand edit, a sync conflict, a half-written file from a killed process) came back
        // as "empty", and the next ordinary save wrote that emptiness over it, destroying whatever
        // the user had. FreeW's Quick Parts gallery was the case that surfaced it: the library loads
        // at startup and any Save/Remove afterwards persists the whole in-memory set.
        //
        // Rather than require every caller to remember a check nobody has ever made, the store keeps
        // the unreadable file: it is moved aside once, before the first overwrite, so the data is
        // still there to recover. Mirrors AutosaveSnapshotStore.QuarantineCandidate, which does the
        // same for a corrupt autosave rather than deleting it.
        if (_loadFailed)
        {
            _loadFailed = false;
            TryQuarantineUnreadableFile();
        }

        var error = SaveToPath(settings, StorePath, _jsonOptions);
        LastError = error;
        return error is null;
    }

    private void TryQuarantineUnreadableFile()
    {
        try
        {
            if (!File.Exists(StorePath))
                return;

            // Best effort throughout: quarantining is a courtesy, and failing to do it must never
            // stop the user saving. A single fixed name is deliberate -- repeated corruption should
            // not accumulate files without bound, and the most recent unreadable copy is the one
            // worth keeping.
            var quarantine = StorePath + ".unreadable";
            File.Copy(StorePath, quarantine, overwrite: true);
        }
        catch
        {
            // Ignored: see above.
        }
    }

    /// <summary>
    /// Stateless safe load from an explicit path. Returns the deserialized value (or a fresh default) and
    /// a non-null error message when the file existed but could not be read/parsed. Never throws.
    /// </summary>
    /// <param name="noun">
    /// The user-facing word for what is being persisted in the error message (default <c>"settings"</c>),
    /// e.g. an app can pass <c>"options"</c> to read <c>"Failed to load options from '…'"</c>.
    /// </param>
    public static (T Value, string? Error) LoadFromPath(
        string storePath,
        JsonSerializerOptions? jsonOptions = null,
        string noun = "settings")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);

        try
        {
            if (File.Exists(storePath))
            {
                var json = File.ReadAllText(storePath);
                var value = JsonSerializer.Deserialize<T>(json, jsonOptions ?? DefaultJsonOptions) ?? new T();
                return (value, null);
            }
        }
        catch (Exception ex)
        {
            return (new T(), $"Failed to load {noun} from '{storePath}': {ex.Message}");
        }

        return (new T(), null);
    }

    /// <summary>
    /// Stateless atomic save to an explicit path. Returns null on success or an error message on failure
    /// (never throws). The directory is created if missing; the write goes through a sibling temp file.
    /// </summary>
    /// <param name="noun">
    /// The user-facing word for what is being persisted in the error message (default <c>"settings"</c>),
    /// e.g. an app can pass <c>"options"</c> to read <c>"Failed to save options to '…'"</c>.
    /// </param>
    public static string? SaveToPath(
        T settings,
        string storePath,
        JsonSerializerOptions? jsonOptions = null,
        string noun = "settings")
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);

        try
        {
            AtomicFileWriter.WriteAllText(
                storePath,
                JsonSerializer.Serialize(settings, jsonOptions ?? DefaultJsonOptions));
            return null;
        }
        catch (Exception ex)
        {
            return $"Failed to save {noun} to '{storePath}': {ex.Message}";
        }
    }
}
