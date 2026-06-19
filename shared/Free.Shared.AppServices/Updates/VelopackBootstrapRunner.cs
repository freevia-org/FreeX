using System.Runtime.Versioning;
using Velopack;

namespace Free.Shared.AppServices.Updates;

/// <summary>
/// App-neutral configuration for the pre-UI Velopack hook. An app supplies callbacks that run when
/// the installer/updater invokes the executable with install / update / uninstall hook arguments —
/// typically (re)registering or removing OS file associations. All callbacks are optional.
/// </summary>
/// <param name="OnAfterInstall">Runs once after a fresh install (e.g. register file associations).</param>
/// <param name="OnAfterUpdate">Runs after each update (e.g. refresh the registered command path).</param>
/// <param name="OnBeforeUninstall">Runs before uninstall (e.g. remove file associations).</param>
public sealed record VelopackHookConfig(
    Action? OnAfterInstall = null,
    Action? OnAfterUpdate = null,
    Action? OnBeforeUninstall = null);

/// <summary>
/// App-neutral builder for the Velopack pre-UI hook. Any app's <c>Program.Main</c> configures the
/// hook here and calls <c>.Run()</c> on the returned <see cref="VelopackApp"/> as the very first
/// thing it does, so Velopack can service install/update/uninstall hook invocations and exit before
/// any UI framework initializes.
///
/// <para>
/// The <c>.Run()</c> call must stay at the app's real entry point (not inside this method) so
/// Velopack's entry-point detection recognizes it.
/// </para>
/// </summary>
public static class VelopackBootstrapRunner
{
    /// <summary>
    /// Build the configured <see cref="VelopackApp"/> from the app's hook callbacks. The caller must
    /// invoke <c>.Run()</c> on the result at its entry point.
    /// </summary>
    /// <remarks>
    /// The Velopack install/update/uninstall fast-callbacks are Windows-only (they service the
    /// Windows installer/updater), so the builder is annotated for Windows. The update-check
    /// orchestration itself (<see cref="VelopackUpdateOrchestrator"/>) stays platform-neutral.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static VelopackApp Configure(VelopackHookConfig config)
    {
        var app = VelopackApp.Build();

        if (config.OnAfterInstall is { } afterInstall)
            app = app.OnAfterInstallFastCallback(_ => afterInstall());
        if (config.OnAfterUpdate is { } afterUpdate)
            app = app.OnAfterUpdateFastCallback(_ => afterUpdate());
        if (config.OnBeforeUninstall is { } beforeUninstall)
            app = app.OnBeforeUninstallFastCallback(_ => beforeUninstall());

        return app;
    }
}
