using FreeX.Core.IO;

namespace FreeX.App.Services;

/// <summary>
/// Shared workbook file-command choreography. Hosts still own prompts, dialogs, storage access,
/// progress rendering, and UI refresh; this coordinator owns the repeated dirty-gate and
/// save-vs-save-as dispatch used by the WPF and Avalonia shells.
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

        if (FileLifecyclePlanner.PlanDirtyGate(isDirty) == DirtyGateIntent.ProceedWithoutPrompt)
            return SaveChangesConfirmation.Continue;

        var prompt = await promptSaveChangesAsync();
        return FileLifecyclePlanner.ResolveDirtyGate(prompt) switch
        {
            DirtyGateAction.Cancel => SaveChangesConfirmation.Cancel,
            DirtyGateAction.ProceedDiscardingChanges => SaveChangesConfirmation.DiscardWithoutSaving,
            DirtyGateAction.SaveThenProceed => await saveCurrentAsync()
                ? SaveChangesConfirmation.Continue
                : SaveChangesConfirmation.Cancel,
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

        if (FileLifecyclePlanner.PlanSave(isDirty, currentFilePath) == FileSaveIntent.PromptSaveAs)
            return await saveAsAsync();

        var target = resolveCurrentTarget();
        return target is null
            ? await saveAsAsync()
            : await saveTargetAsync(target);
    }
}
