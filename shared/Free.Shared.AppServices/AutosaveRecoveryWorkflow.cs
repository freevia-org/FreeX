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
    /// <summary>
    /// Resource keys for the prompt body, one per prompt mode and plurality. Every template takes
    /// {0} = document name, {1} = remaining count (multiple variants only) and {2} = the localized
    /// "last autosaved" time, matching FreeX's <c>Startup_RecoveryPrompt*</c> contract.
    /// </summary>
    public static IReadOnlyList<string> RequiredResourceKeys { get; } =
        Enum.GetValues<AutosaveRecoveryPromptMode>()
            .SelectMany(mode => new[] { ResourceKey(mode, multiple: false), ResourceKey(mode, multiple: true) })
            .ToArray();

    public static string ResourceKey(AutosaveRecoveryPromptMode promptMode, bool multiple) =>
        "Autosave_Recovery_Prompt_" + promptMode + (multiple ? "_Multiple" : string.Empty);

    /// <summary>The English template for a prompt mode (the fallback and the neutral resx value).</summary>
    public static string DefaultTemplate(
        AutosaveRecoveryPromptMode promptMode,
        bool multiple,
        AutosaveRecoveryPromptText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(text.ProductName);
        ArgumentException.ThrowIfNullOrWhiteSpace(text.UnsavedItemPlural);

        var counted = $"({{1}} unsaved {text.UnsavedItemPlural} found)";
        return promptMode switch
        {
            AutosaveRecoveryPromptMode.Startup when multiple =>
                $"{text.ProductName} found unsaved changes to {{0}} from a previous session {counted}. Recover this one? (Last autosaved: {{2}})",
            AutosaveRecoveryPromptMode.Startup =>
                $"{text.ProductName} found unsaved changes to {{0}} from a previous session. Recover them? (Last autosaved: {{2}})",
            AutosaveRecoveryPromptMode.StartupQuotedDisplayName when multiple =>
                $"{text.ProductName} found unsaved changes to \"{{0}}\" from a previous session {counted}. Recover this one? (Last autosaved: {{2}})",
            AutosaveRecoveryPromptMode.StartupQuotedDisplayName =>
                $"{text.ProductName} found unsaved changes to \"{{0}}\" from a previous session. Recover them? (Last autosaved: {{2}})",
            AutosaveRecoveryPromptMode.Manual when multiple =>
                $"Recover unsaved changes to {{0}}? ({{1}} unsaved {text.UnsavedItemPlural} found.) (Last autosaved: {{2}})",
            AutosaveRecoveryPromptMode.Manual =>
                "Recover unsaved changes to {0}? (Last autosaved: {2})",
            _ => throw new ArgumentOutOfRangeException(nameof(promptMode), promptMode, null),
        };
    }

    public static string Format(
        string displayName,
        int remainingCount,
        AutosaveRecoveryPromptMode promptMode,
        AutosaveRecoveryPromptText text,
        string savedAtText,
        Func<string, string?>? getText = null,
        IFormatProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var multiple = remainingCount > 1;
        var template = new ResourceTextDescriptor(
                ResourceKey(promptMode, multiple),
                DefaultTemplate(promptMode, multiple, text))
            .Resolve(getText);
        return string.Format(provider, template, displayName, remainingCount, savedAtText);
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
