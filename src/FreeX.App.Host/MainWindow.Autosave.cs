using System.Windows;
using System.Windows.Threading;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// Autosave / crash-recovery plumbing for MainWindow.
/// Wires a DispatcherTimer-driven AutosaveService and implements IAutosaveWorkbookSource.
/// </summary>
public partial class MainWindow : IAutosaveWorkbookSource
{
    // One snapshot per window session — deterministic ID so a crash and re-open produces the
    // same file path for the same window session (process id + window index within the registry).
    // _autosaveWindowId is assigned in the constructor so it is unique per instance regardless
    // of when AttachAutosaveService is called relative to registry registration.
    private readonly Guid _autosaveWindowId = Guid.NewGuid();
    private AutosaveService? _autosaveService;
    private DispatcherTimer? _autosaveTimer;

    // ── IAutosaveWorkbookSource ───────────────────────────────────────────────

    Workbook IAutosaveWorkbookSource.Workbook => _workbook;
    string? IAutosaveWorkbookSource.CurrentFilePath => _currentFilePath;
    string IAutosaveWorkbookSource.DisplayName => _workbook.Name;
    bool IAutosaveWorkbookSource.IsWorkbookDirty => _workbookDirty;
    int IAutosaveWorkbookSource.WorkbookDirtyGeneration => _workbookDirtyGeneration;
    // Reuse the same reconciliation Ctrl+S uses (MainWindow.Viewport.cs) so an autosave/crash
    // snapshot persists THIS window's own zoom/freeze/split/scroll/active-cell instead of
    // whichever "New Window" sibling last touched the shared Sheet view fields.
    void IAutosaveWorkbookSource.ReconcileViewStateForSnapshot() => ReconcileViewStateForSave();
    // Workbook.Id is per-instance (assigned fresh in the Workbook constructor), so windows that
    // share the SAME Workbook instance (View > New Window siblings — see AdoptSharedWorkbook in
    // MainWindow.MultiWindow.cs) report the same DocumentId, while independent windows opened on
    // the same file path each get their own freshly-deserialized Workbook and a different one.
    string IAutosaveWorkbookSource.DocumentId => _workbook.Id.Value.ToString();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    internal void AttachAutosaveService(AutosaveService service, AutosaveSnapshotStore store)
    {
        // Use the per-instance window Guid rather than the registry index so the snapshot ID
        // is unique even when AttachAutosaveService runs before RegisterWithWindowRegistry
        // (e.g. on startup and crash-recovery paths). The registry IndexOf returns -1 before
        // the Loaded handler fires, which previously caused all windows to share "w0" and
        // overwrite each other's autosave.
        _autosaveService = service;
        _autosaveService.Attach(this, _autosaveWindowId);

        _autosaveTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = AutosaveService.DefaultInterval
        };
        _autosaveTimer.Tick += (_, _) => OnAutosaveTimerTick();
        _autosaveTimer.Start();

