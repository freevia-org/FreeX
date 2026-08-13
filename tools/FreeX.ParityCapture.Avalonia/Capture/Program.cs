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
        Velopack.VelopackApp.Build().Run();

        var startupArguments = args;

        if (!ParityCaptureOptions.TryParse(
                startupArguments,
                out var parityCaptureOptions,
                out startupArguments,
                out var parityCaptureError))
        {
            Console.Error.WriteLine(parityCaptureError);
            return 1;
        }

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
            diagnosticsDirectory: null);
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
                    App.Diagnostics = diagnostics;
                    if (parityCaptureOptions is not null ||
                        gridCaptureOptions is not null ||
                        interactionValidationOptions is not null)
                    {
                        App.ExternalOptionsFixtureFactory = parityCaptureOptions is null
                            ? null
                            : OptionsDialogParityFixture.Create;
                        App.ExternalStartupSessionFactory = (factory, height, width, includeObjects) =>
                            ParityCaptureWorkbookSessionFactory.Create(
                                factory,
                                height,
                                width,
                                includeObjects);
                        App.ExternalStartupCoordinator = (mainWindow, appDiagnostics) =>
                        {
                            if (parityCaptureOptions is not null)
                                ParityCaptureCoordinator.Start(mainWindow, parityCaptureOptions, appDiagnostics);
                            if (gridCaptureOptions is not null)
                                GridCaptureCoordinator.Start(mainWindow, gridCaptureOptions, appDiagnostics);
                            if (interactionValidationOptions is not null)
                                InteractionValidationCoordinator.Start(mainWindow, interactionValidationOptions, appDiagnostics);
                        };
                    }
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
