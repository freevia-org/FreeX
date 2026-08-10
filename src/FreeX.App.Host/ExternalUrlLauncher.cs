namespace FreeX.App.Host;

/// <summary>Outcome of attempting to open an external URL through the shell.</summary>
public enum ExternalUrlLaunchResult
{
    /// <summary>The URL passed the scheme allowlist and was handed to the shell.</summary>
    Launched,

    /// <summary>The URL was rejected because its scheme is not on the allowlist.</summary>
    BlockedScheme,

    /// <summary>The URL was allowed but the shell launch threw.</summary>
    LaunchFailed
}

/// <summary>
/// Single guarded entry point for opening external URLs via the shell. Every URL
/// launch goes through the shared service allowlist so guarded schemes cannot be
/// bypassed by a new call site.
/// </summary>
public static class ExternalUrlLauncher
{
    public static ExternalUrlLaunchResult Open(string url) =>
        DesktopExternalUriLauncher.Open(url) switch
        {
            ExternalUriLaunchResult.Launched => ExternalUrlLaunchResult.Launched,
            ExternalUriLaunchResult.BlockedScheme => ExternalUrlLaunchResult.BlockedScheme,
            _ => ExternalUrlLaunchResult.LaunchFailed
        };

    public static ExternalUrlLaunchResult Open(string url, Action<string> launch) =>
        ExternalUriLauncher.Open(url, uri => launch(uri.AbsoluteUri)) switch
        {
            ExternalUriLaunchResult.Launched => ExternalUrlLaunchResult.Launched,
            ExternalUriLaunchResult.BlockedScheme => ExternalUrlLaunchResult.BlockedScheme,
            _ => ExternalUrlLaunchResult.LaunchFailed
        };
}
