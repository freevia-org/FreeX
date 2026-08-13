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
        SisterAvaloniaStandardDesktopFactory.Run(args, App.DesktopProfile);
}
