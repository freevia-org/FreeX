using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

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

    // Cross-process lock tuning: FreeX has no single-instance enforcement, so two separate
    // FreeX.exe processes (or a Windows + companion instance) can each hold their own
    // RecentFilesStore over the same recent.json. Without coordination, process B's
    // load-modify-write would silently discard process A's write that landed in between B's own
    // load and save (a classic lost-update race) even though each individual write is atomic at
    // the file-replace level (AtomicFileWriter). See ReloadEntriesLocked()/AcquireCrossProcessLock().
    private const int CrossProcessLockTimeoutMs = 3000;
    private const int CrossProcessLockRetryDelayMs = 15;

    private readonly Func<DateTimeOffset> _clock;
    private readonly PlatformPathIdentityComparer _pathIdentityComparer;
    private readonly string _storePath;
    // Serializes the list mutation + file rewrite in the mutators so concurrent callers can't lose
    // updates or interleave writes. Readers that may run concurrently should use Snapshot(), which
    // copies under this same lock (enumerating the live Entries directly is not synchronized).
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

    /// <summary>
    /// Set when the most recent load/reload of <c>recent.json</c> from disk failed (e.g. the file was
    /// corrupt, truncated, or otherwise unreadable/undeserializable) — in which case the in-memory
    /// <see cref="Entries"/> silently fell back to empty (initial <see cref="Load(string, Func{DateTimeOffset}?)"/>)
    /// or to whatever was already held (a mutator's pre-write <see cref="ReloadEntriesLocked"/>), rather
    /// than throwing. Null when the last load/reload succeeded (including "file does not exist", which is
    /// not an error). Mirrors <c>AppOptions.LastPersistenceError</c> / <c>JsonSettingsStore{T}.LastError</c>
    /// so a caller (e.g. the backstage Recent list) can surface this instead of the loss being invisible.
    /// </summary>
    public string? LastLoadError { get; private set; }

    public IEnumerable<RecentFileEntry> PinnedEntries =>
        Entries.Where(entry => entry.IsPinned);

    /// <summary>
    /// A point-in-time copy of the entries taken under the lock. Use this (rather than enumerating
    /// <see cref="Entries"/> directly) from any reader that may run concurrently with a mutator, so
    /// the enumeration cannot observe a half-applied mutation or throw "collection was modified".
    /// </summary>
    public IReadOnlyList<RecentFileEntry> Snapshot()
    {
        lock (_sync)
            return Entries.ToList();
    }

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
            var raw = ReadEntriesFromDisk(storePath);
            if (raw is not null)
                store.Entries = LimitForPersistence(raw);
            store.LastLoadError = null;
        }
        catch (Exception ex)
        {
            var message = $"Failed to load recent files from '{storePath}': {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[RecentFiles] {message}");
            store.LastLoadError = message;
        }

        return store;
    }

    /// <summary>
    /// Reads and deserializes <paramref name="storePath"/> as-is (no cap applied), or returns null
    /// when the file does not exist. Shared by <see cref="LoadCore"/> (initial load, which applies
    /// the default <see cref="MaxRecentEntries"/> cap on top — see below) and
    /// <see cref="ReloadEntriesLocked"/> (the fresh-from-disk re-read every mutator performs
    /// immediately before applying its change, which must NOT collapse the list down to the default
    /// cap: a mutator invoked with a larger app-configured <c>maxRecentEntries</c> would otherwise have
    /// its own earlier writes silently truncated back to 25 on every subsequent call).
    /// </summary>
    private static List<RecentFileEntry>? ReadEntriesFromDisk(string storePath)
    {
        if (!File.Exists(storePath))
            return null;

        var json = File.ReadAllText(storePath);
        return JsonSerializer.Deserialize<List<RecentFileEntry>>(json) ?? [];
    }

    /// <summary>
    /// Re-reads <see cref="Entries"/> from disk immediately before a mutator applies its change, so a
    /// concurrent writer's already-saved update (same process via a sibling instance, or a wholly
    /// separate FreeX.exe process) is merged rather than clobbered. Must be called while holding both
    /// <see cref="_sync"/> and the cross-process lock (see <see cref="AcquireCrossProcessLock"/>) so no
    /// third writer can land between this read and the mutator's subsequent <see cref="Save"/>.
    /// Deliberately does NOT apply <see cref="LimitForPersistence"/>: capping (when applicable) is the
    /// mutator's own job at the end, using whatever cap that specific caller supplied (see
    /// <see cref="AddOrUpdate(string, int, WorkbookFileAccessIdentity?)"/>); Pin/Unpin/Remove never
    /// capped even before this fix.
    /// </summary>
    private void ReloadEntriesLocked()
    {
        try
        {
            var diskEntries = ReadEntriesFromDisk(_storePath);
            if (diskEntries is not null)
                Entries = diskEntries;
            LastLoadError = null;
        }
        catch (Exception ex)
        {
            // Best-effort: if the on-disk copy can't be read, proceed with whatever Entries already
            // holds rather than losing the caller's in-flight mutation entirely. Still record the
            // failure on LastLoadError so a caller can surface it, even though we deliberately don't
            // let it abort the in-flight mutation (see the sync-mutator callers, which write through
            // regardless — the user's pin/unpin/remove action must not silently vanish either).
            var message = $"Failed to reload recent files from '{_storePath}': {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[RecentFiles] {message}");
            LastLoadError = message;
        }
    }

    /// <summary>
    /// Acquires an exclusive, cross-process lock scoped to this store's backing file, via an
    /// exclusively-opened sibling ".lock" file (FileShare.None is honored across separate OS
    /// processes, unlike the in-process-only <see cref="_sync"/> monitor). Falls back to a no-op
    /// lock (best-effort, no cross-process serialization) if the lock file can't be created/opened
    /// at all, so a locked-down environment degrades gracefully instead of losing the user's action.
    /// </summary>
    private IDisposable AcquireCrossProcessLock()
    {
        var lockPath = _storePath + ".lock";
        try
        {
            var directory = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var deadline = Environment.TickCount64 + CrossProcessLockTimeoutMs;
            while (true)
            {
                try
                {
                    return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException) when (Environment.TickCount64 < deadline)
                {
                    Thread.Sleep(CrossProcessLockRetryDelayMs);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RecentFiles] Failed to acquire cross-process lock: {ex.Message}");
            return NoOpLock.Instance;
        }
    }

    private sealed class NoOpLock : IDisposable
    {
        public static readonly NoOpLock Instance = new();
        public void Dispose()
        {
        }
    }

    public static string GetDefaultStorePath(IApplicationDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);

        return System.IO.Path.Combine(
            pathProvider.GetApplicationDataDirectory(),
            AppProduct.Current.ProductDirectoryName,
            "recent.json");
    }

    public void AddOrUpdate(string path, WorkbookFileAccessIdentity? fileAccessIdentity = null) =>
        AddOrUpdate(path, MaxRecentEntries, fileAccessIdentity);

    /// <summary>
    /// As <see cref="AddOrUpdate(string, WorkbookFileAccessIdentity?)"/>, but caps the retained unpinned
    /// entries at <paramref name="maxRecentEntries"/> instead of the default <see cref="MaxRecentEntries"/>,
    /// so a host can honour an app-configured recent-files cap. Pinned entries are always retained.
    /// </summary>
    public void AddOrUpdate(string path, int maxRecentEntries, WorkbookFileAccessIdentity? fileAccessIdentity = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        lock (_sync)
        {
            using var crossProcessLock = AcquireCrossProcessLock();
            ReloadEntriesLocked();

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
            Entries = LimitForPersistence(Entries, maxRecentEntries);

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
            using var crossProcessLock = AcquireCrossProcessLock();
            ReloadEntriesLocked();

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
            using var crossProcessLock = AcquireCrossProcessLock();
            ReloadEntriesLocked();

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
            using var crossProcessLock = AcquireCrossProcessLock();
            ReloadEntriesLocked();

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
