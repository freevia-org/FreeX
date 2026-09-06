using Free.Shared.AppServices;

namespace FreeP.App.Compositor;

/// <summary>
/// Portable presentation lifecycle port over the shared file-command workflow. Hosts may supply
/// asynchronous destructive-action hooks when their native save prompt cannot run synchronously.
/// </summary>
public sealed class PresentationFileLifecycleAdapter : IPresentationFileLifecyclePort
{
    private readonly FileCommandWorkflow _workflow;
    private readonly Func<string, Func<Task>, Task<bool>> _newAsync;
    private readonly Func<string, Func<Task<string?>>, Func<string, Task<bool>>, Task<bool>> _openAsync;
    private readonly Func<string, Task<bool>> _confirmCloseAllowedAsync;

    public PresentationFileLifecycleAdapter(
        FileCommandWorkflow workflow,
        Func<string, Func<Task>, Task<bool>>? newAsync = null,
        Func<string, Func<Task<string?>>, Func<string, Task<bool>>, Task<bool>>? openAsync = null,
        Func<string, Task<bool>>? confirmCloseAllowedAsync = null)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _newAsync = newAsync ?? ((action, load) => _workflow.NewAsync(action, load));
        _openAsync = openAsync ?? _workflow.OpenAsync;
        // r489: this fallback is an async-SHAPED wrapper around a SYNCHRONOUS dirty gate, which
        // prompts by blocking until a dialog returns. That is safe only on a toolkit whose modal
        // dialog pumps a nested message loop; on one that does not, blocking the UI thread waits
        // for a result only that thread can produce, which is a deadlock. A renderer that cannot
        // guarantee the nested pump must pass confirmCloseAllowedAsync rather than take this
        // default. (Deliberately toolkit-neutral wording: this file is the portable tier and a
        // source contract keeps toolkit names out of it -- the named warning lives beside the
        // implementation that actually blocks.)
        _confirmCloseAllowedAsync = confirmCloseAllowedAsync ??
            (action => Task.FromResult(_workflow.ConfirmCloseAllowed(action)));
    }

    public bool IsDirty => _workflow.IsDirty;
    public int DirtyGeneration => _workflow.DirtyGeneration;
    public string? CurrentPath => _workflow.CurrentPath;
    public string? CurrentFileName => _workflow.CurrentFileName;
    public string DisplayName => _workflow.DisplayName;
    public IReadOnlyList<RecentFileEntry> RecentEntries => _workflow.RecentEntries;

    public void MarkDirty() => _workflow.MarkDirty();

    public void MarkDirtyWithPath(string? path) => _workflow.MarkDirtyWithPath(path);

    public void MarkSavedWithoutPath() => _workflow.MarkSavedWithoutPath();

    public void MarkSavedWithPath(string path, bool suppressRecentFiles) =>
        _workflow.MarkSavedWithPath(path, suppressRecentFiles);

    /// <summary>See <see cref="FileCommandWorkflow.MarkSavedAtUndoDepth"/>.</summary>
    public void MarkSavedAtUndoDepth(int undoDepthAtSave, long undoStackVersionAtSave) =>
        _workflow.MarkSavedAtUndoDepth(undoDepthAtSave, undoStackVersionAtSave);

    /// <summary>See <see cref="FileCommandWorkflow.TryMarkCleanIfAtSavePoint"/>.</summary>
    public bool TryMarkCleanIfAtSavePoint(int currentUndoDepth, long currentUndoStackVersion) =>
        _workflow.TryMarkCleanIfAtSavePoint(currentUndoDepth, currentUndoStackVersion);

    public Task<bool> NewAsync(string action, Func<Task> loadNewPresentationAsync) =>
        _newAsync(action, loadNewPresentationAsync);

    public Task<bool> OpenAsync(
        string action,
        Func<Task<string?>> pickPathAsync,
        Func<string, Task<bool>> openPathAsync) =>
        _openAsync(action, pickPathAsync, openPathAsync);

    public Task<bool> SaveAsync(
        Func<string, Task<bool>> saveToCurrentPathAsync,
        Func<Task<bool>> saveAsAsync) =>
        _workflow.SaveAsync(saveToCurrentPathAsync, saveAsAsync);

    public Task<bool> ConfirmCloseAllowedAsync(string action) =>
        _confirmCloseAllowedAsync(action);
}
