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
    /// On success this does not return (the process is replaced/restarted).
    /// </summary>
    void ApplyAndRestart();

    /// <summary>The releases page URL, used as a fallback when self-update is unavailable.</summary>
    string ReleasesPageUrl { get; }
}
