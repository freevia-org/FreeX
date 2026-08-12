using FreeP.App.Avalonia;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;

namespace FreeP.Validation.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (PackagingSmokeCommand.TryRun(args, Console.Out, Console.Error, out var packagingExitCode))
            return packagingExitCode;

        if (!PhysicalFixtureOptions.TryParse(args, out var fixture, out var remaining, out var error))
            return ReportError(error);
        if (fixture is not null)
            return Run(remaining, false, access => PhysicalFixtureCoordinator.Start(access, fixture));

        if (!PhysicalValidationOptions.TryParse(args, out var physical, out remaining, out error))
            return ReportError(error);
        if (physical is not null)
            return Run(remaining, false, access => PhysicalValidationCoordinator.Start(access, physical));

        if (!AccessibilityValidationOptions.TryParse(args, out var accessibility, out remaining, out error))
            return ReportError(error);
        if (accessibility is not null)
            return Run(remaining, false, access => AccessibilityValidationCoordinator.Start(access, accessibility));

        if (!StartupDirtyTraceOptions.TryParse(args, out var startupDirty, out remaining, out var traceError))
            return ReportError(traceError);
        if (startupDirty is not null)
            return Run(remaining, true, access => StartupDirtyTraceCoordinator.Start(access, startupDirty));

        if (!SisterAppLaunchSmokeOptions.TryParse(
                args,
                out var launchSmoke,
                out remaining,
                out error))
        {
            return ReportError(error);
        }
        if (launchSmoke is not null)
            return Run(remaining, false, access => LaunchSmokeCoordinator.Start(access, launchSmoke));

        return ReportError(
            $"Expected {SisterAppPackagingSmoke.Argument}, {SisterAppLaunchSmokeOptions.Argument}, " +
            $"{PhysicalValidationOptions.Argument}, {AccessibilityValidationOptions.Argument}, " +
            $"or {StartupDirtyTraceOptions.Argument}.");
    }

    private static int Run(
        IReadOnlyList<string> startupArguments,
        bool enableStartupDirtyTrace,
        Action<MainWindow.ValidationAccessAdapter> coordinator) =>
        FreeP.App.Avalonia.Program.RunToolHost(
            startupArguments,
            enableStartupDirtyTrace,
            coordinator);

    private static int ReportError(string? error)
    {
        Console.Error.WriteLine(error);
        return 2;
    }
}
