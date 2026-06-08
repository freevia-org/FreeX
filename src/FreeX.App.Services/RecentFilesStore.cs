using System.Text.Json;

namespace FreeX.App.Services;

public sealed class RecentFileEntry
{
    public string Path { get; set; } = "";
    public DateTimeOffset LastOpened { get; set; }
    public bool IsPinned { get; set; }
}

public sealed class RecentFilesStore
{
    private const int MaxEntries = 25;

    private readonly Func<DateTimeOffset> _clock;
    private readonly PlatformPathIdentityComparer _pathIdentityComparer;
    private readonly string _storePath;

    private RecentFilesStore(
        string storePath,
        Func<DateTimeOffset>? clock,
        PlatformPathIdentityComparer? pathIdentityComparer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);

        _storePath = storePath;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _pathIdentityComparer = pathIdentityComparer ?? PlatformPathIdentityComparer.Current;
    }

    public static string DefaultStorePath => GetDefaultStorePath(PlatformApplicationDataPathProvider.Instance);

    public List<RecentFileEntry> Entries { get; private set; } = [];

    public IEnumerable<RecentFileEntry> PinnedEntries =>
        Entries.Where(entry => entry.IsPinned);

    public static RecentFilesStore Load() => Load(DefaultStorePath);

    public static RecentFilesStore Load(IApplicationDataPathProvider pathProvider, Func<DateTimeOffset>? clock = null) =>
        Load(GetDefaultStorePath(pathProvider), clock);

    public static RecentFilesStore Load(
        IApplicationDataPathProvider pathProvider,
        PlatformPathIdentityComparer pathIdentityComparer,
        Func<DateTimeOffset>? clock = null) =>
        Load(GetDefaultStorePath(pathProvider), pathIdentityComparer, clock);

    public static RecentFilesStore Load(string storePath, Func<DateTimeOffset>? clock = null) =>
        LoadCore(storePath, clock, pathIdentityComparer: null);

    public static RecentFilesStore Load(
        string storePath,
        PlatformPathIdentityComparer pathIdentityComparer,
        Func<DateTimeOffset>? clock = null) =>
        LoadCore(storePath, clock, pathIdentityComparer);

    private static RecentFilesStore LoadCore(
        string storePath,
        Func<DateTimeOffset>? clock,
        PlatformPathIdentityComparer? pathIdentityComparer)
    {
        var store = new RecentFilesStore(storePath, clock, pathIdentityComparer);
        try
        {
            if (File.Exists(storePath))
            {
                var json = File.ReadAllText(storePath);
                store.Entries = JsonSerializer.Deserialize<List<RecentFileEntry>>(json) ?? [];
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RecentFiles] Failed to load: {ex.Message}");
        }

        return store;
    }

    public static string GetDefaultStorePath(IApplicationDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);

        return System.IO.Path.Combine(
            pathProvider.GetApplicationDataDirectory(),
            "FreeX",
            "recent.json");
    }

    public void AddOrUpdate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var existing = Entries.FirstOrDefault(entry => PathsMatch(entry.Path, path));
        var wasPinned = existing?.IsPinned ?? false;
        Entries.RemoveAll(entry => PathsMatch(entry.Path, path));
        Entries.Insert(0, new RecentFileEntry { Path = path, LastOpened = _clock(), IsPinned = wasPinned });
        if (Entries.Count > MaxEntries)
            Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);

        Save();
    }

    public void Pin(string path)
    {
        var entry = Entries.FirstOrDefault(entry => PathsMatch(entry.Path, path));
        if (entry is null)
            return;

        entry.IsPinned = true;
        Save();
    }

    public void Unpin(string path)
    {
        var entry = Entries.FirstOrDefault(entry => PathsMatch(entry.Path, path));
        if (entry is null)
            return;

        entry.IsPinned = false;
        Save();
    }

    public void Remove(string path)
    {
        Entries.RemoveAll(entry => PathsMatch(entry.Path, path));
        Save();
    }

    private bool PathsMatch(string existingPath, string candidatePath) =>
        _pathIdentityComparer.Equals(existingPath, candidatePath);

    private void Save()
    {
        try
        {
            AtomicFileWriter.WriteAllText(_storePath, JsonSerializer.Serialize(Entries));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RecentFiles] Failed to save: {ex.Message}");
        }
    }
}
