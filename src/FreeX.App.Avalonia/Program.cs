using Avalonia;
using Avalonia.Fonts.Inter;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

internal static partial class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack must run before any other startup work so it can service install/update/
        // uninstall hook invocations and exit fast. macOS associations are declared in
        // Info.plist, so no install hooks are wired here.
        Velopack.VelopackApp.Build().Run();

        return RunApplication(args, diagnosticsDirectory: null, externalStartupCoordinator: null);
    }

    internal static int RunPivotRuntimeObservationHost(
        IReadOnlyList<string> startupArguments,
        Action<MainWindow.PivotRuntimeObservationAccessAdapter> externalStartupCoordinator) =>
        RunApplication(
            startupArguments.ToArray(),
            diagnosticsDirectory: null,
            (window, _) => externalStartupCoordinator(window.CreatePivotRuntimeObservationAccessAdapter()));

    private static int RunApplication(
        string[] startupArguments,
        string? diagnosticsDirectory,
        Action<MainWindow, LocalAppDiagnostics?>? externalStartupCoordinator)
    {
        LocalAppDiagnostics? diagnostics = null;
        return SisterAvaloniaProgramRunner.Run(
            startupArguments,
            new SisterAvaloniaProgramSpec(
                FreeXApplicationStartupDescriptor.ProductIdentity,
                SisterAvaloniaLaunchPreparation.Continue,
                arguments => BuildAvaloniaApp().StartWithClassicDesktopLifetime(arguments))
        {
            CreateDiagnostics = () =>
            {
                diagnostics = LocalAppDiagnostics.Create(
                    AppHelpInfo.GetVersionText(typeof(Program).Assembly),
                    diagnosticsDirectory,
                    remoteAnalyticsConsent: new FreeXOptionsRuntimeSession().LiveOptions.CrashAnalyticsEnabled);
                return new SisterAvaloniaProgramDiagnostics(
                    () => diagnostics.RegisterCrashHandlers(),
                    (exception, source) => diagnostics.RecordCrash(exception, source));
            },
            BeforeRun = () =>
            {
                var activeDiagnostics = diagnostics
                    ?? throw new InvalidOperationException("FreeX diagnostics were not initialized.");
                activeDiagnostics.RecordEvent("app_start", new Dictionary<string, string?>
                {
                    ["source"] = "avalonia",
                    ["scope"] = "app",
                    ["status"] = "starting"
                });

                App.StartupArguments = startupArguments;
                App.ExternalStartupCoordinator = externalStartupCoordinator;
                App.Diagnostics = activeDiagnostics;
            },
            AfterRun = _ =>
                (diagnostics ?? throw new InvalidOperationException("FreeX diagnostics were not initialized."))
                .RecordEvent("app_exit", new Dictionary<string, string?>
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
