using FreeX.App.Avalonia;

namespace FreeX.Validation.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!PivotRuntimeEvidenceOptions.TryParse(
                args,
                out var pivotOptions,
                out var remainingArguments,
                out var pivotError))
        {
            Console.Error.WriteLine(pivotError);
            return 2;
        }

        if (pivotOptions is not null)
        {
            return FreeX.App.Avalonia.Program.RunPivotRuntimeObservationHost(
                remainingArguments,
                access => PivotRuntimeEvidenceCoordinator.Start(access, pivotOptions, args));
        }

        if (!MacOsLaunchSmokeOptions.TryParse(
                remainingArguments,
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
