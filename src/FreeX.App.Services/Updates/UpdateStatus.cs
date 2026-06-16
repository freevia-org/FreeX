namespace FreeX.App.Services.Updates;

/// <summary>Outcome of an update check.</summary>
public enum UpdateState
{
    /// <summary>No newer release available.</summary>
    UpToDate,
    /// <summary>A newer release is available (not yet downloaded).</summary>
    UpdateAvailable,
    /// <summary>A newer release has been downloaded and is staged to apply on restart.</summary>
    ReadyToApply,
    /// <summary>The check could not complete (offline, no Velopack manager, feed error).</summary>
    Unavailable,
}

/// <summary>Immutable result of a check, safe to marshal to the UI thread.</summary>
public sealed record UpdateCheckResult(UpdateState State, string? AvailableVersion)
{
    public static UpdateCheckResult UpToDate { get; } = new(UpdateState.UpToDate, null);
    public static UpdateCheckResult Unavailable { get; } = new(UpdateState.Unavailable, null);
}
