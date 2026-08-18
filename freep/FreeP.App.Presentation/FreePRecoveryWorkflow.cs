namespace FreeP.App.Compositor;

public enum FreePRecoveryPromptMode
{
    Startup,
    StartupQuotedDisplayName,
    Manual,
}

public sealed record FreePRecoveryOffer(
    AutosaveRecoveryPlan Recovery,
    int RemainingCount,
    FreePRecoveryPromptMode PromptMode)
{
    public string Prompt => PromptMode switch
    {
        FreePRecoveryPromptMode.Startup when RemainingCount > 1 =>
            $"FreeP found unsaved changes to {Recovery.DisplayName} from a previous session ({RemainingCount} unsaved presentations found). Recover this one?",
        FreePRecoveryPromptMode.Startup =>
            $"FreeP found unsaved changes to {Recovery.DisplayName} from a previous session. Recover them?",
        FreePRecoveryPromptMode.StartupQuotedDisplayName when RemainingCount > 1 =>
            $"FreeP found unsaved changes to \"{Recovery.DisplayName}\" from a previous session ({RemainingCount} unsaved presentations found). Recover this one?",
        FreePRecoveryPromptMode.StartupQuotedDisplayName =>
            $"FreeP found unsaved changes to \"{Recovery.DisplayName}\" from a previous session. Recover them?",
        FreePRecoveryPromptMode.Manual when RemainingCount > 1 =>
            $"Recover unsaved changes to {Recovery.DisplayName}? ({RemainingCount} unsaved presentations found.)",
        FreePRecoveryPromptMode.Manual =>
            $"Recover unsaved changes to {Recovery.DisplayName}?",
        _ => throw new ArgumentOutOfRangeException(nameof(PromptMode), PromptMode, null),
    };
}

public readonly record struct FreePRecoveryWorkflowResult(
    bool AnyAccepted,
    bool AnyRecovered);

/// <summary>
/// Owns renderer-neutral recovery sequencing. Native hosts supply the modal prompt and restore the
/// accepted presentation in the current or a new native window. Mirrors FreeW's
/// <c>FreeWRecoveryWorkflow</c>.
/// </summary>
public static class FreePRecoveryWorkflow
{
    public static async ValueTask<FreePRecoveryWorkflowResult> RunAsync(
        IReadOnlyList<AutosaveRecoveryPlan> recoveries,
        FreePRecoveryPromptMode promptMode,
        Func<FreePRecoveryOffer, ValueTask<bool>> promptAsync,
        Func<AutosaveRecoveryPlan, bool, ValueTask<bool>> completeRecoveryAsync)
    {
        ArgumentNullException.ThrowIfNull(recoveries);
        ArgumentNullException.ThrowIfNull(promptAsync);
        ArgumentNullException.ThrowIfNull(completeRecoveryAsync);

        var anyAccepted = false;
        var anyRecovered = false;
        for (var index = 0; index < recoveries.Count; index++)
        {
            var recovery = recoveries[index];
            var offer = new FreePRecoveryOffer(
                recovery,
                RemainingCount: recoveries.Count - index,
                promptMode);
            if (!await promptAsync(offer))
            {
                // A startup offer is unprompted and repeats on every launch, so a decline here must
                // stick -- otherwise the same stale snapshot nags forever (matches FreeX's own
                // startup workflow, which discards a declined snapshot the same way). The manual
                // "Recover Unsaved Presentations" command is opt-in and browsable, so a decline there
                // leaves the candidate for the user to revisit later.
                if (promptMode != FreePRecoveryPromptMode.Manual)
                    AutosaveRecoveryCandidateProcessor.DiscardDeclined(recovery.Candidate);
                continue;
            }

            var useCurrentWindow = !anyAccepted;
            anyAccepted = true;
            anyRecovered |= await completeRecoveryAsync(recovery, useCurrentWindow);
        }

        return new FreePRecoveryWorkflowResult(anyAccepted, anyRecovered);
    }
}
