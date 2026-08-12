using Avalonia;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

// Compiled into isolated Avalonia tool-host renderer variants only.
internal static partial class Program
{
    internal static int RunToolHost(Action<MainWindow> coordinator) =>
        RunToolHostCore([], coordinator);

    internal static int RunToolHostCore(
        IReadOnlyList<string> startupArguments,
        Action<MainWindow> coordinator)
    {
        App.ConfigureToolHost(startupArguments, coordinator);
        return SisterAvaloniaProgramRunner.Run(
            [],
            new SisterAvaloniaProgramSpec(
                FreePApplicationStartupDescriptor.ProductIdentity,
                arguments => SisterAvaloniaLaunchPreparation.Continue(arguments),
                arguments => BuildAvaloniaApp().StartWithClassicDesktopLifetime(arguments)));
    }
}
