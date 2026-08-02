namespace FreeW.DialogVisualHarness;

public static class DialogSemanticText
{
    public static bool TryResolveActionButtonText(
        bool isVisible,
        string? automationName,
        string? content,
        out string actionText)
    {
        actionText = string.Empty;
        if (!isVisible)
            return false;

        var candidate = string.IsNullOrWhiteSpace(automationName) ? content : automationName;
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        actionText = RemoveAccessKeyMarkers(candidate).Trim();
        return actionText.Length > 0;
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
