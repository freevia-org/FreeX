using Avalonia.Controls;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Avalonia;

public enum SisterAvaloniaFileTitleDisplayName
{
    DisplayName,
    FileName
}

public sealed record SisterAvaloniaFileTitleSpec(
    string ApplicationName,
    string Separator,
    string DirtyMarker = " *",
    WindowTitleApplicationPlacement ApplicationPlacement = WindowTitleApplicationPlacement.ApplicationThenDocument,
    SisterAvaloniaFileTitleDisplayName DocumentDisplayName = SisterAvaloniaFileTitleDisplayName.FileName,
    string UntitledDisplayName = FileCommandSession.DefaultUntitledDisplayName,
    bool CollapseCleanUntitledTitle = false);

/// <summary>
/// Thin Avalonia shell wrapper around <see cref="FileCommandWorkflow"/> for sister document apps.
/// It keeps title composition, dirty prompt text, current-file display, and recent-file limits shared
/// while apps continue to own picker setup plus document-format open/save operations.
/// </summary>
public sealed class SisterAvaloniaFileCommandWorkflow
{
    private readonly Window _owner;
    private readonly SisterAvaloniaFileTitleSpec _titleSpec;
    private readonly Action _onChanged;
    private readonly FileCommandWorkflow _workflow;

    public SisterAvaloniaFileCommandWorkflow(
        Window owner,
        SisterAvaloniaFileTitleSpec titleSpec,
        Func<int> maxRecentEntries,
        Action onChanged,
        Func<bool> save,
        Func<RecentFilesStore>? loadRecentFilesStore = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(titleSpec);
        ArgumentNullException.ThrowIfNull(maxRecentEntries);
        ArgumentNullException.ThrowIfNull(onChanged);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleSpec.ApplicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleSpec.Separator);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleSpec.UntitledDisplayName);

        _owner = owner;
        _titleSpec = titleSpec;
        _onChanged = onChanged;
        _workflow = new FileCommandWorkflow(
            maxRecentEntries,
            OnWorkflowChanged,
            PromptSaveChangesSync,
            save,
            titleSpec.UntitledDisplayName,
            loadRecentFilesStore);

        RefreshTitle();
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

    public Task<bool> SaveAsync(
        Func<string, Task<bool>> saveToCurrentPathAsync,
        Func<Task<bool>> saveAsAsync) =>
        _workflow.SaveAsync(saveToCurrentPathAsync, saveAsAsync);

    public bool ConfirmCloseAllowed(string action = "closing") =>
        _workflow.ConfirmCloseAllowed(action);

    public void MarkSavedWithoutPath(Action? beforeChanged = null) =>
        _workflow.MarkSavedWithoutPath(beforeChanged);

    public void MarkSavedWithPath(string path, bool suppressRecentFiles, Action? beforeChanged = null) =>
        _workflow.MarkSavedWithPath(path, suppressRecentFiles, beforeChanged);

    public void RefreshTitle() => _owner.Title = BuildTitle();

    public string BuildTitle()
    {
        if (_titleSpec.CollapseCleanUntitledTitle && !_workflow.IsDirty && _workflow.CurrentPath is null)
            return _titleSpec.ApplicationName;

        return WindowTitlePlanner.Compose(
            displayName: ResolveDocumentDisplayName(),
            applicationName: _titleSpec.ApplicationName,
            isDirty: _workflow.IsDirty,
            dirtyMarker: _titleSpec.DirtyMarker,
            separator: _titleSpec.Separator,
            applicationPlacement: _titleSpec.ApplicationPlacement);
    }

    private SaveChangesPrompt PromptSaveChangesSync(string action) =>
        AvaloniaSaveChangesDialog.ShowAsync(
                _owner,
                AvaloniaSaveChangesPromptText.ForDocumentAction(
                    _titleSpec.ApplicationName,
                    _workflow.DisplayName,
                    action))
            .GetAwaiter().GetResult();

    private string ResolveDocumentDisplayName()
    {
        if (_titleSpec.DocumentDisplayName == SisterAvaloniaFileTitleDisplayName.FileName)
            return _workflow.CurrentFileName ?? _titleSpec.UntitledDisplayName;

        return _workflow.DisplayName;
    }

    private void OnWorkflowChanged()
    {
        RefreshTitle();
        _onChanged();
    }
}