        // Delete snapshot on clean save and on normal close.
        Closed += (_, _) => OnAutosaveCleanClose();
    }

    /// <summary>
    /// Called by the autosave service after startup recovery loads a workbook into this window.
    /// Sets the original file path association from the sidecar so Save goes to the right place.
    /// </summary>
    /// <remarks>
    /// OpenRecoverySnapshotAsync (run just before this) captured
    /// <c>_currentFileSourceLastWriteTimeUtc</c> from the SNAPSHOT file (a temp .fxl under the
    /// autosave directory), not from <paramref name="originalFilePath"/>. Left uncorrected, the
    /// save-conflict guard in SaveWorkbookToTargetAsync would compare the original file's real
    /// on-disk write time against the unrelated snapshot's write time — a mismatch on every save,
    /// firing a spurious "modified by someone else" warning on the ordinary recover-then-save
    /// workflow. Reconcile here by re-capturing the expected write time from the ORIGINAL file as
    /// it stands right now: this preserves the guard's purpose (still catches a genuine edit to
    /// the original file between recovery and save) while dropping the bogus snapshot-vs-original
    /// comparison. If the original file no longer exists (e.g. it was moved/deleted since the
    /// crash), null it so the guard is skipped — there is nothing to compare against, matching
    /// Excel's own behavior of not conflict-checking a save target that isn't there yet.
    /// </remarks>
    internal void SetCurrentFilePathForRecovery(string? originalFilePath)
    {
        _currentFilePath = originalFilePath;
        _currentFileSourceLastWriteTimeUtc =
            originalFilePath is not null && System.IO.File.Exists(originalFilePath)
                ? System.IO.File.GetLastWriteTimeUtc(originalFilePath)
                : null;
    }

    private void OnAutosaveCleanClose()
    {
        _autosaveTimer?.Stop();
        _autosaveService?.DeleteSnapshot();
        // Releases this window's Round134-remediation liveness lock deterministically on close,
        // rather than leaving it to whenever the GC finalizes the underlying handle — see
        // AutosaveSnapshotCoordinator.Dispose / ReleaseOwnershipLock. Safe after DeleteSnapshot:
        // the timer is already stopped, so no further OnTimerTick can race this.
        _autosaveService?.Dispose();
    }

    /// <summary>
    /// Deletes the autosave snapshot after a successful clean save (called from MarkWorkbookSaved
    /// path in the save workflow — hook is in the existing MarkWorkbookSaved method). Also fans out
    /// to every OTHER live window viewing the same document (Excel "New Window" siblings): each such
    /// window owns its own independent per-window autosave snapshot (see AttachAutosaveService),
    /// last written before this save, so leaving it in place would let a later crash offer stale
    /// pre-save content that could clobber the file this save just wrote.
    /// </summary>
    internal void NotifyAutosaveSaved()
    {
        _autosaveService?.DeleteSnapshot();

        if (_windowRegistry is null)
            return;

        foreach (var window in _windowRegistry.Windows)
        {
            if (window is MainWindow sibling && !ReferenceEquals(sibling, this) && sibling.DocumentId == DocumentId)
                sibling._autosaveService?.DeleteSnapshot();
        }
    }

    /// <summary>
    /// Marks the workbook dirty after recovery load so the user sees the modified indicator
    /// and a save prompt on close. Called from App.xaml.cs after recovery succeeds.
    /// </summary>
    internal void MarkWorkbookDirtyForRecovery()
    {
        MarkWorkbookDirty();
    }

    /// <summary>Exposed for App.xaml.cs crash handler to attempt a best-effort emergency save.</summary>
    internal AutosaveService? AutosaveServiceForCrashHandler => _autosaveService;

    /// <summary>
    /// The periodic autosave <see cref="DispatcherTimer"/>'s Tick handler. Skips the tick entirely
    /// while the save-input gate is held (<see cref="ShouldSkipAutosaveTickForSave"/>): that gate
    /// is non-zero both in the window doing its OWN explicit save and in every "New Window" sibling
    /// the save-in-progress broadcast reached (see AdjustSaveGate / ApplySaveInProgress in
    /// MainWindow.Backstage.cs, and the identical reasoning already applied to the queued
    /// viewport-resize refresh via <c>ShouldDeferViewportResizeRefreshForSave</c> in
    /// MainWindow.WorkbookUiState.cs). Without this, an autosave tick landing mid-save calls
    /// ReconcileViewStateForSnapshot -&gt; ReconcileViewStateForSave, which writes THIS window's
    /// view-state fields (zoom/freeze/split/active cell) directly onto the shared Sheet objects
    /// that the OTHER window's background save thread is concurrently serializing -- and then
    /// serializes the whole live Workbook itself on the UI thread while that background thread is
    /// reading it, both unsynchronized. Simply skipping is sufficient: the timer keeps its normal
    /// cadence and fires again next interval once the gate has released, so no snapshot is lost,
    /// only deferred. Named (rather than inlined in the Tick lambda) so it can be invoked directly
    /// from a test without needing the real DispatcherTimer to fire.
    /// </summary>
    private void OnAutosaveTimerTick()
    {
        if (ShouldSkipAutosaveTickForSave)
            return;

        _autosaveService?.OnTimerTick();
    }

    /// <summary>
    /// Whether the autosave timer's tick must be skipped because a save is in flight -- either
    /// THIS window's own explicit save, or a "New Window" sibling's save that broadcast the gate
    /// into this window. Non-zero for the same duration <c>ShouldDeferViewportResizeRefreshForSave</c>
    /// (MainWindow.WorkbookUiState.cs) reports true.
    /// </summary>
    internal bool ShouldSkipAutosaveTickForSave => _saveGateHoldCount > 0;
}
