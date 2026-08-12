using FreeW.App.Avalonia;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;

namespace FreeW.Validation.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (PackagingSmoke.TryRun(args, Console.Out, Console.Error, out var packagingExit))
            return packagingExit;

        if (ReadAloudPauseSmoke.TryRun(args, Console.Out, Console.Error, out var speechExit))
            return speechExit;

        if (!SisterAppLaunchSmokeOptions.TryParse(
                args,
                out var launchOptions,
                out var launchStartupArguments,
                out var launchError))
        {
            Console.Error.WriteLine(launchError);
            return 2;
        }

        if (launchOptions is not null)
        {
            return FreeW.App.Avalonia.Program.RunToolHost(
                launchStartupArguments,
                access => LaunchSmokeCoordinator.Start(access, launchOptions));
        }

        if (!TablePropertiesX11ValidationOptions.TryParse(
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
            Console.Error.WriteLine(
                $"Expected {SisterAppPackagingSmoke.Argument}, {ReadAloudPauseSmoke.Argument}, " +
                $"{SisterAppLaunchSmokeOptions.Argument}, or {TablePropertiesX11ValidationOptions.Argument}.");
            return 2;
        }

        return FreeW.App.Avalonia.Program.RunToolHost(
            startupArguments,
            access => TablePropertiesX11ValidationCoordinator.Start(access, options));
    }
}
