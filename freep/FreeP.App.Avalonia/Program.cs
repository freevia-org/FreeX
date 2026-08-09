using Avalonia;
using Avalonia.Fonts.Inter;
using FreeP.App.Avalonia.Smoke;
using Free.Shared.AppServices;

namespace FreeP.App.Avalonia;

/// <summary>
/// FreeP cross-platform entry point. Installs FreeP's product identity into the shared tier
/// (so storage/diagnostics land under the FreeP folder), services the headless smoke commands
/// used by the Linux CI lane, then runs the Avalonia shell.
/// Mirrors FreeW.App.Avalonia bootstrap; the WPF FreeP.App.Host stays Windows-only.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Set identity before any shared storage path is resolved.
        AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");

        if (AvaloniaWholeWindowVisualEvidenceCapture.TryParse(args, out var wholeWindowOutput, out var wholeWindowScenario, out var wholeWindowError))
        {
            if (wholeWindowError is not null)
            {
                Console.Error.WriteLine(wholeWindowError);
                return 2;
            }

            App.WholeWindowVisualEvidenceOutputRoot = wholeWindowOutput;
            App.WholeWindowVisualEvidenceScenarioId = wholeWindowScenario;
            args = [];
        }

        if (AvaloniaDialogPaneVisualEvidenceCapture.TryParse(args, out var evidenceOutput, out var evidenceScenario, out var evidenceError))
        {
            if (evidenceError is not null)
            {
                Console.Error.WriteLine(evidenceError);
                return 2;
            }

            App.DialogPaneVisualEvidenceOutputRoot = evidenceOutput;
            App.DialogPaneVisualEvidenceScenarioId = evidenceScenario;
            args = [];
        }

        if (!PhysicalValidationOptions.TryParse(
                args,
                out var physicalValidationOptions,
                out var physicalStartupArguments,
                out var physicalValidationError))
        {
            Console.Error.WriteLine(physicalValidationError);
            return 2;
        }
        args = physicalStartupArguments;

        if (!AccessibilityValidationOptions.TryParse(
                args,
                out var accessibilityValidationOptions,
                out var accessibilityStartupArguments,
                out var accessibilityValidationError))
        {
            Console.Error.WriteLine(accessibilityValidationError);
            return 2;
        }
        args = accessibilityStartupArguments;

        if (!StartupDirtyTraceOptions.TryParse(
                args,
                out var startupDirtyTraceOptions,
                out var startupDirtyTraceArguments,
                out var startupDirtyTraceError))
        {
            Console.Error.WriteLine(startupDirtyTraceError);
            return 2;
        }
        args = startupDirtyTraceArguments;

        // Headless engine smoke (no display): exercise the model + .pptx round-trip and exit.
        if (PackagingSmoke.TryRun(args, Console.Out, Console.Error, out var packagingExit))
            return packagingExit;

        // Parse the platform-neutral --launch-smoke contract (shared with the FreeX/FreeW Linux lanes).
        if (!LaunchSmokeOptions.TryParse(args, out var launchSmoke, out var startupArguments, out var error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        App.StartupArguments = startupArguments;
        App.StartupDirtyTraceOptions = startupDirtyTraceOptions;
        App.PhysicalValidationOptions = physicalValidationOptions;
        App.AccessibilityValidationOptions = accessibilityValidationOptions;
        App.LaunchSmokeOptions = launchSmoke;

        // Crash capture, mirroring the FreeX Avalonia shell. Without this the Linux/macOS build was
        // completely blind: no crash report, no breadcrumbs, nothing — the only crashes we have ever
        // recovered from a real machine were Avalonia startup faults exactly like the ones this
        // catches, and in FreeP they would have vanished silently. Registered before the shell runs
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
