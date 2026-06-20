using System.Windows;

namespace FreeP.App.Host;

/// <summary>
/// FreeP entry point. Installs FreeP's product identity into the shared tier (so
/// settings/recent-files/diagnostics land in %LOCALAPPDATA%\FreeP, not FreeX/FreeW), installs the shared
/// shell/backstage string + dialog-sizing seams (<see cref="AppComposition"/>), loads FreeP's persisted
/// options, wires local file-store diagnostics (no Sentry), then shows the window.
///
/// Mirrors FreeW.App.Host.Program — code-only startup, no App.xaml — so FreeP assembles itself from the same
/// shared parts the other sister apps use.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Set identity before any shared storage path is resolved, so settings/recent-files/diagnostics land
        // under %LOCALAPPDATA%\FreeP (not the neutral default). Same contract FreeX and FreeW use.
        AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");

        // TODO(velopack): if/when a shared Velopack bootstrap helper lands, call it here — before the WPF
        // Application is created — so install/update/uninstall hooks are serviced before any UI initializes
        // (the same ordering FreeX uses). The scaffold ships without self-update.

        // Install the shared shell seams (ShellStrings / BackstageStrings / DialogSizing / icon resolver).
        // Must follow the AppProduct install and precede any shared dialog/backstage use.
        AppComposition.InstallSharedSeams();

        // Load FreeP's persisted options via the shared JsonSettingsStore (best-effort; missing/corrupt
        // degrades to defaults). Must follow the AppProduct install so the path resolves under FreeP.
        var optionsStore = FreePOptionsStore.Create();
        var options = optionsStore.Load();

        // Local diagnostics, backed by the shared file store under %LOCALAPPDATA%\FreeP\Diagnostics
        // (FreeX-style local-only wiring, no Sentry). Best-effort throughout.
        var diagnostics = LocalAppDiagnostics.CreateDefault(EntryAssemblyVersion.Resolve());

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };

        diagnostics.RegisterCrashHandlers(
            handler => app.DispatcherUnhandledException += (_, args) => handler(args.Exception));
        diagnostics.RecordEvent("app_start");

        app.Run(new MainWindow(options, optionsStore));

        diagnostics.RecordEvent("app_exit");
    }
}
