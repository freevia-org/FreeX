using System.Collections.Concurrent;
using Free.Shared.IO;

namespace Free.Shared.AppServices;

/// <summary>
/// Model-free file-command state for small document hosts. It owns the duplicated ceremony shared
/// by FreeW and FreeP: dirty/path state, display-name derivation, idempotent dirty notification,
/// dirty-gate resolution, and best-effort recent-file persistence. Hosts still own native dialogs
/// and document-format I/O.
/// </summary>
public sealed class FileCommandSession
{
    public const string DefaultUntitledDisplayName = "Untitled";

    private readonly Func<RecentFilesStore> _loadRecentFilesStore;
    private readonly ConcurrentDictionary<string, bool> _pathExistsCache = new(PlatformPathIdentityComparer.Current);
    private readonly Func<string, bool> _pathExists;

    // Caches each recent path's existence probe result for the lifetime of this session, so a
    // Backstage Open-pane rebuild triggered once per keystroke (FreeW) or once per pane switch
    // (FreeP) re-probes each path at most once instead of re-running a synchronous File.Exists
    // (potentially a 20+ second SMB/TCP timeout for an unreachable UNC/mapped-network path) on
    // every single access. Keyed with platform path identity so a case-differing alias of an
    // already-probed path reuses the cached result instead of re-probing.

    private readonly WorkbookDocumentState _state = new();
    private readonly string _untitledDisplayName;

    public FileCommandSession(
        string untitledDisplayName = DefaultUntitledDisplayName,
        Func<RecentFilesStore>? loadRecentFilesStore = null,
        Func<string, bool>? pathExists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(untitledDisplayName);

        _untitledDisplayName = untitledDisplayName;
        _loadRecentFilesStore = loadRecentFilesStore ?? RecentFilesStore.Load;
        // r181: this default stays SYNCHRONOUS deliberately, and the finding that asked for the
        // off-thread RecentFilePathExistenceCache here was only half right. The freeze it
        // describes is real -- File.Exists against an unreachable UNC path blocks for the SMB
        // timeout, 20+ seconds, on the UI thread building the Backstage pane. But that cache
        // answers OPTIMISTICALLY until its background probe returns, and FreeW/FreeP read
        // RecentEntries once and render it with no refresh hook, so defaulting to it swapped a
        // freeze for a dead entry that never disappears (RecentEntries_PrunesEntriesWhoseFile
        // NoLongerExists pins exactly that, and is right to). The complete fix is host-side: the
        // four FreeW/FreeP shells must pass the cache in AND wire its onProbed callback to
        // re-render, the way both FreeX shells already do. Until then the correct default is the
        // one that never shows a dead entry.
        _pathExists = pathExists ?? File.Exists;
    }

    public bool IsDirty => _state.IsDirty;

    public int DirtyGeneration => _state.DirtyGeneration;

    public string? CurrentPath => _state.CurrentFilePath;

    public string DisplayName => DisplayNameFromPath(_state.CurrentFilePath, _untitledDisplayName);

    public string? CurrentFileName => FileNameFromPath(_state.CurrentFilePath);

    /// <summary>
    /// Recent files (most recent first) from the shared store, pruned to entries whose file still
    /// exists on disk; never throws. Mirrors the FreeX WPF host's <c>BackstageRecentFileListPlanner</c>
    /// filtering so a moved/deleted file silently drops out of the Recent list instead of producing a
    /// dead "Open" click. Each path existence result is cached for the lifetime of this session, so
    /// repeated reads -- e.g. once per keystroke while a host filters its Open-pane Recent list --
    /// probe a given path at most once instead of re-running a synchronous, potentially multi-second
    /// filesystem/network check on every access.
    /// </summary>
    public IReadOnlyList<RecentFileEntry> RecentEntries
    {
        get
        {
            try
            {
                var entries = _loadRecentFilesStore().Entries;
                var existing = new List<RecentFileEntry>(entries.Count);
                foreach (var entry in entries)
                {
                    // NOTE: a caller that injects RecentFilePathExistenceCache must NOT have its
                    // answer memoised here -- that cache updates once its background probe
                    // returns, and a second cache in front of it would freeze the optimistic
                    // "yes" forever. The per-session cache is only correct for a probe that is
                    // already authoritative on its first call, which the default File.Exists is.
                    if (_pathExistsCache.GetOrAdd(entry.Path, _pathExists))
                        existing.Add(entry);
                }

                return existing;
            }
            catch
            {
                return Array.Empty<RecentFileEntry>();
            }
        }
    }

    public static string DisplayNameFromPath(
        string? currentFilePath,
        string untitledDisplayName = DefaultUntitledDisplayName)
    {
        return FilePathPolicy.FileNameWithoutExtensionOr(currentFilePath, untitledDisplayName);
    }

    public static string? FileNameFromPath(string? currentFilePath)
    {
        return FilePathPolicy.TryGetFileName(currentFilePath, out var fileName) ? fileName : null;
    }

