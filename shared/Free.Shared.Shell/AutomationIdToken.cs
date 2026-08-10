namespace Free.Shared.Shell;

public static class AutomationIdToken
{
    public static string AppendSegment(string automationId, string? suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);
        return suffix is null
            ? automationId
            : $"{automationId}.{suffix}";
    }

    public static string KeepLettersAndDigits(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Concat(value.Where(char.IsLetterOrDigit));
    }
}
