using Avalonia;
using Avalonia.Fonts.Inter;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia.Smoke;
using FreeW.App.Presentation.Shell;

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
    public static int Main(string[] args) =>
        SisterAvaloniaProgramRunner.Run(
            args,
            new SisterAvaloniaProgramSpec(
                FreeWApplicationStartup.ProductIdentity,
                PrepareLaunch,
                startupArguments => BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments)));

    internal static int RunToolHost(
        IReadOnlyList<string> startupArguments,
        Action<MainWindow.ValidationAccessAdapter> coordinator)
    {
        ArgumentNullException.ThrowIfNull(startupArguments);
        ArgumentNullException.ThrowIfNull(coordinator);
        App.StartupArguments = startupArguments.ToArray();
        App.LaunchSmokeOptions = null;
        App.ExternalStartupCoordinator = window => coordinator(window.CreateValidationAccessAdapter());
        return SisterAvaloniaProgramRunner.Run(
            [],
            new SisterAvaloniaProgramSpec(
                FreeWApplicationStartup.ProductIdentity,
                arguments => SisterAvaloniaLaunchPreparation.Continue(arguments),
                arguments => BuildAvaloniaApp().StartWithClassicDesktopLifetime(arguments)));
    }

    private static SisterAvaloniaLaunchPreparation PrepareLaunch(string[] args)
    {
        // Headless engine smoke (no display): exercise the model + DOCX round-trip and exit.
        if (PackagingSmoke.TryRun(args, Console.Out, Console.Error, out var packagingExit))
            return SisterAvaloniaLaunchPreparation.Exit(packagingExit);

        if (ReadAloudPauseSmoke.TryRun(args, Console.Out, Console.Error, out var readAloudPauseExit))
            return SisterAvaloniaLaunchPreparation.Exit(readAloudPauseExit);

        // Parse the platform-neutral --launch-smoke contract (shared with the FreeX Linux lane).
        if (!LaunchSmokeOptions.TryParse(args, out var launchSmoke, out var startupArguments, out var error))
        {
            Console.Error.WriteLine(error);
            return SisterAvaloniaLaunchPreparation.Exit(1);
        }

        App.StartupArguments = startupArguments;
        App.LaunchSmokeOptions = launchSmoke;
        App.ExternalStartupCoordinator = null;
        return SisterAvaloniaLaunchPreparation.Continue(startupArguments);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
