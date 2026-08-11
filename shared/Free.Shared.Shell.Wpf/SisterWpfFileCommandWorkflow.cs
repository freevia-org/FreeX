using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Wpf;

/// <summary>
/// WPF shell wrapper around <see cref="FileCommandWorkflow"/> for sister document apps.
/// It centralizes app-scoped dirty prompts and file-command errors while apps keep
/// picker setup plus document-format open/save operations local.
/// </summary>
public sealed class SisterWpfFileCommandWorkflow
{
    private readonly string _applicationName;
    private readonly IUserMessageService _messageService;
    private readonly FileCommandWorkflow _workflow;

    public SisterWpfFileCommandWorkflow(
        string applicationName,
        Func<int> maxRecentEntries,
        Action onChanged,
        Func<bool> save,
        Func<RecentFilesStore>? loadRecentFilesStore = null,
        IUserMessageService? messageService = null,
        string untitledDisplayName = FileCommandSession.DefaultUntitledDisplayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentNullException.ThrowIfNull(maxRecentEntries);
        ArgumentNullException.ThrowIfNull(onChanged);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentException.ThrowIfNullOrWhiteSpace(untitledDisplayName);

        _applicationName = applicationName;
        _messageService = messageService ?? new WpfUserMessageService();
        _workflow = new FileCommandWorkflow(
            maxRecentEntries,
            onChanged,
            PromptSaveChanges,
            save,
            untitledDisplayName,
            loadRecentFilesStore);
    }

    public FileCommandWorkflow Workflow => _workflow;

    public bool IsDirty => _workflow.IsDirty;

    public int DirtyGeneration => _workflow.DirtyGeneration;

    public string? CurrentPath => _workflow.CurrentPath;

    public string? CurrentFileName => _workflow.CurrentFileName;

    public string DisplayName => _workflow.DisplayName;

    public IReadOnlyList<RecentFileEntry> RecentEntries => _workflow.RecentEntries;

    public string CurrentFileNameWithoutExtensionOr(string fallbackDisplayName) =>
        _workflow.CurrentFileNameWithoutExtensionOr(fallbackDisplayName);

    public void MarkDirty() => _workflow.MarkDirty();

    public void MarkDirtyWithPath(string? path, Action? beforeChanged = null) =>
        _workflow.MarkDirtyWithPath(path, beforeChanged);

    public bool New(string action, Action loadNewDocument, Action? beforeChanged = null) =>
        _workflow.New(action, loadNewDocument, beforeChanged);

    public bool Open(string action, Func<string?> promptPath, Func<string, bool> openPath) =>
        _workflow.Open(action, promptPath, openPath);

    public Task<bool> OpenAsync(
        string action,
        Func<Task<string?>> promptPathAsync,
        Func<string, Task<bool>> openPathAsync) =>
        _workflow.OpenAsync(action, promptPathAsync, openPathAsync);

    public bool Save(Func<string, bool> saveToCurrentPath, Func<bool> saveAs) =>
        _workflow.Save(saveToCurrentPath, saveAs);

    public Task<bool> SaveAsync(
        Func<string, Task<bool>> saveToCurrentPathAsync,
        Func<Task<bool>> saveAsAsync) =>
        _workflow.SaveAsync(saveToCurrentPathAsync, saveAsAsync);

    public bool ConfirmCloseAllowed(string action = "closing") =>
        _workflow.ConfirmCloseAllowed(action);

    public bool ConfirmDiscardOrSave(string action) =>
        _workflow.ConfirmDiscardOrSave(action);

    public void MarkSavedWithoutPath(Action? beforeChanged = null) =>
        _workflow.MarkSavedWithoutPath(beforeChanged);

    public void MarkSavedWithPath(string path, bool suppressRecentFiles, Action? beforeChanged = null) =>
        _workflow.MarkSavedWithPath(path, suppressRecentFiles, beforeChanged);

    private SaveChangesPrompt PromptSaveChanges(string action) =>
        _messageService.PromptSaveChanges(DisplayName, action, _applicationName);

    public void ShowError(string summary, Exception exception) =>
        _messageService.ShowFileCommandError(summary, exception, _applicationName);

    /// <summary>
    /// Surfaces non-fatal image-decode losses collected during an export. No-op when empty.
    /// See <see cref="UserMessageServiceFileCommandExtensions.ShowExportImageWarnings"/>.
    /// </summary>
    public void ShowExportImageWarnings(string exportedSummary, IReadOnlyCollection<string> imageDiagnostics) =>
        _messageService.ShowExportImageWarnings(exportedSummary, imageDiagnostics, _applicationName);
}
