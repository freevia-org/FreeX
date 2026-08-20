namespace Free.Shared.AppServices;

/// <summary>
/// Per-app binding that supplies the autosave engine with everything it needs to decide
/// whether to snapshot and how to produce the snapshot bytes. Each app implements this:
/// FreeX serializes a workbook, FreeW writes a .docx. The engine itself owns the neutral
/// orchestration (dirty/generation gating, sidecar, atomic write, emergency-save, delete).
/// </summary>
public interface IAutosaveSnapshotSource
{
    /// <summary>The user-facing original file path, embedded in the sidecar.</summary>
    string? OriginalFilePath { get; }

    /// <summary>A friendly document name shown in the recovery prompt.</summary>
    string DisplayName { get; }

    /// <summary>Whether the document currently has unsaved changes.</summary>
    bool IsDirty { get; }

    /// <summary>
    /// A monotonically advancing dirty-edit counter. The engine only re-snapshots when this
    /// differs from the generation it last snapshotted, suppressing redundant writes. Apps that
    /// do not track generations may return a constant value to snapshot on every dirty tick.
    /// </summary>
    int DirtyGeneration { get; }

    /// <summary>
    /// Serializes the current document to <paramref name="snapshotPath"/>. Called on the
    /// dispatcher/UI thread. May throw — the engine treats failures as best-effort no-ops.
    /// </summary>
    void WriteSnapshot(string snapshotPath);

    /// <summary>
    /// Optional stable identity of the in-memory document instance producing this snapshot (e.g.
    /// FreeX's <c>Workbook.Id</c>). Lets crash-recovery dedup tell apart genuine multi-window
    /// siblings over one shared document (same id) from independent documents that merely happen
    /// to share a saved file path (different ids) — see FreeX's App.xaml.cs
    /// GetDocumentIdentityKey. Defaulted to <c>null</c> so existing implementations (FreeW/FreeP)
    /// need no changes; apps that do not supply an identity simply opt out of that distinction.
    /// </summary>
    string? DocumentId => null;
}

/// <summary>
/// Neutral autosave orchestration shared by FreeX and FreeW. Given an
/// <see cref="IAutosaveSnapshotSource"/>, it gates snapshots on dirty-state and generation,
/// writes the snapshot atomically (temp + move) followed by the sidecar, supports a never-throw
/// emergency snapshot for crash handlers, and deletes the snapshot on clean save/close.
///
/// This type is timer-agnostic: the host owns the periodic timer (FreeX uses a background-priority
/// DispatcherTimer at 5 min, FreeW a 30 s DispatcherTimer) and calls <see cref="Snapshot"/> on each
/// tick. Keeping the timer in the host preserves each app's exact threading and interval behavior.
///
/// Thread note: serialization runs synchronously on the calling (dispatcher) thread, matching the
/// prior per-app behavior.
/// </summary>
// IDisposable, not just a Dispose() method: R134 gave this type an OS ownership lock held from
// construction until Dispose, which makes deterministic release part of its contract rather than a
// nicety. Without the interface a `using` on it is a compile error and no analyzer flags a caller
// that forgets, so a leaked coordinator keeps its snapshot slot marked "live" and ExcludeLiveOwned
// silently hides that snapshot from every recovery offer.
public sealed class AutosaveSnapshotCoordinator : IDisposable
{
    private const int BufferSize = 1024 * 128;

    private readonly AutosaveSnapshotStore _store;
    private readonly string _snapshotId;

    private int _lastSnapshotGeneration = -1;
    private bool _disposed;

    // Round134-remediation: held for this coordinator's entire lifetime (construction through
    // Dispose) as the liveness marker AutosaveSnapshotStore.ExcludeLiveOwned checks before any
    // recovery UI offers or deletes a candidate — see TryAcquireOwnershipLock's doc comment for
    // why an OS file lock, not a PID/heartbeat marker, needs no staleness handling. Acquired even
    // before this session has written its first snapshot file: there is nothing to protect yet,
    // but claiming the slot immediately means a sibling window's recovery scan can never race
    // this one's very first write.
    private FileStream? _ownershipLock;

