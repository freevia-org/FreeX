namespace FreeW.Core.Model;

/// <summary>Owns the precedence between the user-authored AutoCorrect table and AutoFormat rules.</summary>
public static class AutoCorrectEvaluationPolicy
{
    public static AutoCorrectResult Evaluate(
        string? textBefore,
        char justTyped,
        AutoCorrectOptions? autoCorrectOptions,
        AutoFormatOptions? autoFormatOptions)
    {
        var result = AutoCorrectEngine.Evaluate(textBefore, justTyped, autoCorrectOptions);
        return result.Applies
            ? result
            : AutoCorrect.Evaluate(textBefore, justTyped, autoFormatOptions);
    }
}
