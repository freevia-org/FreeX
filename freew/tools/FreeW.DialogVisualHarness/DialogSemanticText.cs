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
        return RemoveAccessKeyMarkers(resolved);
    }

    private static string RemoveAccessKeyMarkers(string value)
    {
        var normalized = new System.Text.StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '_')
            {
                normalized.Append(value[index]);
                continue;
            }

            if (index + 1 < value.Length && value[index + 1] == '_')
            {
                normalized.Append('_');
                index++;
            }
        }

        return normalized.ToString();
    }
}
