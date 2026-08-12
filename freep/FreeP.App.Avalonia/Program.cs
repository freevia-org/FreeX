using Avalonia;
using Avalonia.Fonts.Inter;
using FreeP.App.Compositor;
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
        App.EnableStartupDirtyTrace = false;
        return SisterAvaloniaProgramRunner.Run(
            [],
            new SisterAvaloniaProgramSpec(
                FreePApplicationStartupDescriptor.ProductIdentity,
                startupArguments => SisterAvaloniaLaunchPreparation.Continue(startupArguments),
                startupArguments => BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments)));
    }

    internal static int RunToolHost(
        IReadOnlyList<string> startupArguments,
        bool enableStartupDirtyTrace,
        Action<MainWindow.ValidationAccessAdapter> coordinator)
    {
        ArgumentNullException.ThrowIfNull(startupArguments);
        ArgumentNullException.ThrowIfNull(coordinator);
        App.StartupArguments = startupArguments.ToArray();
        App.EnableStartupDirtyTrace = enableStartupDirtyTrace;
        App.ExternalStartupCoordinator = window => coordinator(window.CreateValidationAccessAdapter());
        return SisterAvaloniaProgramRunner.Run(
            [],
            new SisterAvaloniaProgramSpec(
                FreePApplicationStartupDescriptor.ProductIdentity,
                arguments => SisterAvaloniaLaunchPreparation.Continue(arguments),
                arguments => BuildAvaloniaApp().StartWithClassicDesktopLifetime(arguments)));
    }

    private static SisterAvaloniaLaunchPreparation PrepareLaunch(string[] args)
    {
        App.StartupArguments = args;
        App.EnableStartupDirtyTrace = false;
        return SisterAvaloniaLaunchPreparation.Continue(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
