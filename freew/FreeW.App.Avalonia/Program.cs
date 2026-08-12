using Avalonia;
using Avalonia.Fonts.Inter;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Avalonia;

/// <summary>
/// FreeW (the Word-like sibling of FreeX) cross-platform entry point. Installs FreeW's product
/// identity into the shared tier (so storage/diagnostics land under the FreeW folder, not FreeX),
/// then runs the Avalonia shell. Validation commands live in the external FreeW validation host;
/// the WPF FreeW.App.Host stays Windows-only.
/// </summary>
internal static partial class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        SisterAvaloniaProgramRunner.Run(
            args,
            new SisterAvaloniaProgramSpec(
                FreeWApplicationStartup.ProductIdentity,
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
