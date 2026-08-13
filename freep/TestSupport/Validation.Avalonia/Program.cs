using FreeP.App.Avalonia;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;

namespace FreeP.Validation.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        ValidationHostCommandRouteExecutor.Run(
            args,
            Console.Error,
            $"Expected {SisterAppPackagingSmoke.Argument}, {SisterAppLaunchSmokeOptions.Argument}, " +
            $"{PhysicalValidationOptions.Argument}, {AccessibilityValidationOptions.Argument}, " +
            $"or {StartupDirtyTraceOptions.Argument}.",
            ValidationHostCommandRouteExecutor.Immediate(
                PackagingSmokeCommand.TryRun,
                Console.Out,
                Console.Error),
            ValidationHostCommandRouteExecutor.Parsed<PhysicalFixtureOptions>(
                PhysicalFixtureOptions.TryParse,
                (options, startupArguments) =>
                    Run(
                        startupArguments,
                        false,
                        access => PhysicalFixtureCoordinator.Start(access, options))),
            ValidationHostCommandRouteExecutor.Parsed<PhysicalValidationOptions>(
                PhysicalValidationOptions.TryParse,
                (options, startupArguments) =>
                    Run(
                        startupArguments,
                        false,
                        access => PhysicalValidationCoordinator.Start(access, options))),
            ValidationHostCommandRouteExecutor.Parsed<AccessibilityValidationOptions>(
                AccessibilityValidationOptions.TryParse,
                (options, startupArguments) =>
                    Run(
                        startupArguments,
                        false,
                        access => AccessibilityValidationCoordinator.Start(access, options))),
            ValidationHostCommandRouteExecutor.Parsed<StartupDirtyTraceOptions>(
                StartupDirtyTraceOptions.TryParse,
                (options, startupArguments) =>
                    Run(
                        startupArguments,
                        true,
                        access => StartupDirtyTraceCoordinator.Start(access, options))),
            ValidationHostCommandRouteExecutor.Parsed<SisterAppLaunchSmokeOptions>(
                SisterAppLaunchSmokeOptions.TryParse,
                (options, startupArguments) =>
                    Run(
                        startupArguments,
                        false,
                        access => LaunchSmokeCoordinator.Start(access, options))));

    private static int Run(
        IReadOnlyList<string> startupArguments,
        bool enableStartupDirtyTrace,
        Action<MainWindow.ValidationAccessAdapter> coordinator) =>
        FreeP.App.Avalonia.Program.RunToolHost(
            startupArguments,
            enableStartupDirtyTrace,
            coordinator);

}
