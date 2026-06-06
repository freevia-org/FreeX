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

        App.StartupArguments = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
