using Avalonia;
using Avalonia.Fonts.Inter;
using FreeW.App.Avalonia.Smoke;
using Free.Shared.AppServices;

namespace FreeW.App.Avalonia;

/// <summary>
/// FreeW (the Word-like sibling of FreeX) cross-platform entry point. Installs FreeW's product
/// identity into the shared tier (so storage/diagnostics land under the FreeW folder, not FreeX),
/// services the headless smoke commands used by the Linux CI lane, then runs the Avalonia shell.
/// Mirrors the FreeX.App.Avalonia bootstrap; the WPF FreeW.App.Host stays Windows-only.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Same contract FreeX uses: set identity before any shared storage path is resolved.
        AppProduct.Current = new AppProductIdentity("FreeW", "FREEW_DIAGNOSTICS", "FreeW");

        // Headless engine smoke (no display): exercise the model + DOCX round-trip and exit.
        if (PackagingSmoke.TryRun(args, Console.Out, Console.Error, out var packagingExit))
            return packagingExit;

        if (ReadAloudPauseSmoke.TryRun(args, Console.Out, Console.Error, out var readAloudPauseExit))
            return readAloudPauseExit;

        // Parse the platform-neutral --launch-smoke contract (shared with the FreeX Linux lane).
        if (!LaunchSmokeOptions.TryParse(args, out var launchSmoke, out var startupArguments, out var error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        App.StartupArguments = startupArguments;
        App.LaunchSmokeOptions = launchSmoke;

        // Crash capture, mirroring the FreeX Avalonia shell. Without this the Linux/macOS build was
        // completely blind: no crash report, no breadcrumbs, nothing — the only crashes we have ever
        // recovered from a real machine were Avalonia startup faults exactly like the ones this
        // catches, and in FreeW they would have vanished silently. Registered before the shell runs
        // so a fault during window construction is still recorded.
        var diagnostics = LocalAppDiagnostics.CreateDefault(EntryAssemblyVersion.Resolve());
        diagnostics.RegisterCrashHandlers();

        // Ribbon/menu command faults are caught by the shared Avalonia renderers rather than being
        // allowed to escape a click handler and kill the process; record them here.
        Free.Shared.Ribbon.RibbonCommandFaultReporter.Handler =
            (exception, commandId) => diagnostics.RecordCrash(exception, "ribbon_command:" + commandId);

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments);
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
