namespace FreeP.App.Avalonia;

// Compiled into the isolated validation-host renderer variant only.
internal static partial class Program
{
    internal static int RunToolHost(
        IReadOnlyList<string> startupArguments,
        bool enableStartupDirtyTrace,
        Action<MainWindow.ValidationAccessAdapter> coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);

        App.ConfigureValidationHost(enableStartupDirtyTrace);
        return RunToolHostCore(
            startupArguments,
            window => coordinator(window.CreateValidationAccessAdapter()));
    }
}
