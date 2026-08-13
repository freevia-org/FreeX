namespace Free.Shared.AppServices;

/// <summary>
/// Shared file-command ceremony for small document hosts. Apps still own dialogs and document I/O; this
/// class owns the repeated dirty-gate, save dispatch, recent-file registration, current-path filename
/// derivation, and change notification choreography around <see cref="FileCommandSession"/>.
/// </summary>
public sealed class FileCommandWorkflow
{
    private readonly FileCommandSession _session;
    private readonly Func<int> _maxRecentEntries;
    private readonly Action _onChanged;
    private readonly Func<string, SaveChangesPrompt> _promptSaveChanges;
    private readonly Func<bool> _save;

    public FileCommandWorkflow(
        Func<int> maxRecentEntries,
        Action onChanged,
        Func<string, SaveChangesPrompt> promptSaveChanges,
        Func<bool> save,
        string untitledDisplayName = FileCommandSession.DefaultUntitledDisplayName,
        Func<RecentFilesStore>? loadRecentFilesStore = null)
    {
        ArgumentNullException.ThrowIfNull(maxRecentEntries);
        ArgumentNullException.ThrowIfNull(onChanged);
        ArgumentNullException.ThrowIfNull(promptSaveChanges);
        ArgumentNullException.ThrowIfNull(save);

        _session = new FileCommandSession(untitledDisplayName, loadRecentFilesStore);
        _maxRecentEntries = maxRecentEntries;
        _onChanged = onChanged;
        _promptSaveChanges = promptSaveChanges;
        _save = save;
    }

    public bool IsDirty => _session.IsDirty;

    public int DirtyGeneration => _session.DirtyGeneration;

    public string? CurrentPath => _session.CurrentPath;

    public string? CurrentFileName => _session.CurrentFileName;

    public string DisplayName => _session.DisplayName;

    public IReadOnlyList<RecentFileEntry> RecentEntries => _session.RecentEntries;

    public string CurrentFileNameWithoutExtensionOr(string fallbackDisplayName) =>
        FileCommandSession.FileNameWithoutExtensionFromPath(_session.CurrentPath, fallbackDisplayName);

    public void MarkDirty() => _session.MarkDirtyIfClean(_onChanged);

    public void MarkDirtyWithPath(string? path, Action? beforeChanged = null)
    {
        _session.SetCurrentPath(path);
        _session.MarkDirty();
        Notify(beforeChanged);
    }

    /// <summary>
    /// Restores the file identity carried by an in-memory document snapshot without registering a
    /// duplicate recent-file entry. This is shared so WPF and Avalonia new-window/recovery adapters
    /// cannot diverge on the path/dirty-state matrix.
    /// </summary>
    public void ApplyDocumentState(string? path, bool isDirty, Action? beforeChanged = null)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path) ? null : path;

        if (isDirty)
        {
            MarkDirtyWithPath(normalizedPath, beforeChanged);
            return;
        }

        if (normalizedPath is null)
        {
            MarkSavedWithoutPath(beforeChanged);
            return;
        }

        MarkSavedWithPath(normalizedPath, suppressRecentFiles: true, beforeChanged);
    }

    public bool New(string action, Action loadNewDocument, Action? beforeChanged = null)
    {
        ArgumentNullException.ThrowIfNull(loadNewDocument);

        if (!ConfirmDiscardOrSave(action))
            return false;

        loadNewDocument();
        MarkSavedWithoutPath(beforeChanged);
        return true;
    }

    public async Task<bool> NewAsync(
        string action,
        Func<Task> loadNewDocumentAsync,
        Action? beforeChanged = null)
    {
        ArgumentNullException.ThrowIfNull(loadNewDocumentAsync);

        if (!ConfirmDiscardOrSave(action))
            return false;

        await loadNewDocumentAsync();
        MarkSavedWithoutPath(beforeChanged);
        return true;
    }

    public bool Open(string action, Func<string?> promptPath, Func<string, bool> openPath)
    {
        ArgumentNullException.ThrowIfNull(promptPath);
        ArgumentNullException.ThrowIfNull(openPath);

        if (!ConfirmDiscardOrSave(action))
            return false;

        var path = promptPath();
        return !string.IsNullOrWhiteSpace(path) && openPath(path);
    }

    public async Task<bool> OpenAsync(
        string action,
        Func<Task<string?>> promptPathAsync,
        Func<string, Task<bool>> openPathAsync)
    {
        ArgumentNullException.ThrowIfNull(promptPathAsync);
        ArgumentNullException.ThrowIfNull(openPathAsync);

        if (!ConfirmDiscardOrSave(action))
            return false;

        var path = await promptPathAsync();
        return !string.IsNullOrWhiteSpace(path) && await openPathAsync(path);
    }

    public bool Save(Func<string, bool> saveToCurrentPath, Func<bool> saveAs)
    {
        ArgumentNullException.ThrowIfNull(saveToCurrentPath);
        ArgumentNullException.ThrowIfNull(saveAs);

        return FileLifecyclePlanner.PlanSave(_session.IsDirty, _session.CurrentPath) switch
        {
            FileSaveIntent.UseExistingPath => saveToCurrentPath(_session.CurrentPath!),
            FileSaveIntent.NothingToDo => saveToCurrentPath(_session.CurrentPath!),
            _ => saveAs(),
        };
    }

    public Task<bool> SaveAsync(
        Func<string, Task<bool>> saveToCurrentPathAsync,
        Func<Task<bool>> saveAsAsync)
    {
        ArgumentNullException.ThrowIfNull(saveToCurrentPathAsync);
        ArgumentNullException.ThrowIfNull(saveAsAsync);

        return FileLifecyclePlanner.PlanSave(_session.IsDirty, _session.CurrentPath) switch
        {
            FileSaveIntent.UseExistingPath => saveToCurrentPathAsync(_session.CurrentPath!),
            FileSaveIntent.NothingToDo => saveToCurrentPathAsync(_session.CurrentPath!),
            _ => saveAsAsync(),
        };
    }

    public bool ConfirmCloseAllowed(string action = "closing") => ConfirmDiscardOrSave(action);

    public bool ConfirmDiscardOrSave(string action) =>
        _session.ConfirmDiscardOrSave(action, _promptSaveChanges, _save);

    public void MarkSavedWithoutPath(Action? beforeChanged = null)
    {
        _session.MarkSavedWithoutPath(() => Notify(beforeChanged));
    }

    public void MarkSavedWithPath(string path, bool suppressRecentFiles, Action? beforeChanged = null)
    {
        _session.MarkSavedWithPath(
            path,
            suppressRecentFiles,
            _maxRecentEntries(),
            () => Notify(beforeChanged));
    }

    private void Notify(Action? beforeChanged)
    {
        beforeChanged?.Invoke();
        _onChanged();
    }
}