    public static string FileNameWithoutExtensionFromPath(
        string? currentFilePath,
        string fallbackDisplayName = DefaultUntitledDisplayName)
    {
        var effectiveName = FileNameFromPath(currentFilePath);
        if (string.IsNullOrWhiteSpace(effectiveName))
            effectiveName = string.IsNullOrWhiteSpace(fallbackDisplayName)
                ? DefaultUntitledDisplayName
                : fallbackDisplayName;

        return FilePathPolicy.FileNameWithoutExtensionOr(effectiveName, DefaultUntitledDisplayName);
    }

    public void ClearCurrentPath() => _state.ClearCurrentFilePath();

    public void SetCurrentPath(string? path) => _state.SetCurrentFilePath(path);

    public void MarkSaved() => _state.MarkSaved();

    public void MarkSavedWithoutPath()
    {
        _state.ClearCurrentFilePath();
        _state.MarkSaved();
    }

    public void MarkSavedWithoutPath(Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);

        MarkSavedWithoutPath();
        onChanged();
    }

    public void MarkDirty() => _state.MarkDirty();

    /// <summary>
    /// Records the undo-stack depth/version at the moment of a successful save, so a later
    /// <see cref="TryMarkCleanIfAtSavePoint(int, long)"/> call can detect "the user undid back to
    /// exactly what is on disk" and clear <see cref="IsDirty"/> without an explicit save. Mirrors
    /// FreeX's <c>WorkbookSession.RecordUndoSavePoint</c> / <c>WorkbookDocumentState.MarkSavedAtUndoDepth</c>
    /// wiring so hosts built on this shared session (FreeW, FreeP) can opt into the same behavior.
    /// </summary>
    /// <param name="undoDepthAtSave">The undo stack's depth at the time the save completed.</param>
    /// <param name="undoStackVersionAtSave">The undo stack's monotonic version token at the time the save completed.</param>
    public void MarkSavedAtUndoDepth(int undoDepthAtSave, long undoStackVersionAtSave) =>
        _state.MarkSavedAtUndoDepth(undoDepthAtSave, undoStackVersionAtSave);

    /// <summary>
    /// If the undo stack has returned to the depth/version recorded by
    /// <see cref="MarkSavedAtUndoDepth"/>, clears <see cref="IsDirty"/> and returns <c>true</c>.
    /// Intended to be called after every Undo/Redo. Mirrors FreeX's
    /// <c>WorkbookSession.TryMarkCleanIfAtSavePoint</c>.
    /// </summary>
    /// <param name="currentUndoDepth">The undo stack's depth right now.</param>
    /// <param name="currentUndoStackVersion">The undo stack's monotonic version token right now.</param>
    public bool TryMarkCleanIfAtSavePoint(int currentUndoDepth, long currentUndoStackVersion) =>
        _state.TryMarkCleanIfAtSavePoint(currentUndoDepth, currentUndoStackVersion);

    /// <summary>
    /// <see cref="TryMarkCleanIfAtSavePoint(int, long)"/> plus a notification callback, invoked only
    /// when the transition to clean actually happens -- mirrors the notify-on-change shape of
    /// <see cref="MarkDirtyIfClean"/> so a host can refresh its dirty-marker UI in one call.
    /// </summary>
    public bool TryMarkCleanIfAtSavePoint(int currentUndoDepth, long currentUndoStackVersion, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);

        if (!_state.TryMarkCleanIfAtSavePoint(currentUndoDepth, currentUndoStackVersion))
            return false;

        onChanged();
        return true;
    }

    public bool MarkDirtyIfClean(Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);

        if (_state.IsDirty)
            return false;

        _state.MarkDirty();
        onChanged();
        return true;
    }

    public void MarkSavedWithPath(string path, bool suppressRecentFiles, int maxRecentEntries)
    {
        _state.MarkSavedWithPath(path);

        try
        {
            RecentFileRegistrationService.RegisterIfNeeded(
                _loadRecentFilesStore,
                new RecentFileRegistrationRequest(path, suppressRecentFiles, maxRecentEntries));
        }
        catch
        {
            // Recent files are a convenience; never block open/save on a corrupt store or bad path.
        }
    }

    public void MarkSavedWithPath(
        string path,
        bool suppressRecentFiles,
        int maxRecentEntries,
        Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);

        MarkSavedWithPath(path, suppressRecentFiles, maxRecentEntries);
        onChanged();
    }

    public bool ConfirmDiscardOrSave(
        string action,
        Func<string, SaveChangesPrompt> promptSaveChanges,
        Func<bool> save)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(promptSaveChanges);
        ArgumentNullException.ThrowIfNull(save);

        if (FileLifecyclePlanner.PlanDirtyGate(_state.IsDirty) == DirtyGateIntent.ProceedWithoutPrompt)
            return true;

        var answer = promptSaveChanges(action);
        return FileLifecyclePlanner.ResolveDirtyGate(answer) switch
        {
            DirtyGateAction.Cancel => false,
            DirtyGateAction.ProceedDiscardingChanges => true,
            DirtyGateAction.SaveThenProceed => save(),
            _ => false,
        };
    }
}
