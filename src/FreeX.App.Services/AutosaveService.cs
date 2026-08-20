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

    /// <summary>
    /// Reconciles this window's own per-window view-state overrides (zoom, freeze panes, split,
    /// active cell, scroll position) onto the shared <see cref="Model.Workbook"/>'s per-<c>Sheet</c>
    /// view fields, immediately before <see cref="Workbook"/> is serialized into an autosave or
    /// crash-recovery snapshot. Those fields (Sheet.ZoomPercent, ViewMode, ShowGridlines,
    /// ShowHeadings, ShowFormulas, FrozenRows/Cols, SplitRow/Column, ActiveRow/Col, ViewTopRow/
    /// LeftCol) are shared by every "New Window" sibling over the same document and are mutated in
    /// place by whichever sibling's command last ran; each window keeps its OWN effective value in
    /// a per-window override cache (see WorkbookSession's <c>_view*Overrides</c> on Avalonia,
    /// MainWindow's <c>_worksheetViewStates</c>/<c>_worksheetSelections</c> on WPF) and only
    /// projects that cache back onto the shared fields on demand. The explicit Ctrl+S path already
    /// does this via <c>WorkbookSaveWorkflowRequest.ProjectViewStateForSave</c>
    /// (WorkbookSession.ReconcileViewStateForSave / MainWindow.ReconcileViewStateForSave) so a save
    /// persists the saving window's own view rather than a sibling's. Without the same call here, a
    /// periodic autosave tick or an emergency crash snapshot serializes whichever sibling window's
    /// view last happened to touch the shared fields, not the view of the window whose timer fired
    /// or whose crash triggered the snapshot. Default no-op: a host with no per-window view-state
    /// overrides to reconcile (or one that has not wired this in) keeps its current behavior.
    /// </summary>
    void ReconcileViewStateForSnapshot() { }

    /// <summary>
    /// Stable identity of the in-memory <see cref="Model.Workbook"/> instance this source wraps
    /// (i.e. <c>Workbook.Id</c>). Two windows sharing the SAME identity are genuine Excel
    /// "New Window" siblings over one shared document (see MainWindow.MultiWindow.cs's
    /// AdoptSharedWorkbook); two windows with DIFFERENT identities are independent documents even
    /// when they happen to share a saved file path (e.g. the same file opened twice via File &gt;
    /// Open into two unrelated windows). Crash-recovery dedup (App.xaml.cs's
    /// GetDocumentIdentityKey) uses this to tell the two cases apart so it never silently deletes
    /// an unrelated window's unsaved snapshot.
    /// </summary>
    string DocumentId { get; }
}

/// <summary>
/// Periodically snapshots dirty workbooks to the recovery directory using
/// <see cref="NativeJsonAdapter"/>. Timer-driven; the Tick fires on the dispatcher
/// thread so workbook access is safe without additional synchronisation.
///
/// The neutral orchestration (dirty/generation gating, atomic snapshot + sidecar write,
/// emergency-save, delete) lives in the shared <see cref="AutosaveSnapshotCoordinator"/>; this
/// type binds it to FreeX's <see cref="IAutosaveWorkbookSource"/> and supplies the workbook
/// serialization via <see cref="NativeJsonAdapter"/>.
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

    private readonly AutosaveSnapshotStore _store;
    private readonly NativeJsonAdapter _adapter = new();

    private AutosaveSnapshotCoordinator? _coordinator;
    private IAutosaveWorkbookSource? _boundSource;
    private bool _disposed;

    public AutosaveService(AutosaveSnapshotStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// Starts tracking a workbook window using the canonical per-launch snapshot identity.
    /// </summary>
    public void Attach(IAutosaveWorkbookSource source, Guid windowId)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (windowId == Guid.Empty)
            throw new ArgumentException("The autosave window identity cannot be empty.", nameof(windowId));

        var launchTag = AutosaveSnapshotStore.LaunchId.ToString("N")[..8];
        var windowTag = windowId.ToString("N")[..8];
        Attach(
            source,
            FormattableString.Invariant(
                $"recovery-{Environment.ProcessId}-{launchTag}-{windowTag}"));
    }

    /// <summary>
    /// Starts tracking the workbook window. Should be called once when the window is loaded.
    /// </summary>
    public void Attach(IAutosaveWorkbookSource source, string snapshotId)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);

        _coordinator = new AutosaveSnapshotCoordinator(_store, snapshotId);
        _boundSource = source;
    }

    /// <summary>
    /// Called on every timer tick (must be called on the dispatcher/UI thread).
    /// Serializes and writes a snapshot if the workbook is dirty and changed since the last snapshot.
    /// </summary>
    public void OnTimerTick()
    {
        if (_disposed || _coordinator is null || _boundSource is null)
            return;

        _coordinator.Snapshot(Wrap(_boundSource));
    }

    /// <summary>
    /// Performs an emergency best-effort snapshot — used from crash handlers.
    /// Must never throw.
    /// </summary>
    public void TryEmergencySnapshot(IAutosaveWorkbookSource source)
    {
        if (_disposed || _coordinator is null)
            return;

        _coordinator.TryEmergencySnapshot(Wrap(source));
    }

    /// <summary>
    /// Performs an emergency best-effort snapshot of the attached workbook source.
    /// Must never throw.
    /// </summary>
    public void TryEmergencySnapshot()
    {
        if (_boundSource is null)
            return;

        TryEmergencySnapshot(_boundSource);
    }

    /// <summary>
    /// Deletes the recovery snapshot for this session. Call after a clean save or normal close.
    /// </summary>
    public void DeleteSnapshot() => _coordinator?.DeleteSnapshot();

    private WorkbookSnapshotSource Wrap(IAutosaveWorkbookSource source) => new(source, _adapter);

    public void Dispose()
    {
        _disposed = true;
        _coordinator?.Dispose();
    }

    /// <summary>
    /// Adapts FreeX's <see cref="IAutosaveWorkbookSource"/> to the neutral
    /// <see cref="IAutosaveSnapshotSource"/>, serializing the workbook via NativeJsonAdapter.
    /// </summary>
    private sealed class WorkbookSnapshotSource : IAutosaveSnapshotSource
    {
        private readonly IAutosaveWorkbookSource _source;
        private readonly NativeJsonAdapter _adapter;

        public WorkbookSnapshotSource(IAutosaveWorkbookSource source, NativeJsonAdapter adapter)
        {
            _source = source;
            _adapter = adapter;
        }

        public string? OriginalFilePath => _source.CurrentFilePath;
        public string DisplayName => _source.DisplayName;
        public bool IsDirty => _source.IsWorkbookDirty;
        public int DirtyGeneration => _source.WorkbookDirtyGeneration;
        public string? DocumentId => _source.DocumentId;

        public void WriteSnapshot(string snapshotPath)
        {
            // Reconcile this window's own view-state overrides onto the shared Sheet fields
            // before serializing, mirroring the explicit-save path's ProjectViewStateForSave --
            // otherwise the snapshot reflects whichever sibling window last touched those shared
            // fields instead of the window this autosave/crash snapshot is actually for.
            _source.ReconcileViewStateForSnapshot();

            using var fs = AutosaveSnapshotCoordinator.OpenSnapshotStream(snapshotPath);
            _adapter.Save(_source.Workbook, fs);
        }
    }
}
