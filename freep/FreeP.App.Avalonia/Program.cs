using Avalonia;
using Avalonia.Fonts.Inter;
using FreeP.App.Compositor;
using Free.Shared.Shell.Avalonia;

namespace FreeP.App.Avalonia;

/// <summary>
/// FreeP cross-platform entry point. Installs FreeP's product identity into the shared tier
/// (so storage and diagnostics land under the FreeP folder), then runs the Avalonia shell.
/// Mirrors FreeW.App.Avalonia bootstrap; the WPF FreeP.App.Host stays Windows-only.
/// </summary>
internal static partial class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        SisterAvaloniaProgramRunner.Run(
            args,
            new SisterAvaloniaProgramSpec(
                FreePApplicationStartupDescriptor.ProductIdentity,
                PrepareLaunch,
                startupArguments => BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments)));

    private static SisterAvaloniaLaunchPreparation PrepareLaunch(string[] args)
    {
        App.StartupArguments = args;
        return SisterAvaloniaLaunchPreparation.Continue(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
