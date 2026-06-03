using Xunit;

[AttributeUsage(AttributeTargets.Method)]
public sealed class WindowsClipboardFactAttribute : FactAttribute
{
    private const string EnabledEnvironmentVariable = "FREEX_RUN_WINDOWS_CLIPBOARD_TESTS";
    private const string SkipReason =
        "Windows clipboard integration test skipped by default. Set FREEX_RUN_WINDOWS_CLIPBOARD_TESTS=1 to run it.";

    public WindowsClipboardFactAttribute()
    {
        var value = Environment.GetEnvironmentVariable(EnabledEnvironmentVariable);
        if (!string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
        {
            Skip = SkipReason;
        }
    }
}
