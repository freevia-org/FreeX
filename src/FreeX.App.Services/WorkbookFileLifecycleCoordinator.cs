using FreeX.Core.IO;

namespace FreeX.App.Services;

/// <summary>
/// Workbook-specific adapter over the shared async file-command choreography. Hosts still own
/// prompts, dialogs, storage access, progress rendering, and UI refresh.
/// </summary>
public static class WorkbookFileLifecycleCoordinator
{
    public static async Task<SaveChangesConfirmation> ConfirmBeforeDestructiveActionAsync(
        bool isDirty,
        Func<Task<SaveChangesPrompt>> promptSaveChangesAsync,
        Func<Task<bool>> saveCurrentAsync)
    {
        ArgumentNullException.ThrowIfNull(promptSaveChangesAsync);
        ArgumentNullException.ThrowIfNull(saveCurrentAsync);

        var result = await AsyncFileLifecycleCoordinator.ConfirmBeforeDestructiveActionAsync(
            isDirty,
            promptSaveChangesAsync,
            saveCurrentAsync);

        return result switch
        {
            DirtyGateResult.Cancel => SaveChangesConfirmation.Cancel,
            DirtyGateResult.Proceed => SaveChangesConfirmation.Continue,
            DirtyGateResult.ProceedDiscardingChanges => SaveChangesConfirmation.DiscardWithoutSaving,
            _ => SaveChangesConfirmation.Cancel
        };
    }

    public static Task<bool> SaveResolvedAsync(
        bool isDirty,
        string? currentFilePath,
        IEnumerable<IFileAdapter> adapters,
        Func<FileSaveTarget, Task<bool>> saveTargetAsync,
        Func<Task<bool>> saveAsAsync)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        return SaveResolvedAsync(
            isDirty,
            currentFilePath,
            () => FileSavePlanner.TryResolveExistingPath(currentFilePath, adapters, out var target)
                ? target
                : null,
            saveTargetAsync,
            saveAsAsync);
    }

    public static async Task<bool> SaveResolvedAsync(
        bool isDirty,
        string? currentFilePath,
        Func<FileSaveTarget?> resolveCurrentTarget,
        Func<FileSaveTarget, Task<bool>> saveTargetAsync,
        Func<Task<bool>> saveAsAsync)
    {
        ArgumentNullException.ThrowIfNull(resolveCurrentTarget);
        ArgumentNullException.ThrowIfNull(saveTargetAsync);
        ArgumentNullException.ThrowIfNull(saveAsAsync);

        return await AsyncFileLifecycleCoordinator.SaveResolvedAsync(
            isDirty,
            currentFilePath,
            resolveCurrentTarget,
            saveTargetAsync,
            saveAsAsync);
    }
}
