using Avalonia;
using Avalonia.Fonts.Inter;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (PackagingSmokeCommand.TryRun(args, Console.Out, Console.Error, out var smokeExitCode))
            return smokeExitCode;

        if (!MacOsLaunchSmokeOptions.TryParse(
                args,
                out var launchSmokeOptions,
                out var startupArguments,
                out var launchSmokeError))
        {
            Console.Error.WriteLine(launchSmokeError);
            return 1;
        }

        App.StartupArguments = startupArguments;
        App.LaunchSmokeOptions = launchSmokeOptions;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
