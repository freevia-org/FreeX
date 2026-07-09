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
    private string _autosaveSnapshotId = string.Empty;

    // ── IAutosaveWorkbookSource ───────────────────────────────────────────────

    Workbook IAutosaveWorkbookSource.Workbook => _workbook;
    string? IAutosaveWorkbookSource.CurrentFilePath => _currentFilePath;
    string IAutosaveWorkbookSource.DisplayName => _workbook.Name;
    bool IAutosaveWorkbookSource.IsWorkbookDirty => _workbookDirty;
    int IAutosaveWorkbookSource.WorkbookDirtyGeneration => _workbookDirtyGeneration;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    internal void AttachAutosaveService(AutosaveService service, AutosaveSnapshotStore store)
    {
        // Use the per-instance window Guid rather than the registry index so the snapshot ID
        // is unique even when AttachAutosaveService runs before RegisterWithWindowRegistry
        // (e.g. on startup and crash-recovery paths). The registry IndexOf returns -1 before
        // the Loaded handler fires, which previously caused all windows to share "w0" and
        // overwrite each other's autosave.
        // Include the per-launch GUID so a recycled OS PID never clobbers a prior session's
        // unrecovered snapshot. The GUID is stable for the lifetime of this process.
        var launchTag = AutosaveSnapshotStore.LaunchId.ToString("N")[..8];
        var windowTag = _autosaveWindowId.ToString("N")[..8];
        _autosaveSnapshotId = FormattableString.Invariant(
            $"recovery-{Environment.ProcessId}-{launchTag}-{windowTag}");

        _autosaveService = service;
        _autosaveService.Attach(this, _autosaveSnapshotId);

        _autosaveTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = AutosaveService.DefaultInterval
        };
        _autosaveTimer.Tick += (_, _) => _autosaveService?.OnTimerTick();
        _autosaveTimer.Start();

        // Delete snapshot on clean save and on normal close.
        Closed += (_, _) => OnAutosaveCleanClose();
    }

    /// <summary>
    /// Called by the autosave service after startup recovery loads a workbook into this window.
    /// Sets the original file path association from the sidecar so Save goes to the right place.
    /// </summary>
    internal void SetCurrentFilePathForRecovery(string? originalFilePath)
    {
        _currentFilePath = originalFilePath;
    }

    private void OnAutosaveCleanClose()
    {
        _autosaveTimer?.Stop();
        _autosaveService?.DeleteSnapshot();
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
}
