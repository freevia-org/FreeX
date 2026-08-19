using Free.Shared.AppServices.Updates;
using Microsoft.Extensions.Logging;

namespace FreeX.App.Services.Updates;

/// <summary>A downloaded, staged update awaiting restart.</summary>
public sealed record DownloadedUpdate(string Version);

/// <summary>
/// Velopack-backed <see cref="IUpdateService"/>. The check/download work is injected as a
/// delegate (<paramref name="downloadProbe"/>) so the decision logic is unit-testable; the
/// production factory wires the delegate to a real <see cref="UpdateManager"/>.
/// When no manager is available (e.g. unpacked dev build) the service degrades to Unavailable
/// and callers fall back to opening <see cref="ReleasesPageUrl"/>.
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private readonly Func<CancellationToken, Task<DownloadedUpdate?>> _downloadProbe;
    private readonly Action? _applyAndRestart;
    private readonly ILogger? _logger;

    public string ReleasesPageUrl { get; }

    public VelopackUpdateService(
        string releasesPageUrl,
        Func<CancellationToken, Task<DownloadedUpdate?>> downloadProbe,
        Action? applyAndRestart = null,
        ILogger? logger = null)
    {
        ReleasesPageUrl = releasesPageUrl;
        _downloadProbe = downloadProbe;
        _applyAndRestart = applyAndRestart;
        _logger = logger;
    }

    /// <summary>
    /// Production factory: builds a service backed by the shared, app-neutral
    /// <see cref="VelopackUpdateOrchestrator"/> pointed at the GitHub repo. Returns a service whose
    /// probe yields null/Unavailable if the app is not Velopack-installed. The orchestration
    /// (UpdateManager creation, check/download/apply) lives in the shared tier so other apps reuse
    /// it; FreeX supplies only the feed/channel config and the releases-page fallback URL.
    /// </summary>
    public static VelopackUpdateService CreateForGitHub(string repoUrl, bool prerelease, string releasesPageUrl, ILogger? logger = null)
    {
        var orchestrator = VelopackUpdateOrchestrator.ForGitHub(repoUrl, prerelease, logger);

        async Task<DownloadedUpdate?> Probe(CancellationToken ct)
        {
            var staged = await orchestrator.CheckAndDownloadAsync(ct).ConfigureAwait(false);
            return staged is null ? null : new DownloadedUpdate(staged.Version);
        }

        return new VelopackUpdateService(releasesPageUrl, Probe, orchestrator.ApplyAndRestart, logger);
    }

    public async Task<UpdateCheckResult> CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var update = await _downloadProbe(cancellationToken).ConfigureAwait(false);
            return update is null
                ? UpdateCheckResult.UpToDate
                : new UpdateCheckResult(UpdateState.ReadyToApply, update.Version);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Update check failed; reporting Unavailable.");
            return UpdateCheckResult.Unavailable;
        }
    }

    public bool ApplyAndRestart()
    {
        try
        {
            _applyAndRestart?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            // A genuine success never reaches here: Velopack replaces/restarts the process before
            // this call returns. Reaching this catch means the app is still running the OLD
            // version, so this is an error the caller must surface to the user -- not a routine,
            // ignorable warning like the check/download best-effort failures above.
            _logger?.LogError(ex, "ApplyAndRestart failed; the app is still running the previous version.");
            return false;
        }
    }
}
