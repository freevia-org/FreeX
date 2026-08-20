using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Free.Shared.AppServices;

/// <summary>
/// Sidecar metadata saved alongside every autosave snapshot.
/// </summary>
public sealed class AutosaveSidecar
{
    [JsonPropertyName("originalFilePath")]
    public string? OriginalFilePath { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("timestampUtc")]
    public string? TimestampUtc { get; set; }

    [JsonPropertyName("snapshotId")]
    public string? SnapshotId { get; set; }

    /// <summary>
    /// Optional stable identity of the in-memory document instance that produced this snapshot
    /// (e.g. FreeX's <c>Workbook.Id</c>). See <see cref="IAutosaveSnapshotSource.DocumentId"/> for
    /// why this exists — it lets crash-recovery dedup distinguish genuine multi-window siblings
    /// over one shared document from independent documents that merely share a saved file path.
    /// Null for apps/sources that do not supply one.
    /// </summary>
    [JsonPropertyName("documentId")]
    public string? DocumentId { get; set; }
}

/// <summary>
/// Represents a recovery candidate found on disk.
/// </summary>
public sealed class AutosaveRecoveryCandidate
{
    public AutosaveRecoveryCandidate(string snapshotPath, string sidecarPath, AutosaveSidecar sidecar)
    {
        SnapshotPath = snapshotPath;
        SidecarPath = sidecarPath;
        Sidecar = sidecar;
    }

    public string SnapshotPath { get; }
    public string SidecarPath { get; }
    public AutosaveSidecar Sidecar { get; }
}

/// <summary>
/// Manages the autosave recovery directory: path resolution, sidecar serialization,
/// and recovery-candidate enumeration. All pure-logic, no WPF dependency.
/// </summary>
public sealed class AutosaveSnapshotStore
{
    public const string RecoveryDirectoryName = "Recovery";
    private const string SnapshotExtension = ".fxl";
    private const string SidecarExtension = ".sidecar.json";
    private const string LockExtension = ".lock";

    /// <summary>
    /// Unique identifier for this process launch. Embedded in every snapshot ID so that a
    /// recycled OS process-ID (PID) can never clobber a snapshot from a prior crashed session
    /// that the user has not yet recovered.
    /// </summary>
    public static readonly Guid LaunchId = Guid.NewGuid();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _recoveryDirectory;

