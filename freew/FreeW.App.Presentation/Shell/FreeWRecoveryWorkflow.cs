using Free.Shared.AppServices;

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
    private static readonly AutosaveRecoveryPromptText PromptText =
        new("FreeW", "documents");

    public string Prompt => AutosaveRecoveryPromptFormatter.Format(
        Recovery.DisplayName,
        RemainingCount,
        FreeWRecoveryWorkflow.MapPromptMode(PromptMode),
        PromptText);
}

public readonly record struct FreeWRecoveryWorkflowResult(
    bool AnyAccepted,
    bool AnyRecovered);

/// <summary>FreeW compatibility facade over the shared recovery sequencer.</summary>
public static class FreeWRecoveryWorkflow
{
    public static async ValueTask<FreeWRecoveryWorkflowResult> RunAsync(
        IReadOnlyList<AutosaveRecoveryPlan> recoveries,
        FreeWRecoveryPromptMode promptMode,
        Func<FreeWRecoveryOffer, ValueTask<bool>> promptAsync,
        Func<AutosaveRecoveryPlan, bool, ValueTask<bool>> completeRecoveryAsync)
    {
        var result = await AutosaveRecoveryWorkflow.RunAsync(
            recoveries,
            MapPromptMode(promptMode),
            (recovery, remainingCount) =>
                new FreeWRecoveryOffer(recovery, remainingCount, promptMode),
            promptAsync,
            completeRecoveryAsync);

        return new FreeWRecoveryWorkflowResult(result.AnyAccepted, result.AnyRecovered);
    }

    internal static AutosaveRecoveryPromptMode MapPromptMode(
        FreeWRecoveryPromptMode promptMode) =>
        (AutosaveRecoveryPromptMode)(int)promptMode;
}
