namespace FreeX.App.Services.Updates;

/// <summary>
/// Checks for, downloads, and applies application updates. Every method is best-effort:
/// network/feed failures resolve to <see cref="UpdateState.Unavailable"/> and never throw.
/// </summary>
public interface IUpdateService
{
    /// <summary>Check the feed and, if an update exists, download it. Returns the resulting state.</summary>
    Task<UpdateCheckResult> CheckAndDownloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply a previously downloaded update and restart the app. No-op if nothing is staged.
    /// On success this does not return (the process is replaced/restarted). On failure the apply
    /// step throws internally; that failure is caught, logged, and reported back as
    /// <see langword="false"/> instead of propagating, so this method itself never throws -- but
    /// callers MUST check the result and surface it, since a <see langword="false"/> return means
    /// the app is still running the old version.
    /// </summary>
    /// <returns><see langword="true"/> if the apply step did not fail (best-effort; a genuine
    /// success normally never returns at all because the process restarts). <see langword="false"/>
    /// if applying/restarting threw and the app remains on the old version.</returns>
    bool ApplyAndRestart();

    /// <summary>The releases page URL, used as a fallback when self-update is unavailable.</summary>
    string ReleasesPageUrl { get; }
}
