namespace Free.Shared.AppServices;

public enum AutosaveRecoveryPromptMode
{
    Startup,
    StartupQuotedDisplayName,
    Manual,
}

public sealed record AutosaveRecoveryPromptText(
    string ProductName,
    string UnsavedItemPlural);

public readonly record struct AutosaveRecoveryWorkflowResult(
    bool AnyAccepted,
    bool AnyRecovered);

public static class AutosaveRecoveryPromptFormatter
{
    public static string Format(
        string displayName,
        int remainingCount,
        AutosaveRecoveryPromptMode promptMode,
        AutosaveRecoveryPromptText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(text.ProductName);
        ArgumentException.ThrowIfNullOrWhiteSpace(text.UnsavedItemPlural);

        return promptMode switch
        {
            AutosaveRecoveryPromptMode.Startup when remainingCount > 1 =>
                $"{text.ProductName} found unsaved changes to {displayName} from a previous session ({remainingCount} unsaved {text.UnsavedItemPlural} found). Recover this one?",
            AutosaveRecoveryPromptMode.Startup =>
                $"{text.ProductName} found unsaved changes to {displayName} from a previous session. Recover them?",
            AutosaveRecoveryPromptMode.StartupQuotedDisplayName when remainingCount > 1 =>
                $"{text.ProductName} found unsaved changes to \"{displayName}\" from a previous session ({remainingCount} unsaved {text.UnsavedItemPlural} found). Recover this one?",
            AutosaveRecoveryPromptMode.StartupQuotedDisplayName =>
                $"{text.ProductName} found unsaved changes to \"{displayName}\" from a previous session. Recover them?",
            AutosaveRecoveryPromptMode.Manual when remainingCount > 1 =>
                $"Recover unsaved changes to {displayName}? ({remainingCount} unsaved {text.UnsavedItemPlural} found.)",
            AutosaveRecoveryPromptMode.Manual =>
                $"Recover unsaved changes to {displayName}?",
            _ => throw new ArgumentOutOfRangeException(nameof(promptMode), promptMode, null),
        };
    }
}

/// <summary>
/// App-neutral recovery sequencing. Apps retain offer types, prompt rendering, and document restore.
/// </summary>
public static class AutosaveRecoveryWorkflow
{
    public static async ValueTask<AutosaveRecoveryWorkflowResult> RunAsync<TPlan, TOffer>(
        IReadOnlyList<TPlan> recoveries,
        AutosaveRecoveryPromptMode promptMode,
        Func<TPlan, int, TOffer> createOffer,
        Func<TOffer, ValueTask<bool>> promptAsync,
        Func<TPlan, bool, ValueTask<bool>> completeRecoveryAsync)
        where TPlan : IAutosaveRecoveryPlan
    {
        ArgumentNullException.ThrowIfNull(recoveries);
        ArgumentNullException.ThrowIfNull(createOffer);
        ArgumentNullException.ThrowIfNull(promptAsync);
        ArgumentNullException.ThrowIfNull(completeRecoveryAsync);

        var anyAccepted = false;
        var anyRecovered = false;
        for (var index = 0; index < recoveries.Count; index++)
        {
            var recovery = recoveries[index];
            var offer = createOffer(recovery, recoveries.Count - index);
            if (!await promptAsync(offer))
            {
                if (promptMode != AutosaveRecoveryPromptMode.Manual)
                    AutosaveRecoveryCandidateProcessor.DiscardDeclined(recovery.Candidate);
                continue;
            }

            var useCurrentWindow = !anyAccepted;
            anyAccepted = true;
            anyRecovered |= await completeRecoveryAsync(recovery, useCurrentWindow);
        }

        return new AutosaveRecoveryWorkflowResult(anyAccepted, anyRecovered);
    }
}