    public AutosaveSnapshotStore(string recoveryDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryDirectory);
        _recoveryDirectory = recoveryDirectory;
    }

    /// <summary>
    /// Creates a store backed by <c>%LOCALAPPDATA%\FreeX\Recovery</c>.
    /// </summary>
    public static AutosaveSnapshotStore CreateDefault(IApplicationDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        var localData = pathProvider.GetApplicationDataDirectory();
        return new AutosaveSnapshotStore(
            Path.Combine(localData, AppStoragePathPlanner.ProductDirectoryName, RecoveryDirectoryName));
    }

    /// <summary>
    /// Computes the snapshot path for a given stable session ID.
    /// </summary>
    public string GetSnapshotPath(string snapshotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        return Path.Combine(_recoveryDirectory, snapshotId + SnapshotExtension);
    }

    /// <summary>
    /// Computes the sidecar path for a given stable session ID.
    /// </summary>
    public string GetSidecarPath(string snapshotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        return Path.Combine(_recoveryDirectory, snapshotId + SidecarExtension);
    }

    /// <summary>
    /// Computes the ownership-lock path for a given stable session ID. See
    /// <see cref="TryAcquireOwnershipLock"/> / <see cref="ExcludeLiveOwned"/>.
    /// </summary>
    public string GetLockPath(string snapshotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        return Path.Combine(_recoveryDirectory, snapshotId + LockExtension);
    }

    /// <summary>
    /// Round134-remediation: claims an OS-level advisory lock (<c>FileShare.None</c>) on
    /// <paramref name="snapshotId"/>'s slot, marking it "live" to <see cref="ExcludeLiveOwned"/>
    /// for as long as the returned handle stays open. <see cref="AutosaveSnapshotCoordinator"/>
    /// (the only production caller) acquires this once at construction and holds it for the
    /// entire lifetime of the window/session it backs, releasing it on <c>Dispose</c>.
    /// <para>
    /// Deliberately an OS file lock rather than a PID, timestamp, or heartbeat marker: the OS
    /// releases a process's open handles automatically and immediately the instant it exits, for
    /// ANY reason — clean shutdown, crash, or a hard kill. That means there is no "the owner is
    /// gone but the marker still says live" state to detect or expire: a snapshot whose owning
    /// window/process has died is instantly and correctly reported not-live by
    /// <see cref="ExcludeLiveOwned"/>, with no staleness window and no extra cleanup pass needed.
    /// A PID-based check would need recycled-PID protection; a heartbeat/timestamp marker would
    /// need an arbitrary expiry window (too short: false "gone" for a slow process; too long:
    /// reintroduces the orphaning bug this remediates). The OS lock has neither failure mode.
    /// </para>
    /// <para>
    /// Best-effort: returns null if the lock cannot be acquired (e.g. a read-only or otherwise
    /// inaccessible recovery directory). The caller must never let this block startup — a null
    /// result simply means this session's own snapshot degrades to the pre-fix "no liveness
    /// filtering" behavior, exactly like every other best-effort guarantee in this store.
    /// </para>
    /// </summary>
    public FileStream? TryAcquireOwnershipLock(string snapshotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        try
        {
            Directory.CreateDirectory(_recoveryDirectory);
            return new FileStream(
                GetLockPath(snapshotId), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Serializes the sidecar to a JSON string (no file I/O — testable).
    /// </summary>
    public static string SerializeSidecar(AutosaveSidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        return JsonSerializer.Serialize(sidecar, JsonOptions);
    }

    /// <summary>
    /// Attempts to deserialize a sidecar from a JSON string (no file I/O — testable).
    /// Returns null if the JSON is missing or corrupt.
    /// </summary>
    public static AutosaveSidecar? TryDeserializeSidecar(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<AutosaveSidecar>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Determines whether a snapshot should be written based on dirty state and generation tracking.
    /// </summary>
    public static bool ShouldSnapshot(bool workbookDirty, int currentGeneration, int lastSnapshotGeneration)
    {
        return workbookDirty && currentGeneration != lastSnapshotGeneration;
    }

    /// <summary>
    /// Enumerates all valid recovery candidates in the recovery directory.
    /// Stale or corrupt sidecar files are silently skipped.
    /// </summary>
    public IReadOnlyList<AutosaveRecoveryCandidate> EnumerateCandidates()
    {
        if (!Directory.Exists(_recoveryDirectory))
            return [];

        var candidates = new List<AutosaveRecoveryCandidate>();

        try
        {
            foreach (var snapshotPath in Directory.EnumerateFiles(_recoveryDirectory, "*" + SnapshotExtension))
            {
                try
                {
                    if (!File.Exists(snapshotPath))
                        continue;

                    // Derive the sidecar from the snapshot name
                    var baseName = Path.GetFileNameWithoutExtension(snapshotPath);
                    var sidecarPath = Path.Combine(_recoveryDirectory, baseName + SidecarExtension);

                    if (!File.Exists(sidecarPath))
                        continue;

                    var json = File.ReadAllText(sidecarPath);
                    var sidecar = TryDeserializeSidecar(json);
                    if (sidecar is null)
                        continue;

                    var candidate = new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar);

                    // A snapshot is either an OPC/ZIP package (FreeW .docx / FreeP .pptx) or, for
                    // FreeX, a plain JSON document (NativeJsonAdapter.Save writes JSON, not a ZIP —
                    // this store's ".fxl" naming is shared cosmetic, not a format promise). If it is
                    // not readable in whichever format it actually is (e.g. truncated by a crash
                    // mid-write), it can never be recovered — quarantine it and skip, so it is NEVER
                    // offered. This stops the modal "Could not recover the document: End of Central
                    // Directory record could not be found" (or JSON parse) error at the source, rather
                    // than surfacing it once on the open attempt.
                    if (!IsReadableSnapshot(snapshotPath))
                    {
                        QuarantineCandidate(candidate);
                        continue;
                    }

                    candidates.Add(candidate);
                }
                catch
                {
                    // Skip corrupt or inaccessible entries.
                }
            }
        }
        catch
        {
            // Directory enumeration failed — return what we have.
        }

        return candidates;
    }

    /// <summary>
    /// Round134-remediation: filters <paramref name="candidates"/> down to those NOT currently
    /// owned by a live window/process — see <see cref="TryAcquireOwnershipLock"/>. Recovery-OFFERING
    /// call sites (FreeW's <c>AutosaveCoordinator.OfferRecovery</c>/<c>RecoverUnsavedDocuments</c>,
    /// <c>AutosaveAdapter.OfferRecoveryAsync</c>, FreeX's <c>App.xaml.cs</c>/<c>App.cs</c>
    /// <c>OfferStartupRecovery</c>) must apply this AFTER <see cref="EnumerateCandidates"/> and
    /// BEFORE ordering/offering candidates to the user — otherwise a still-open sibling window's (or
    /// sibling process's) live snapshot can be listed for "recovery" in another window and, if
    /// accepted, <see cref="DeleteCandidate"/>d out from under the window that is still actively
    /// relying on it for its own future crash recovery.
    /// <para>
    /// Deliberately NOT folded into <see cref="EnumerateCandidates"/> itself: that method is also
    /// relied on for the raw, unfiltered on-disk view (diagnostics-style assertions, and tests that
    /// intentionally keep a sibling coordinator alive to prove its snapshot file survives another
    /// window's cleanup) where "everything currently on disk" — live or not — is exactly what is
    /// wanted. Only the user-facing recovery OFFER needs liveness filtering.
    /// </para>
    /// </summary>
    public IReadOnlyList<AutosaveRecoveryCandidate> ExcludeLiveOwned(
        IReadOnlyList<AutosaveRecoveryCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
            return candidates;

        List<AutosaveRecoveryCandidate>? kept = null;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var baseName = Path.GetFileNameWithoutExtension(candidate.SnapshotPath);
            if (IsLockHeld(GetLockPath(baseName)))
            {
                kept ??= new List<AutosaveRecoveryCandidate>(candidates.Take(i));
                continue;
            }

            kept?.Add(candidate);
        }

        return kept ?? candidates;
    }

    /// <summary>
    /// True if the lock file at <paramref name="lockPath"/> is currently held open
    /// (<c>FileShare.None</c>) by a live process — this one or another. Probes by attempting to
    /// open the very same path exclusively: succeeding means nothing else holds it (not live);
    /// a sharing violation means something does (live). A missing lock file is also "not live" —
    /// either nothing ever claimed this snapshot slot, or its owner already exited and the OS
    /// released the lock. See <see cref="TryAcquireOwnershipLock"/> for why this needs no
    /// staleness/expiry handling: the OS tears the lock down the instant the owning process exits,
    /// for any reason, so a dead owner's marker can never be observed as "still live."
    /// </summary>
    private static bool IsLockHeld(string lockPath)
    {
        if (!File.Exists(lockPath))
            return false;

        try
        {
            using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch
        {
            // Any other failure (permissions, etc.) must not block recovery — degrade to "not
            // live" so the candidate is still offered rather than silently disappearing forever.
            return false;
        }
    }

    /// <summary>
    /// Deletes a recovery snapshot and its sidecar. Ignores errors (best-effort cleanup).
    /// </summary>
    public static void DeleteCandidate(AutosaveRecoveryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        try { File.Delete(candidate.SnapshotPath); } catch { /* best-effort */ }
        try { File.Delete(candidate.SidecarPath); } catch { /* best-effort */ }
    }

    /// <summary>
    /// True if <paramref name="path"/> is a readable snapshot in whichever format it was actually
    /// written in. FreeW/FreeP snapshots are OPC/ZIP packages (.docx/.pptx); FreeX snapshots are
    /// plain JSON (<c>NativeJsonAdapter.Save</c> — see <see cref="AutosaveSidecar"/> doc). The two
    /// formats have distinct corruption signatures (a truncated ZIP fails its central-directory
    /// read; truncated/malformed JSON fails to parse), so detect which one this file actually is
    /// by its leading magic bytes and validate accordingly — a snapshot that fails validation for
    /// its own format is a truncated/corrupt write (e.g. the writing process was killed mid-write)
    /// and is unrecoverable. Cheap — never reads more than the ZIP central directory or does a full
    /// JSON parse pass; no entry contents / DTO materialization.
    /// </summary>
    private static bool IsReadableSnapshot(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (LooksLikeZipArchive(stream))
            {
                stream.Position = 0;
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                _ = archive.Entries.Count;
                return true;
            }

            stream.Position = 0;
            using var document = JsonDocument.Parse(stream);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Detects a ZIP/OPC package by its leading local-file-header signature ("PK"). Every ZIP
    /// central-directory/local-file-header variant (including an empty archive's end-of-central-
    /// directory record) starts with these two bytes, and no valid JSON document can (JSON's first
    /// non-whitespace byte is always one of <c>{ [ " - t f n</c> or a digit), so this cannot
    /// misclassify a genuine FreeX JSON snapshot as a ZIP.
    /// </summary>
    private static bool LooksLikeZipArchive(Stream stream)
    {
        Span<byte> header = stackalloc byte[2];
        var read = stream.Read(header);
        return read == 2 && header[0] == (byte)'P' && header[1] == (byte)'K';
    }

    /// <summary>
    /// Moves a recovery snapshot + sidecar aside into a <c>Quarantine</c> subfolder of the recovery
    /// directory, so a structurally-corrupt snapshot (e.g. a truncated ZIP from a crashed write) is
    /// NOT re-offered on every launch — which otherwise produces an endless "Could not recover the
    /// document" loop. The bytes are preserved (moved, not deleted) for diagnostics. Best-effort: if
    /// the move fails the snapshot is deleted instead so the loop still ends. Ignores errors.
    /// </summary>
    public static void QuarantineCandidate(AutosaveRecoveryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var dir = Path.GetDirectoryName(candidate.SnapshotPath);
        if (string.IsNullOrEmpty(dir))
        {
            DeleteCandidate(candidate);
            return;
        }

        string? quarantine = null;
        try
        {
            quarantine = Path.Combine(dir, "Quarantine");
            Directory.CreateDirectory(quarantine);
        }
        catch { /* fall back to delete below */ }

        MoveAsideOrDelete(candidate.SnapshotPath, quarantine);
        MoveAsideOrDelete(candidate.SidecarPath, quarantine);
    }

    private static void MoveAsideOrDelete(string path, string? quarantineDir)
    {
        try
        {
            if (!File.Exists(path))
                return;
            if (quarantineDir is not null)
            {
                var dest = Path.Combine(quarantineDir, Path.GetFileName(path));
                if (File.Exists(dest))
                    dest = Path.Combine(quarantineDir,
                        Path.GetFileNameWithoutExtension(path) + "." + LaunchId.ToString("N")[..8] + Path.GetExtension(path));
                File.Move(path, dest);
                return;
            }
        }
        catch { /* fall through to delete */ }

        try { File.Delete(path); } catch { /* best-effort */ }
    }

    /// <summary>
    /// Deletes the snapshot and sidecar for a specific session ID. Ignores errors (best-effort
    /// cleanup), but the two deletes are NOT independent: the sidecar is only removed once the
    /// snapshot delete has actually succeeded (or the snapshot was already gone). <c>File.Delete</c>
    /// is a silent no-op for a missing file, so this costs nothing in the common case. It matters
    /// for the rare one: if the snapshot delete throws (e.g. a transient AV-scan/indexer lock on
    /// Windows), the sidecar is deliberately left in place too, so the pair stays intact for
    /// <see cref="EnumerateCandidates"/> to find again later rather than splitting into a payload
    /// with no sidecar — which is invisible to every recovery scan (they require a matching
    /// sidecar) and would otherwise leak in the recovery directory forever with no cleanup path.
    /// </summary>
    public void DeleteSnapshot(string snapshotId)
    {
        if (string.IsNullOrWhiteSpace(snapshotId))
            return;

        var snapshotDeleted = true;
        try { File.Delete(GetSnapshotPath(snapshotId)); }
        catch { snapshotDeleted = false; /* best-effort; sidecar deliberately kept, see above */ }

        if (snapshotDeleted)
        {
            try { File.Delete(GetSidecarPath(snapshotId)); } catch { /* best-effort */ }
        }
    }
}
