using System.IO;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Provides the workbook and dirty-state information needed by the autosave service.
/// Implemented by MainWindow; allows the service to stay in the services layer.
/// </summary>
public interface IAutosaveWorkbookSource
{
    Workbook Workbook { get; }
    string? CurrentFilePath { get; }
    string DisplayName { get; }
    bool IsWorkbookDirty { get; }
    int WorkbookDirtyGeneration { get; }
}

/// <summary>
/// Periodically snapshots dirty workbooks to the recovery directory using
/// <see cref="NativeJsonAdapter"/>. Timer-driven; the Tick fires on the dispatcher
/// thread so workbook access is safe without additional synchronisation.
///
/// Thread note: NativeJsonAdapter.Save serializes synchronously on the dispatcher thread.
/// For typical workbooks (&lt;50k cells) this is imperceptible; for very large workbooks it
/// may stall the UI for a fraction of a second. A proper clone-then-background-serialize
/// would require a deep-copy API that does not currently exist on Workbook, so we accept
/// the trade-off and document it here.
/// </summary>
public sealed class AutosaveService : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);
    private const int BufferSize = 1024 * 128;

    private readonly AutosaveSnapshotStore _store;
    private readonly NativeJsonAdapter _adapter = new();

    private IAutosaveWorkbookSource? _source;
    private string _snapshotId = string.Empty;
    private int _lastSnapshotGeneration = -1;
    private bool _disposed;

    public AutosaveService(AutosaveSnapshotStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// Starts tracking the workbook window. Should be called once when the window is loaded.
    /// </summary>
    public void Attach(IAutosaveWorkbookSource source, string snapshotId)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);

        _source = source;
        _snapshotId = snapshotId;
        _lastSnapshotGeneration = -1;
    }

    /// <summary>
    /// Called on every timer tick (must be called on the dispatcher/UI thread).
    /// Serializes and writes a snapshot if the workbook is dirty and changed since the last snapshot.
    /// </summary>
    public void OnTimerTick()
    {
        if (_disposed || _source is null)
            return;

        TryWriteSnapshot(_source);
    }

    /// <summary>
    /// Performs an emergency best-effort snapshot — used from crash handlers.
    /// Must never throw.
    /// </summary>
    public void TryEmergencySnapshot(IAutosaveWorkbookSource source)
    {
        try
        {
            if (_disposed)
                return;

            TryWriteSnapshot(source);
        }
        catch
        {
            // Crash handlers must never throw.
        }
    }

    /// <summary>
    /// Deletes the recovery snapshot for this session. Call after a clean save or normal close.
    /// </summary>
    public void DeleteSnapshot()
    {
        if (string.IsNullOrWhiteSpace(_snapshotId))
            return;

        _store.DeleteSnapshot(_snapshotId);
    }

    private void TryWriteSnapshot(IAutosaveWorkbookSource source)
    {
        try
        {
            if (!AutosaveSnapshotStore.ShouldSnapshot(
                    source.IsWorkbookDirty,
                    source.WorkbookDirtyGeneration,
                    _lastSnapshotGeneration))
            {
                return;
            }

            var snapshotPath = _store.GetSnapshotPath(_snapshotId);
            var sidecarPath = _store.GetSidecarPath(_snapshotId);

            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);

            // Write snapshot atomically: serialize to temp, then move.
            var tempSnapshot = snapshotPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var fs = new FileStream(
                    tempSnapshot,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    BufferSize))
                {
                    _adapter.Save(source.Workbook, fs);
                }

                File.Move(tempSnapshot, snapshotPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempSnapshot))
                    try { File.Delete(tempSnapshot); } catch { /* best-effort */ }
            }

            // Write sidecar.
            var sidecar = new AutosaveSidecar
            {
                OriginalFilePath = source.CurrentFilePath,
                DisplayName = source.DisplayName,
                TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
                SnapshotId = _snapshotId
            };
            AtomicFileWriter.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));

            _lastSnapshotGeneration = source.WorkbookDirtyGeneration;
        }
        catch
        {
            // Autosave is best-effort and must never affect app behavior.
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
