using FreeX.Core.IO;

namespace FreeX.App.Services;

public enum WorkbookSaveTargetIntent
{
    Write,
    SkipCleanCurrentPath
}

public sealed record WorkbookSavePathNormalizationPlan(
    string Path,
    bool ShouldConfirmOverwrite);

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

    public static async Task<bool> CanProceedAfterDirtyGateAsync(
        bool isDirty,
        Func<Task<SaveChangesPrompt>> promptSaveChangesAsync,
        Func<Task<bool>> saveCurrentAsync)
    {
        var confirmation = await ConfirmBeforeDestructiveActionAsync(
            isDirty,
            promptSaveChangesAsync,
            saveCurrentAsync);
        return confirmation != SaveChangesConfirmation.Cancel;
    }

    public static async Task<bool> CanProceedAfterDirtyGateWithCleanSaveAsync(
        bool isDirty,
        Func<Task<SaveChangesPrompt>> promptSaveChangesAsync,
        Func<Task<bool>> saveCurrentAsync,
        Func<bool> isDirtyNow)
    {
        ArgumentNullException.ThrowIfNull(isDirtyNow);

        var confirmation = await ConfirmBeforeDestructiveActionAsync(
            isDirty,
            promptSaveChangesAsync,
            async () => await saveCurrentAsync() && !isDirtyNow());
        return confirmation != SaveChangesConfirmation.Cancel;
    }

    public static async Task<bool> RunAfterDirtyGateAsync(
        bool isDirty,
        Func<Task<SaveChangesPrompt>> promptSaveChangesAsync,
        Func<Task<bool>> saveCurrentAsync,
        Func<Task> runActionAsync)
    {
        ArgumentNullException.ThrowIfNull(runActionAsync);

        if (!await CanProceedAfterDirtyGateAsync(isDirty, promptSaveChangesAsync, saveCurrentAsync))
            return false;

        await runActionAsync();
        return true;
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

    public static Task<bool> SaveResolvedAsync(
        bool isDirty,
        string? currentFilePath,
        Func<FileSaveTarget?> resolveCurrentTarget,
        Func<FileSaveTarget, Task<bool>> saveTargetAsync,
        Func<Task<bool>> saveAsAsync) =>
        AsyncFileLifecycleCoordinator.SaveResolvedAsync(
            isDirty,
            currentFilePath,
            resolveCurrentTarget,
            saveTargetAsync,
            saveAsAsync,
            resolvedTargetPolicy: target =>
                PlanSaveTargetWrite(isDirty, currentFilePath, target) == WorkbookSaveTargetIntent.SkipCleanCurrentPath
                    ? ResolvedSaveTargetDecision.Skip
                    : ResolvedSaveTargetDecision.Write);

    public static WorkbookSaveTargetIntent PlanSaveTargetWrite(
        bool isDirty,
        string? currentFilePath,
        FileSaveTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return FileSavePlanner.CanSkipCleanSave(isDirty, currentFilePath, target)
            ? WorkbookSaveTargetIntent.SkipCleanCurrentPath
            : WorkbookSaveTargetIntent.Write;
    }

    public static WorkbookSavePathNormalizationPlan PlanSavePathNormalization(
        string requestedPath,
        string defaultExtension,
        Func<string, bool> pathExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultExtension);
        ArgumentNullException.ThrowIfNull(pathExists);

        var normalizedPath = WorkbookSession.EnsureSaveExtension(requestedPath, defaultExtension);
        var shouldConfirmOverwrite =
            !PathsEqual(requestedPath, normalizedPath) &&
            pathExists(normalizedPath);

        return new WorkbookSavePathNormalizationPlan(normalizedPath, shouldConfirmOverwrite);
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                PlatformPathComparison);
        }
        catch (ArgumentException)
        {
            return string.Equals(left, right, PlatformPathComparison);
        }
        catch (NotSupportedException)
        {
            return string.Equals(left, right, PlatformPathComparison);
        }
        catch (PathTooLongException)
        {
            return string.Equals(left, right, PlatformPathComparison);
        }
    }

    private static StringComparison PlatformPathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
