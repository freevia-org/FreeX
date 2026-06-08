using System.Diagnostics.CodeAnalysis;

namespace FreeX.App.Services;

/// <summary>Outcome of attempting to open an external URI through a platform launcher.</summary>
public enum ExternalUriLaunchResult
{
    /// <summary>The URI passed validation and was accepted by the platform launcher.</summary>
    Launched,

    /// <summary>The URI was rejected because its scheme is not on the hyperlink allowlist.</summary>
    BlockedScheme,

    /// <summary>No platform launcher is available for this app surface.</summary>
    LauncherUnavailable,

    /// <summary>The URI was allowed, but the platform launcher rejected it or threw.</summary>
    LaunchFailed
}

/// <summary>
/// Shared guard for opening external URIs. Platform hosts provide the actual
/// launch delegate so macOS can use Avalonia's launcher while WPF keeps shell execution.
/// </summary>
public static class ExternalUriLauncher
{
    public static ExternalUriLaunchResult Open(string target, Action<Uri>? launch)
    {
        if (!TryCreateAllowedUri(target, out var uri))
            return ExternalUriLaunchResult.BlockedScheme;

        if (launch is null)
            return ExternalUriLaunchResult.LauncherUnavailable;

        try
        {
            launch(uri);
            return ExternalUriLaunchResult.Launched;
        }
        catch (Exception)
        {
            return ExternalUriLaunchResult.LaunchFailed;
        }
    }

    public static async Task<ExternalUriLaunchResult> OpenAsync(
        string target,
        Func<Uri, Task<bool>>? launchAsync)
    {
        if (!TryCreateAllowedUri(target, out var uri))
            return ExternalUriLaunchResult.BlockedScheme;

        if (launchAsync is null)
            return ExternalUriLaunchResult.LauncherUnavailable;

        try
        {
            return await launchAsync(uri)
                ? ExternalUriLaunchResult.Launched
                : ExternalUriLaunchResult.LaunchFailed;
        }
        catch (Exception)
        {
            return ExternalUriLaunchResult.LaunchFailed;
        }
    }

    public static bool TryCreateAllowedUri(string target, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(target))
            return false;

        var normalizedTarget = target.Trim();
        if (!Uri.TryCreate(normalizedTarget, UriKind.Absolute, out var candidate) ||
            !HyperlinkNavigationPlanner.IsAllowedScheme(normalizedTarget))
        {
            return false;
        }

        uri = candidate;
        return true;
    }
}
