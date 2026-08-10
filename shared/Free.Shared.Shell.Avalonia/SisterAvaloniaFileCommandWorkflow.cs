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
    bool CollapseCleanUntitledTitle = false)
{
    public ApplicationWindowTitleSpec ToApplicationWindowTitleSpec() => new(
        ApplicationName,
        UntitledDisplayName,
        DirtyMarker,
        Separator,
        ApplicationPlacement,
        CollapseCleanUntitledTitle);
}

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
    private readonly Func<string, Task<SaveChangesPrompt>> _promptSaveChangesAsync;
    private readonly Func<Task<bool>> _saveAsync;
    private readonly Func<string, Exception, Task> _showFileCommandErrorAsync;
    private readonly Action? _restoreOwnerFocus;
    private readonly SemaphoreSlim _destructiveActionGate = new(1, 1);

    public SisterAvaloniaFileCommandWorkflow(
        Window owner,
        SisterAvaloniaFileTitleSpec titleSpec,
        Func<int> maxRecentEntries,
        Action onChanged,
        Func<bool>? save = null,
        Func<RecentFilesStore>? loadRecentFilesStore = null,
        Func<Task<bool>>? saveAsync = null,
        Func<string, Task<SaveChangesPrompt>>? promptSaveChangesAsync = null,
        Func<string, Exception, Task>? showFileCommandErrorAsync = null,
        Action? restoreOwnerFocus = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(titleSpec);
        ArgumentNullException.ThrowIfNull(maxRecentEntries);
        ArgumentNullException.ThrowIfNull(onChanged);
        if (save is null && saveAsync is null)
            throw new ArgumentException("A synchronous or asynchronous save callback is required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(titleSpec.ApplicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleSpec.Separator);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleSpec.UntitledDisplayName);

        _owner = owner;
        _titleSpec = titleSpec;
        _onChanged = onChanged;
        _promptSaveChangesAsync = promptSaveChangesAsync ?? PromptSaveChangesAsync;
        _saveAsync = saveAsync ?? (() => Task.FromResult(save!()));
        _showFileCommandErrorAsync = showFileCommandErrorAsync ?? ShowFileCommandErrorCoreAsync;
        _restoreOwnerFocus = restoreOwnerFocus;
        _workflow = new FileCommandWorkflow(
            maxRecentEntries,
            OnWorkflowChanged,
            PromptSaveChangesSync,
            save ?? ThrowSynchronousSaveUnavailable,
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

    public Task<bool> NewAsync(
        string action,
        Func<Task> loadNewDocumentAsync,
        Action? beforeChanged = null)
    {
        ArgumentNullException.ThrowIfNull(loadNewDocumentAsync);

        return RunDestructiveActionAsync(
            action,
            async () =>
            {
                await loadNewDocumentAsync();
                MarkSavedWithoutPath(beforeChanged);
                return true;
            });
    }

    public bool Open(string action, Func<string?> promptPath, Func<string, bool> openPath) =>
        _workflow.Open(action, promptPath, openPath);

    public Task<bool> OpenAsync(
        string action,
        Func<Task<string?>> promptPathAsync,
        Func<string, Task<bool>> openPathAsync)
    {
        ArgumentNullException.ThrowIfNull(promptPathAsync);
        ArgumentNullException.ThrowIfNull(openPathAsync);

        return RunDestructiveActionAsync(
            action,
            async () =>
            {
                var path = await promptPathAsync();
                return !string.IsNullOrWhiteSpace(path) && await openPathAsync(path);
            });
    }

    public Task<bool> SaveAsync(
        Func<string, Task<bool>> saveToCurrentPathAsync,
        Func<Task<bool>> saveAsAsync) =>
        _workflow.SaveAsync(saveToCurrentPathAsync, saveAsAsync);

    public bool ConfirmCloseAllowed(string action = "closing") =>
        _workflow.ConfirmCloseAllowed(action);

    public Task<bool> ConfirmCloseAllowedAsync(string action = "closing") =>
        RunDestructiveActionAsync(action, static () => Task.FromResult(true));

    public Task ShowFileCommandErrorAsync(string summary, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(exception);
        return _showFileCommandErrorAsync(summary, exception);
    }

    public void MarkSavedWithoutPath(Action? beforeChanged = null) =>
        _workflow.MarkSavedWithoutPath(beforeChanged);

    public void MarkSavedWithPath(string path, bool suppressRecentFiles, Action? beforeChanged = null) =>
        _workflow.MarkSavedWithPath(path, suppressRecentFiles, beforeChanged);

    public void RefreshTitle() => _owner.Title = BuildTitle();

    public string BuildTitle()
        => ApplicationWindowTitlePolicy.Compose(
            _titleSpec.ToApplicationWindowTitleSpec(),
            ResolveDocumentDisplayName(),
            _workflow.IsDirty,
            isDefaultDocument: _workflow.CurrentPath is null);

    private SaveChangesPrompt PromptSaveChangesSync(string action) =>
        AvaloniaSaveChangesDialog.ShowAsync(
                _owner,
                AvaloniaSaveChangesPromptText.ForDocumentAction(
                    _titleSpec.ApplicationName,
                    _workflow.DisplayName,
                    action))
            .GetAwaiter().GetResult();

    private Task<SaveChangesPrompt> PromptSaveChangesAsync(string action) =>
        AvaloniaSaveChangesDialog.ShowAsync(
            _owner,
            AvaloniaSaveChangesPromptText.ForDocumentAction(
                _titleSpec.ApplicationName,
                _workflow.DisplayName,
                action));

    private Task ShowFileCommandErrorCoreAsync(string summary, Exception exception) =>
        AvaloniaUserMessageDialog.ShowErrorAsync(
            _owner,
            $"{summary}:\n{exception.Message}",
            _titleSpec.ApplicationName);

    private async Task<bool> RunDestructiveActionAsync(
        string action,
        Func<Task<bool>> destructiveActionAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentNullException.ThrowIfNull(destructiveActionAsync);

        if (!await _destructiveActionGate.WaitAsync(0))
            return false;

        try
        {
            var result = await AsyncFileLifecycleCoordinator.ConfirmBeforeDestructiveActionAsync(
                _workflow.IsDirty,
                () => _promptSaveChangesAsync(action),
                _saveAsync);
            if (result == DirtyGateResult.Cancel)
            {
                _owner.Activate();
                _owner.Focus();
                return false;
            }

            return await destructiveActionAsync();
        }
        finally
        {
            RestoreOwnerFocus();
            _destructiveActionGate.Release();
        }
    }

    private void RestoreOwnerFocus() => _restoreOwnerFocus?.Invoke();

    private static bool ThrowSynchronousSaveUnavailable() =>
        throw new InvalidOperationException("This file workflow is configured for asynchronous save operations.");

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
