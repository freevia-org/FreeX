using System.Diagnostics;

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
/// Single guarded entry point for opening external URLs via the shell. Every shell
/// launch of a URL goes through here so the <see cref="HyperlinkNavigationPlanner.IsAllowedScheme"/>
/// allowlist (http/https/mailto/ftp) cannot be bypassed by a new call site.
/// </summary>
public static class ExternalUrlLauncher
{
    public static ExternalUrlLaunchResult Open(string url) =>
        Open(url, target => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }));

    public static ExternalUrlLaunchResult Open(string url, Action<string> launch)
    {
        if (string.IsNullOrWhiteSpace(url) || !HyperlinkNavigationPlanner.IsAllowedScheme(url))
            return ExternalUrlLaunchResult.BlockedScheme;

        try
        {
            launch(url);
            return ExternalUrlLaunchResult.Launched;
        }
        catch (Exception)
        {
            return ExternalUrlLaunchResult.LaunchFailed;
        }
    }
}
