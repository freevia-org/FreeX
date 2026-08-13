using Free.Shared.AppServices;
using FreeX.App.Avalonia;

namespace FreeX.Validation.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        ValidationHostCommandRouteExecutor.Run(
            args,
            Console.Error,
            $"Expected {MacOsLaunchSmokeOptions.Argument}.",
            ValidationHostCommandRouteExecutor.Immediate(
                PackagingSmokeCommand.TryRun,
                Console.Out,
                Console.Error),
            ValidationHostCommandRouteExecutor.Parsed<PivotRuntimeEvidenceOptions>(
                PivotRuntimeEvidenceOptions.TryParse,
                (options, startupArguments) =>
                    FreeX.App.Avalonia.Program.RunPivotRuntimeObservationHost(
                        startupArguments,
                        access => PivotRuntimeEvidenceCoordinator.Start(access, options, args))),
            ValidationHostCommandRouteExecutor.Parsed<MacOsLaunchSmokeOptions>(
                MacOsLaunchSmokeOptions.TryParse,
                (options, startupArguments) =>
                    FreeX.App.Avalonia.Program.RunValidationToolHost(
                        startupArguments,
                        options.DiagnosticsDirectory,
                        (window, diagnostics) =>
                            MacOsLaunchSmokeCoordinator.Start(window, options, diagnostics))));
}
