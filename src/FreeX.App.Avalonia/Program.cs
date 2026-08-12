using Avalonia;
using Avalonia.Fonts.Inter;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack must run before any other startup work so it can service install/update/
        // uninstall hook invocations and exit fast. macOS associations are declared in
        // Info.plist, so no install hooks are wired here.
        Velopack.VelopackApp.Build().Run();

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

        // Additive headless surface-capture mode (--parity-capture <outDir>). Parsed out of the
        // launch-smoke-filtered arguments so it composes with the existing modes without disturbing them.
        if (!ParityCaptureOptions.TryParse(
                startupArguments,
                out var parityCaptureOptions,
                out startupArguments,
                out var parityCaptureError))
        {
            Console.Error.WriteLine(parityCaptureError);
            return 1;
        }

        // Additive headless grid-range capture mode (--parity-grid <xlsx> <range> <outDir>).
        if (!GridCaptureOptions.TryParse(
                startupArguments,
                out var gridCaptureOptions,
                out startupArguments,
                out var gridCaptureError))
        {
            Console.Error.WriteLine(gridCaptureError);
            return 1;
        }

        if (!InteractionValidationOptions.TryParse(
                startupArguments,
                out var interactionValidationOptions,
                out startupArguments,
                out var interactionValidationError))
        {
            Console.Error.WriteLine(interactionValidationError);
            return 1;
        }

        var diagnostics = LocalAppDiagnostics.Create(
            AppHelpInfo.GetVersionText(typeof(Program).Assembly),
            launchSmokeOptions?.DiagnosticsDirectory);
        return SisterAvaloniaApplicationStartupRunner.Run(
            startupArguments,
            new SisterAvaloniaApplicationStartupSpec(
                StartApplication: _ => BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments),
                RegisterUnhandledExceptionHandlers: () => diagnostics.RegisterCrashHandlers(),
                RecordCrash: (exception, source) => diagnostics.RecordCrash(exception, source))
        {
            BeforeRun = () =>
            {
                diagnostics.RecordEvent("app_start", new Dictionary<string, string?>
                {
                    ["source"] = "avalonia",
                    ["scope"] = "app",
                    ["status"] = "starting"
                });

                App.StartupArguments = startupArguments;
                App.LaunchSmokeOptions = launchSmokeOptions;
                App.ParityCaptureOptions = parityCaptureOptions;
                App.GridCaptureOptions = gridCaptureOptions;
                App.InteractionValidationOptions = interactionValidationOptions;
                App.Diagnostics = diagnostics;
            },
            AfterRun = _ => diagnostics.RecordEvent("app_exit", new Dictionary<string, string?>
            {
                ["source"] = "avalonia",
                ["scope"] = "app",
                ["status"] = "completed"
            }),
            CompletedExitCode = 0
        });
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
