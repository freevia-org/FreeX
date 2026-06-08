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

        var diagnostics = AvaloniaAppDiagnostics.Create(launchSmokeOptions?.DiagnosticsDirectory);
        diagnostics.RegisterUnhandledExceptionHandlers();
        diagnostics.RecordEvent("app_start", new Dictionary<string, string?>
        {
            ["source"] = "avalonia",
            ["scope"] = "app",
            ["status"] = "starting"
        });

        App.StartupArguments = startupArguments;
        App.LaunchSmokeOptions = launchSmokeOptions;
        App.Diagnostics = diagnostics;
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments);
            diagnostics.RecordEvent("app_exit", new Dictionary<string, string?>
            {
                ["source"] = "avalonia",
                ["scope"] = "app",
                ["status"] = "completed"
            });
            return 0;
        }
        catch (Exception ex)
        {
            diagnostics.RecordCrash(ex, "avalonia_startup");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
