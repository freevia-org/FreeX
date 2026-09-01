using Free.Shared.AppServices.Updates;
using Velopack;

namespace FreeW.App.Host;

/// <summary>
/// Configures FreeW's pre-UI Velopack lifecycle. Product-specific install hooks can be added here
/// without moving the required <c>Run()</c> call away from <see cref="Program.Main"/>.
/// </summary>
public static class VelopackBootstrap
{
    public static VelopackApp Configure() =>
        VelopackBootstrapRunner.Configure(new VelopackHookConfig());
}
