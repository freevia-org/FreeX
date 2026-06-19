using System.Diagnostics;
using Free.Shared.AppServices.Updates;
using Velopack;
using Free.Shared.AppServices.Windows;
using FreeX.App.Services.FileAssociations;

namespace FreeX.App.Host;

/// <summary>
/// Configures the Velopack app with FreeX's install/update/uninstall hooks. The caller
/// (<see cref="Program.Main"/>) invokes <c>.Run()</c> on the returned builder as the very first
/// thing it does, so Velopack can service hook invocations and exit before any WPF initializes.
/// Install/update callbacks (re)register Windows file associations; uninstall removes them.
/// </summary>
public static class VelopackBootstrap
{
    /// <summary>
    /// Build the configured Velopack app. The caller must call <c>.Run()</c> on the result
    /// (kept at the entry point so Velopack's entry-point detection recognizes it).
    /// </summary>
    public static VelopackApp Configure()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName
                      ?? Environment.ProcessPath
                      ?? AppContext.BaseDirectory;
        var assoc = new WindowsFileAssociationService(FreeXFileAssociations.All);

        // The hook-building mechanics live in the shared tier (VelopackBootstrapRunner); FreeX
        // supplies only its own install/update/uninstall callbacks.
        return VelopackBootstrapRunner.Configure(new VelopackHookConfig(
            OnAfterInstall: () => assoc.RegisterAll(exePath),
            OnAfterUpdate: () => assoc.RegisterAll(exePath), // keep command path current after update
            OnBeforeUninstall: assoc.UnregisterAll));
    }
}
