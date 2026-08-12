using FreeX.App.Services;

namespace FreeX.App.Avalonia;

internal static partial class Program
{
    internal static int RunValidationToolHost(
        IReadOnlyList<string> startupArguments,
        string? diagnosticsDirectory,
        Action<MainWindow.RendererValidationAccess, LocalAppDiagnostics?> externalStartupCoordinator)
    {
        ArgumentNullException.ThrowIfNull(startupArguments);
        ArgumentNullException.ThrowIfNull(externalStartupCoordinator);
        return RunApplication(
            startupArguments.ToArray(),
            diagnosticsDirectory,
            (window, diagnostics) =>
                externalStartupCoordinator(window.CreateRendererValidationAccess(), diagnostics));
    }
}
