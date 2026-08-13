namespace Free.Shared.AppServices;

/// <summary>
/// Final result of an async dirty gate after any requested save has already completed.
/// </summary>
public enum DirtyGateResult
{
    /// <summary>Abort the destructive action and keep the current document open.</summary>
    Cancel,

    /// <summary>Proceed because the document was clean or the requested save succeeded.</summary>
    Proceed,

    /// <summary>Proceed without saving the current dirty document.</summary>
    ProceedDiscardingChanges
}

/// <summary>
/// Host-owned decision for a successfully resolved save target.
/// </summary>
public enum ResolvedSaveTargetDecision
{
    Write,
    Skip
}

/// <summary>
/// Async file-lifecycle choreography shared by document hosts. Hosts still own prompts,
/// dialogs, target resolution, storage, progress UI, and document-model state.
/// </summary>
public static class AsyncFileLifecycleCoordinator
{
    public static async Task<DirtyGateResult> ConfirmBeforeDestructiveActionAsync(
        bool isDirty,
        Func<Task<SaveChangesPrompt>> promptSaveChangesAsync,
        Func<Task<bool>> saveCurrentAsync)
    {
        ArgumentNullException.ThrowIfNull(promptSaveChangesAsync);
        ArgumentNullException.ThrowIfNull(saveCurrentAsync);

        if (FileLifecyclePlanner.PlanDirtyGate(isDirty) == DirtyGateIntent.ProceedWithoutPrompt)
            return DirtyGateResult.Proceed;

        var prompt = await promptSaveChangesAsync();
        return FileLifecyclePlanner.ResolveDirtyGate(prompt) switch
        {
            DirtyGateAction.Cancel => DirtyGateResult.Cancel,
            DirtyGateAction.ProceedDiscardingChanges => DirtyGateResult.ProceedDiscardingChanges,
            DirtyGateAction.SaveThenProceed => await saveCurrentAsync()
                ? DirtyGateResult.Proceed
                : DirtyGateResult.Cancel,
            _ => DirtyGateResult.Cancel
        };
    }

    public static async Task<bool> SaveResolvedAsync<TTarget>(
        bool isDirty,
        string? currentFilePath,
        Func<TTarget?> resolveCurrentTarget,
        Func<TTarget, Task<bool>> saveTargetAsync,
        Func<Task<bool>> saveAsAsync,
        Func<TTarget, ResolvedSaveTargetDecision>? resolvedTargetPolicy = null)
        where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(resolveCurrentTarget);
        ArgumentNullException.ThrowIfNull(saveTargetAsync);
        ArgumentNullException.ThrowIfNull(saveAsAsync);

        if (FileLifecyclePlanner.PlanSave(isDirty, currentFilePath) == FileSaveIntent.PromptSaveAs)
            return await saveAsAsync();

        var target = resolveCurrentTarget();
        if (target is null)
            return await saveAsAsync();

        return resolvedTargetPolicy?.Invoke(target) == ResolvedSaveTargetDecision.Skip
            ? true
            : await saveTargetAsync(target);
    }
}
