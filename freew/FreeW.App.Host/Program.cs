using System.Windows;

namespace FreeW.App.Host;

/// <summary>
/// FreeW entry point. Installs FreeW's own product identity and shell strings into the shared
/// tier (so storage/diagnostics land in %LOCALAPPDATA%\FreeW, not FreeX), loads FreeW's persisted
/// settings, wires local file-store diagnostics (no remote telemetry), then shows the window.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Same contract FreeX uses — set identity before any shared storage path is resolved, so
        // settings/recent-files/autosave/diagnostics land under %APPDATA%\FreeW (not the neutral default).
        AppProduct.Current = new AppProductIdentity("FreeW", "FREEW_DIAGNOSTICS", "FreeW");
        ShellStrings.Current = new DefaultShellStrings();

        // Load FreeW's persisted options via the shared JsonSettingsStore (best-effort; a missing or
        // corrupt settings file degrades to defaults). Read once at startup; the recent-files cap flows
        // into the file commands. Must follow the AppProduct install so the path resolves under FreeW.
        var optionsStore = FreeWOptionsStore.Create();
        var options = optionsStore.Load();

        // Local diagnostics, backed by the shared file store under %LOCALAPPDATA%\FreeW\Diagnostics.
        // FreeX-style local-only wiring (no Sentry). Best-effort throughout.
        var diagnostics = LocalAppDiagnostics.CreateDefault(EntryAssemblyVersion.Resolve());

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };

        // Application now exists, so the dispatcher hook can attach; record startup before the window shows.
        diagnostics.RegisterCrashHandlers(
            handler => app.DispatcherUnhandledException += (_, args) => handler(args.Exception));
        diagnostics.RecordEvent("app_start");

        app.Run(new MainWindow(options, optionsStore));

        diagnostics.RecordEvent("app_exit");
    }
}
