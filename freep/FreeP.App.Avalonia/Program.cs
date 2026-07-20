using Avalonia;
using Avalonia.Fonts.Inter;
using FreeP.App.Avalonia.Smoke;

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

        if (AvaloniaDialogPaneVisualEvidenceCapture.TryParse(args, out var evidenceOutput, out var evidenceError))
        {
            if (evidenceError is not null)
            {
                Console.Error.WriteLine(evidenceError);
                return 2;
            }

            App.DialogPaneVisualEvidenceOutputRoot = evidenceOutput;
            args = [];
        }

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
        App.LaunchSmokeOptions = launchSmoke;

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
