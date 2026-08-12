using Avalonia;
using Avalonia.Fonts.Inter;
using FreeP.App.Avalonia.Smoke;
using FreeP.App.Compositor;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;

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
    public static int Main(string[] args) =>
        SisterAvaloniaProgramRunner.Run(
            args,
            new SisterAvaloniaProgramSpec(
                FreePApplicationStartupDescriptor.ProductIdentity,
                PrepareLaunch,
                startupArguments => BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments)));

    internal static int RunToolHost(Action<MainWindow> coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        App.ExternalStartupCoordinator = coordinator;
        App.StartupArguments = [];
        return SisterAvaloniaProgramRunner.Run(
            [],
            new SisterAvaloniaProgramSpec(
                FreePApplicationStartupDescriptor.ProductIdentity,
                startupArguments => SisterAvaloniaLaunchPreparation.Continue(startupArguments),
                startupArguments => BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments)));
    }

    private static SisterAvaloniaLaunchPreparation PrepareLaunch(string[] args)
    {
        if (!PhysicalValidationOptions.TryParse(
                args,
                out var physicalValidationOptions,
                out var physicalStartupArguments,
                out var physicalValidationError))
        {
            Console.Error.WriteLine(physicalValidationError);
            return SisterAvaloniaLaunchPreparation.Exit(2);
        }
        args = physicalStartupArguments;

        if (!AccessibilityValidationOptions.TryParse(
                args,
                out var accessibilityValidationOptions,
                out var accessibilityStartupArguments,
                out var accessibilityValidationError))
        {
            Console.Error.WriteLine(accessibilityValidationError);
            return SisterAvaloniaLaunchPreparation.Exit(2);
        }
        args = accessibilityStartupArguments;

        if (!StartupDirtyTraceOptions.TryParse(
                args,
                out var startupDirtyTraceOptions,
                out var startupDirtyTraceArguments,
                out var startupDirtyTraceError))
        {
            Console.Error.WriteLine(startupDirtyTraceError);
            return SisterAvaloniaLaunchPreparation.Exit(2);
        }
        args = startupDirtyTraceArguments;

        // Headless engine smoke (no display): exercise the model + .pptx round-trip and exit.
        if (PackagingSmoke.TryRun(args, Console.Out, Console.Error, out var packagingExit))
            return SisterAvaloniaLaunchPreparation.Exit(packagingExit);

        // Parse the platform-neutral --launch-smoke contract (shared with the FreeX/FreeW Linux lanes).
        if (!LaunchSmokeOptions.TryParse(args, out var launchSmoke, out var startupArguments, out var error))
        {
            Console.Error.WriteLine(error);
            return SisterAvaloniaLaunchPreparation.Exit(1);
        }

        App.StartupArguments = startupArguments;
        App.StartupDirtyTraceOptions = startupDirtyTraceOptions;
        App.PhysicalValidationOptions = physicalValidationOptions;
        App.AccessibilityValidationOptions = accessibilityValidationOptions;
        App.LaunchSmokeOptions = launchSmoke;
        return SisterAvaloniaLaunchPreparation.Continue(startupArguments);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
