namespace FreeW.App.Presentation.Shell;

public enum FreeWRecoveryPromptMode
{
    Startup,
    StartupQuotedDisplayName,
    Manual,
}

public sealed record FreeWRecoveryOffer(
    AutosaveRecoveryPlan Recovery,
    int RemainingCount,
    FreeWRecoveryPromptMode PromptMode)
{
    public string Prompt => PromptMode switch
    {
        FreeWRecoveryPromptMode.Startup when RemainingCount > 1 =>
            $"FreeW found unsaved changes to {Recovery.DisplayName} from a previous session ({RemainingCount} unsaved documents found). Recover this one?",
        FreeWRecoveryPromptMode.Startup =>
            $"FreeW found unsaved changes to {Recovery.DisplayName} from a previous session. Recover them?",
        FreeWRecoveryPromptMode.StartupQuotedDisplayName when RemainingCount > 1 =>
            $"FreeW found unsaved changes to \"{Recovery.DisplayName}\" from a previous session ({RemainingCount} unsaved documents found). Recover this one?",
        FreeWRecoveryPromptMode.StartupQuotedDisplayName =>
            $"FreeW found unsaved changes to \"{Recovery.DisplayName}\" from a previous session. Recover them?",
        FreeWRecoveryPromptMode.Manual when RemainingCount > 1 =>
            $"Recover unsaved changes to {Recovery.DisplayName}? ({RemainingCount} unsaved documents found.)",
        FreeWRecoveryPromptMode.Manual =>
            $"Recover unsaved changes to {Recovery.DisplayName}?",
        _ => throw new ArgumentOutOfRangeException(nameof(PromptMode), PromptMode, null),
    };
}

public readonly record struct FreeWRecoveryWorkflowResult(
    bool AnyAccepted,
    bool AnyRecovered);

/// <summary>
/// Owns renderer-neutral recovery sequencing. Native hosts supply the modal prompt and restore the
/// accepted document in the current or a new native window.
/// </summary>
public static class FreeWRecoveryWorkflow
{
    public static async ValueTask<FreeWRecoveryWorkflowResult> RunAsync(
        IReadOnlyList<AutosaveRecoveryPlan> recoveries,
        FreeWRecoveryPromptMode promptMode,
        Func<FreeWRecoveryOffer, ValueTask<bool>> promptAsync,
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
            var offer = new FreeWRecoveryOffer(
                recovery,
                RemainingCount: recoveries.Count - index,
                promptMode);
            if (!await promptAsync(offer))
                continue;

            var useCurrentWindow = !anyAccepted;
            anyAccepted = true;
            anyRecovered |= await completeRecoveryAsync(recovery, useCurrentWindow);
        }

        return new FreeWRecoveryWorkflowResult(anyAccepted, anyRecovered);
    }
}
