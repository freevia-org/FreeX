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
    private readonly WorkbookDocumentState _state = new();
    private readonly string _untitledDisplayName;

    public FileCommandSession(
        string untitledDisplayName = DefaultUntitledDisplayName,
        Func<RecentFilesStore>? loadRecentFilesStore = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(untitledDisplayName);

        _untitledDisplayName = untitledDisplayName;
        _loadRecentFilesStore = loadRecentFilesStore ?? RecentFilesStore.Load;
    }

    public bool IsDirty => _state.IsDirty;

    public int DirtyGeneration => _state.DirtyGeneration;

    public string? CurrentPath => _state.CurrentFilePath;

    public string DisplayName => DisplayNameFromPath(_state.CurrentFilePath, _untitledDisplayName);

    public string? CurrentFileName => FileNameFromPath(_state.CurrentFilePath);

    /// <summary>Recent files (most recent first) from the shared store; never throws.</summary>
    public IReadOnlyList<RecentFileEntry> RecentEntries
    {
        get
        {
            try
            {
                return _loadRecentFilesStore().Entries;
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
