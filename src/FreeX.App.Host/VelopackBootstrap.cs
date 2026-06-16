using System.Diagnostics;
using Velopack;
using FreeX.App.Host.FileAssociations;

namespace FreeX.App.Host;

/// <summary>
/// Velopack entry hook. <see cref="Run"/> MUST be called before any WPF/UI work so Velopack
/// can service install/update/uninstall invocations and exit fast. Install/update callbacks
/// (re)register Windows file associations; the uninstall callback removes them.
/// </summary>
public static class VelopackBootstrap
{
    public static void Run()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName
                      ?? Environment.ProcessPath
                      ?? AppContext.BaseDirectory;
        var assoc = new WindowsFileAssociationService();

        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => assoc.RegisterAll(exePath))
            .OnAfterUpdateFastCallback(_ => assoc.RegisterAll(exePath)) // keep command path current after update
            .OnBeforeUninstallFastCallback(_ => assoc.UnregisterAll())
            .Run();
    }
}