    public AutosaveSnapshotCoordinator(AutosaveSnapshotStore store, string snapshotId)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        _store = store;
        _snapshotId = snapshotId;
        _ownershipLock = _store.TryAcquireOwnershipLock(_snapshotId);
    }

    /// <summary>The stable session snapshot id this coordinator writes under.</summary>
    public string SnapshotId => _snapshotId;

    /// <summary>
    /// Writes a snapshot for <paramref name="source"/> if it is dirty and its generation changed
    /// since the last snapshot. Intended to be called on each timer tick (dispatcher thread).
    /// Best-effort: never throws.
    /// </summary>
    public void Snapshot(IAutosaveSnapshotSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_disposed)
            return;

        TryWriteSnapshot(source, bypassGenerationGate: false);
    }

    /// <summary>
    /// Performs an emergency best-effort snapshot — used from crash handlers. Must never throw.
    /// Bypasses generation gating: a crash handler always tries to capture the latest state,
    /// even when its generation has not advanced since the last periodic snapshot (e.g. a fault
    /// that occurs before any edit bumps the generation again after a prior autosave tick). It
    /// still requires the source to be dirty — a clean (unmodified) document has nothing an
    /// emergency snapshot would recover, and writing one anyway would later offer Document
    /// Recovery for a workbook that never had any unsaved changes at crash time, which Excel
    /// itself never does.
    /// </summary>
    public void TryEmergencySnapshot(IAutosaveSnapshotSource source)
    {
        try
        {
            if (_disposed || source is null)
                return;

            TryWriteSnapshot(source, bypassGenerationGate: true);
        }
        catch
        {
            // Crash handlers must never throw.
        }
    }

    /// <summary>
    /// Deletes the recovery snapshot for this session. Call after a clean save or normal close.
    /// </summary>
    public void DeleteSnapshot() => _store.DeleteSnapshot(_snapshotId);

    private void TryWriteSnapshot(IAutosaveSnapshotSource source, bool bypassGenerationGate)
    {
        try
        {
            // bypassGenerationGate only skips the GENERATION comparison (so a crash handler still
            // captures state even when the generation has not advanced since the last periodic
            // snapshot) — it must never skip the underlying dirty check. A clean document has no
            // unsaved changes to recover, and writing (and later offering) a snapshot for one would
            // mean Document Recovery could resurrect a workbook that had nothing to lose at crash
            // time, which Excel never does.
            if (bypassGenerationGate)
            {
                if (!source.IsDirty)
                    return;
            }
            else if (!AutosaveSnapshotStore.ShouldSnapshot(
                    source.IsDirty,
                    source.DirtyGeneration,
                    _lastSnapshotGeneration))
            {
                return;
            }

            var snapshotPath = _store.GetSnapshotPath(_snapshotId);
            var sidecarPath = _store.GetSidecarPath(_snapshotId);

            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);

            // Write the snapshot atomically (produce into a sibling temp file, then move into
            // place) BEFORE touching the sidecar. source.WriteSnapshot "May throw" (see the
            // interface doc above) and this whole method is a swallow-everything best-effort
            // operation (catch below) — if it throws, execution never reaches the sidecar write,
            // so an existing sidecar keeps pointing at the still-unchanged, still-matching
            // snapshot content instead of gaining a fresh timestamp for data that was never
            // written (round152 finding: a mid-write failure used to leave the sidecar claiming
            // brand-new content while the .fxl/.docx/.pptx on disk still held the PRIOR tick's
            // payload, and Document Recovery would surface that stale content as if it were
            // current). The remaining race is narrower than the reverse ordering's: if the
            // process is killed between the move and the sidecar write below, an existing
            // sidecar is merely stale-but-consistent (it under-promises freshness — recovery may
            // offer content newer than its timestamp claims, never staler), and for the very
            // first snapshot of a session (no sidecar yet) the snapshot is briefly invisible to
            // recovery until the sidecar follows, rather than silently misdescribed.
            using (var temporarySnapshot = AtomicFileWriter.CreateTempLease(snapshotPath))
            {
                source.WriteSnapshot(temporarySnapshot.Path);
                // Flush the temp's data to physical storage BEFORE the rename. WriteSnapshot may return
                // with bytes still in the OS write cache; without this, a power loss after File.Move could
                // leave a renamed-but-truncated snapshot at the target path — the exact corruption that
                // triggers "End of Central Directory record could not be found" on recovery. (Mirrors
                // AtomicFileWriter; FlushFileBuffers via a write handle syncs the file's dirty pages.)
                try
                {
                    using var fs = new FileStream(temporarySnapshot.Path, FileMode.Open, FileAccess.Write, FileShare.None);
                    fs.Flush(flushToDisk: true);
                }
                catch { /* flush is best-effort hardening; the atomic move below is the primary guarantee */ }

                File.Move(temporarySnapshot.Path, snapshotPath, overwrite: true);
                temporarySnapshot.Commit();
            }

            var sidecar = new AutosaveSidecar
            {
                OriginalFilePath = source.OriginalFilePath,
                DisplayName = source.DisplayName,
                TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
                SnapshotId = _snapshotId,
                DocumentId = source.DocumentId
            };
            AtomicFileWriter.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));

            _lastSnapshotGeneration = source.DirtyGeneration;
        }
        catch
        {
            // Autosave is best-effort and must never affect app behavior.
        }
    }

    /// <summary>
    /// A reusable file stream sized for snapshot writes, opened with exclusive create semantics.
    /// Helper for sources that serialize through a <see cref="System.IO.Stream"/>.
    /// </summary>
    public static FileStream OpenSnapshotStream(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, BufferSize);
    }

    public void Dispose()
    {
        _disposed = true;
        ReleaseOwnershipLock();
    }

    /// <summary>
    /// Releases this session's liveness lock (see <see cref="_ownershipLock"/>) so
    /// <see cref="AutosaveSnapshotStore.ExcludeLiveOwned"/> stops reporting this snapshot slot as
    /// live. Deliberately NOT called from <see cref="DeleteSnapshot"/>: that method also runs on a
    /// clean SAVE mid-session (not just a clean close — see e.g. FreeX's
    /// MainWindow.Autosave.cs NotifyAutosaveSaved), while the window itself keeps running and may
    /// autosave again under the SAME snapshot id; releasing the lock there would let another
    /// window's recovery scan treat this one as "gone" the moment it saves, even though it is very
    /// much still open. The lock is only released when the coordinator itself is disposed — i.e.
    /// the window/session it backs is actually going away.
    /// </summary>
    private void ReleaseOwnershipLock()
    {
        var handle = _ownershipLock;
        if (handle is null)
            return;

        _ownershipLock = null;
        try { handle.Dispose(); } catch { /* best-effort */ }
        try { File.Delete(_store.GetLockPath(_snapshotId)); } catch { /* best-effort */ }
    }
}
