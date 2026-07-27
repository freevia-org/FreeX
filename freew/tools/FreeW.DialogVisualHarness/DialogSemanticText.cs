namespace FreeW.DialogVisualHarness;

public static class DialogSemanticText
{
    public static string ResolveButtonText(string? automationName, string? content, string fallback)
    {
        // WPF stores access-key markers in Content while Avalonia may expose them through the
        // automation name. Resolve both to the same user-facing action label.
        var resolved = string.IsNullOrWhiteSpace(automationName)
            ? content ?? fallback
            : automationName;
        return resolved.Replace("_", string.Empty, StringComparison.Ordinal);
    }
}
