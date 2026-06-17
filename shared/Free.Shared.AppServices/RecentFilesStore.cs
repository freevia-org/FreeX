using System.Text.Json;
using System.Text.Json.Serialization;

namespace Free.Shared.AppServices;

public sealed class RecentFileEntry
{
    public string Path { get; set; } = "";
    public DateTimeOffset LastOpened { get; set; }
    public bool IsPinned { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkbookFileAccessIdentity? FileAccessIdentity { get; set; }
}

public sealed class RecentFilesStore
{
    public const int MaxRecentEntries = 25;

    private readonly Func<DateTimeOffset> _clock;
    private readonly PlatformPathIdentityComparer _pathIdentityComparer;
    private readonly string _storePath;
    // Mutators can be invoked from background threads (async open/save flows); serialize the
    // list mutation + file rewrite so concurrent callers don't lose updates or interleave writes.
    private readonly object _sync = new();

    public RecentFilesStore(
        string storePath,
        Func<DateTimeOffset>? clock = null)
        : this(storePath, clock, pathIdentityComparer: null)
    {
    }

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
                store.Entries = LimitForPersistence(JsonSerializer.Deserialize<List<RecentFileEntry>>(json) ?? []);
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
            AppProduct.Current.ProductDirectoryName,
            "recent.json");
    }

    public void AddOrUpdate(string path, WorkbookFileAccessIdentity? fileAccessIdentity = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        lock (_sync)
        {
            var existing = FindEntryByPath(path);
            var wasPinned = existing?.IsPinned ?? false;
            var identity = TryPreparePersistentIdentity(fileAccessIdentity, path) ??
                TryPreparePersistentIdentity(existing?.FileAccessIdentity, path);
            RemoveEntriesByPath(path);
            Entries.Insert(0, new RecentFileEntry
            {
                Path = path,
                LastOpened = _clock(),
                IsPinned = wasPinned,
                FileAccessIdentity = identity,
            });
            Entries = LimitForPersistence(Entries);

            Save();
        }
    }

    public static List<RecentFileEntry> LimitForPersistence(
        IEnumerable<RecentFileEntry> entries,
        int maxRecentEntries = MaxRecentEntries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRecentEntries);

        var limited = new List<RecentFileEntry>();
        var unpinnedCount = 0;
        foreach (var entry in entries)
        {
            if (entry.IsPinned)
            {
                limited.Add(entry);
                continue;
            }

            if (unpinnedCount >= maxRecentEntries)
                continue;

            limited.Add(entry);
            unpinnedCount++;
        }

        return limited;
    }

    public void Pin(string path)
    {
        lock (_sync)
        {
            var entry = FindEntryByPath(path);
            if (entry is null)
                return;

            entry.IsPinned = true;
            Save();
        }
    }

    public void Unpin(string path)
    {
        lock (_sync)
        {
            var entry = FindEntryByPath(path);
            if (entry is null)
                return;

            entry.IsPinned = false;
            Save();
        }
    }

    public void Remove(string path)
    {
        lock (_sync)
        {
            RemoveEntriesByPath(path);
            Save();
        }
    }

    private RecentFileEntry? FindEntryByPath(string path)
    {
        foreach (var entry in Entries)
        {
            if (PathsMatch(entry, path))
                return entry;
        }

        return null;
    }

    private void RemoveEntriesByPath(string path) =>
        Entries.RemoveAll(entry => PathsMatch(entry, path));

    private bool PathsMatch(RecentFileEntry existingEntry, string candidatePath) =>
        PathsMatch(existingEntry.Path, candidatePath);

    private bool PathsMatch(string existingPath, string candidatePath) =>
        _pathIdentityComparer.Equals(existingPath, candidatePath);

    private static WorkbookFileAccessIdentity? TryPreparePersistentIdentity(
        WorkbookFileAccessIdentity? identity,
        string path) =>
        identity is not null &&
        identity.HasBookmark &&
        identity.TryWithLocalPath(path, out var movedIdentity) &&
        movedIdentity?.HasBookmark == true
            ? movedIdentity
            : null;

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
