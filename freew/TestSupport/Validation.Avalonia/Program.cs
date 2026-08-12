using FreeW.App.Avalonia;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;

namespace FreeW.Validation.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        ValidationHostCommandRouteExecutor.Run(
            args,
            Console.Error,
            $"Expected {SisterAppPackagingSmoke.Argument}, {ReadAloudPauseSmoke.Argument}, " +
            $"{SisterAppLaunchSmokeOptions.Argument}, or {TablePropertiesX11ValidationOptions.Argument}.",
            ValidationHostCommandRouteExecutor.Immediate(
                PackagingSmoke.TryRun,
                Console.Out,
                Console.Error),
            ValidationHostCommandRouteExecutor.Immediate(
                ReadAloudPauseSmoke.TryRun,
                Console.Out,
                Console.Error),
            ValidationHostCommandRouteExecutor.Parsed<SisterAppLaunchSmokeOptions>(
                SisterAppLaunchSmokeOptions.TryParse,
                (options, startupArguments) =>
                    FreeW.App.Avalonia.Program.RunToolHost(
                        startupArguments,
                        access => LaunchSmokeCoordinator.Start(access, options))),
            ValidationHostCommandRouteExecutor.Parsed<TablePropertiesX11ValidationOptions>(
                TablePropertiesX11ValidationOptions.TryParse,
                (options, startupArguments) =>
                    FreeW.App.Avalonia.Program.RunToolHost(
                        startupArguments,
                        access => TablePropertiesX11ValidationCoordinator.Start(access, options))));
}
