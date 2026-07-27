namespace FreeW.DialogVisualHarness;

public static class DialogSemanticText
{
    public static string ResolveButtonText(string? automationName, string? content, string fallback)
    {
        // WPF can return an empty automation name for a button that still has visible content.
        // Preserve every nonblank automation name so real semantic differences remain visible.
        return string.IsNullOrWhiteSpace(automationName)
            ? content ?? fallback
            : automationName;
    }
}
