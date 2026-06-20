using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace Free.Shared.AppServices.Updates;

/// <summary>A staged update awaiting restart, identified by its target version string.</summary>
public sealed record VelopackStagedUpdate(string Version);

/// <summary>
/// App-neutral Velopack self-update orchestration: builds an <see cref="UpdateManager"/> for a
/// GitHub Releases feed and exposes the check/download and apply/restart flow as plain delegates,
/// parameterized by repo URL + channel (prerelease) policy. The decision/reporting layer lives in
/// the host's <c>IUpdateService</c>; this type owns only the Velopack mechanics so any app
/// (FreeX, FreeP, FreeW) reuses identical update behavior by supplying config.
///
/// <para>
/// When no manager is available (e.g. an unpacked dev build that is not Velopack-installed) the
/// probe yields <c>null</c> and apply is a no-op, so callers degrade to "up to date / unavailable"
/// without throwing.
/// </para>
/// </summary>
public sealed class VelopackUpdateOrchestrator
{
    private readonly UpdateManager? _manager;
    private readonly ILogger? _logger;

    private VelopackUpdateOrchestrator(UpdateManager? manager, ILogger? logger)
    {
        _manager = manager;
        _logger = logger;
    }

    /// <summary>
    /// Build an orchestrator pointed at a GitHub Releases feed. If the manager cannot be created
    /// (offline metadata, packaging mismatch), the orchestrator is still returned but inert.
    /// </summary>
    /// <param name="repoUrl">The GitHub repository URL hosting the Velopack releases.</param>
    /// <param name="prerelease">When true, pre-releases are eligible (e.g. a tester channel).</param>
    /// <param name="logger">Optional diagnostics sink; failures are logged, never thrown.</param>
    public static VelopackUpdateOrchestrator ForGitHub(string repoUrl, bool prerelease, ILogger? logger = null)
    {
        UpdateManager? manager;
        try
        {
            manager = new UpdateManager(new GithubSource(repoUrl, accessToken: null, prerelease: prerelease));
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Velopack UpdateManager unavailable; self-update disabled.");
            manager = null;
        }

        return new VelopackUpdateOrchestrator(manager, logger);
    }

    /// <summary>
    /// Check the feed and, if a newer release exists, download/stage it. Returns the staged update
    /// (with its target version) or <c>null</c> when nothing is available or the app is not
    /// Velopack-installed.
    /// </summary>
    public async Task<VelopackStagedUpdate?> CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        if (_manager is null || !_manager.IsInstalled)
            return null;

        var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (info is null)
            return null;

        await _manager.DownloadUpdatesAsync(info, progress: null, cancellationToken).ConfigureAwait(false);
        return new VelopackStagedUpdate(info.TargetFullRelease.Version.ToString());
    }

    /// <summary>
    /// Apply the currently available update and restart the process. No-op when nothing is staged
    /// or the app is not Velopack-installed. On success this does not return (the process restarts).
    /// </summary>
    public void ApplyAndRestart()
    {
        if (_manager is null || !_manager.IsInstalled)
            return;

        var info = _manager.CheckForUpdates();
        if (info is not null)
            _manager.ApplyUpdatesAndRestart(info.TargetFullRelease, restartArgs: null);
    }
}
