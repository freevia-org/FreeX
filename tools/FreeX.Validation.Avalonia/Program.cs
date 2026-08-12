using FreeX.App.Avalonia;

namespace FreeX.Validation.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!MacOsLaunchSmokeOptions.TryParse(
                args,
                out var options,
                out var startupArguments,
                out var error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        if (options is null)
        {
            Console.Error.WriteLine($"Expected {MacOsLaunchSmokeOptions.Argument}.");
            return 2;
        }

        return FreeX.App.Avalonia.Program.RunToolHost(
            startupArguments,
            options.DiagnosticsDirectory,
            (window, diagnostics) => MacOsLaunchSmokeCoordinator.Start(window, options, diagnostics));
    }
}
