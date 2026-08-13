namespace FreeW.Core.Model;

/// <summary>
/// Renderer-neutral decision layer for one as-you-type character. It gives the user-editable AutoCorrect
/// table first refusal, falls back to AutoFormat As You Type, applies the list-start capitalization rule,
/// and rejects results that would delete before the start of the paragraph. Renderers only apply the
/// returned text/formatting outcome through their native editing primitives.
/// </summary>
public static class AutoCorrectTypingPlanner
{
    public static AutoCorrectTypingPlan Build(
        string? textBeforeCaret,
        char justTyped,
        bool enabled,
        AutoCorrectOptions? autoCorrectOptions,
        AutoFormatOptions? autoFormatOptions,
        bool suppressCapitalizationAtListStart = false)
    {
        var textBefore = textBeforeCaret ?? string.Empty;
        if (!enabled)
            return AutoCorrectTypingPlan.None;

        var result = AutoCorrectEngine.Evaluate(textBefore, justTyped, autoCorrectOptions);
        if (!result.Applies)
        {
            var effectiveAutoFormat = autoFormatOptions ?? AutoFormatOptions.Default;
            if (suppressCapitalizationAtListStart && effectiveAutoFormat.Capitalization)
                effectiveAutoFormat = effectiveAutoFormat with { Capitalization = false };

            result = AutoCorrect.Evaluate(textBefore, justTyped, effectiveAutoFormat);
        }

        if (!result.Applies || result.DeleteBefore > textBefore.Length)
            return AutoCorrectTypingPlan.None;

        return new AutoCorrectTypingPlan(result, textBefore.Length - result.DeleteBefore);
    }
}

public readonly record struct AutoCorrectTypingPlan(
    AutoCorrectResult Result,
    int ReplacementStartOffset)
{
    public static AutoCorrectTypingPlan None { get; } = new(AutoCorrectResult.None, -1);

    public bool Applies => Result.Applies;
}
