using Avalonia;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Avalonia;

internal static partial class Program
{
    internal static int RunToolHost(
        IReadOnlyList<string> startupArguments,
        Action<MainWindow.ValidationAccessAdapter> coordinator)
    {
        ArgumentNullException.ThrowIfNull(startupArguments);
        ArgumentNullException.ThrowIfNull(coordinator);
        App.StartupArguments = startupArguments.ToArray();
        App.ExternalStartupCoordinator = window => coordinator(window.CreateValidationAccessAdapter());
        return SisterAvaloniaProgramRunner.Run(
            [],
            new SisterAvaloniaProgramSpec(
                FreeWApplicationStartup.ProductIdentity,
                arguments => SisterAvaloniaLaunchPreparation.Continue(arguments),
                arguments => BuildAvaloniaApp().StartWithClassicDesktopLifetime(arguments)));
    }
}
