namespace Free.Shared.Shell;

public static class AutomationIdToken
{
    public static string KeepLettersAndDigits(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Concat(value.Where(char.IsLetterOrDigit));
    }
}
