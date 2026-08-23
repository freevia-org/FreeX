using Free.Shared.AppServices;

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
    private static readonly AutosaveRecoveryPromptText PromptText =
        new("FreeP", "presentations");

    public string Prompt => AutosaveRecoveryPromptFormatter.Format(
        Recovery.DisplayName,
        RemainingCount,
        FreePRecoveryWorkflow.MapPromptMode(PromptMode),
        PromptText);
}

public readonly record struct FreePRecoveryWorkflowResult(
    bool AnyAccepted,
    bool AnyRecovered);

/// <summary>FreeP compatibility facade over the shared recovery sequencer.</summary>
public static class FreePRecoveryWorkflow
{
    public static async ValueTask<FreePRecoveryWorkflowResult> RunAsync(
        IReadOnlyList<AutosaveRecoveryPlan> recoveries,
        FreePRecoveryPromptMode promptMode,
        Func<FreePRecoveryOffer, ValueTask<bool>> promptAsync,
        Func<AutosaveRecoveryPlan, bool, ValueTask<bool>> completeRecoveryAsync)
    {
        var result = await AutosaveRecoveryWorkflow.RunAsync(
            recoveries,
            MapPromptMode(promptMode),
            (recovery, remainingCount) =>
                new FreePRecoveryOffer(recovery, remainingCount, promptMode),
            promptAsync,
            completeRecoveryAsync);

        return new FreePRecoveryWorkflowResult(result.AnyAccepted, result.AnyRecovered);
    }

    internal static AutosaveRecoveryPromptMode MapPromptMode(
        FreePRecoveryPromptMode promptMode) =>
        (AutosaveRecoveryPromptMode)(int)promptMode;
}
