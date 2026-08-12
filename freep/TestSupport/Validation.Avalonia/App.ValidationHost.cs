namespace FreeP.App.Avalonia;

// Compiled into the isolated validation-host renderer variant only.
public sealed partial class App
{
    internal static bool StartupDirtyTraceEnabledForValidationHost { get; private set; }

    internal static void ConfigureValidationHost(bool enableStartupDirtyTrace) =>
        StartupDirtyTraceEnabledForValidationHost = enableStartupDirtyTrace;
}
