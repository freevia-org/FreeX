using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Free.Shared.AppServices;

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
/// Domain-neutral: lives in the shared tier so FreeX and FreeW route every external
/// URL launch through one scheme allowlist.
/// </summary>
public static class ExternalUriLauncher
{
    /// <summary>Schemes safe to hand to a platform launcher (mirrors the hyperlink allowlist).</summary>
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "mailto", "ftp", "file"
    };

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
            !AllowedSchemes.Contains(candidate.Scheme))
        {
            return false;
        }

        if (candidate.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase) &&
            (!candidate.IsFile ||
             !string.IsNullOrWhiteSpace(candidate.Host) &&
             !candidate.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
             !IsWellFormedLocalPath(candidate.LocalPath)))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    /// <summary>
    /// A file:// URI's decoded LocalPath can be syntactically valid Uri text (e.g. absurdly long
    /// enough to trip PathTooLongException) while still being a path Path.GetFullPath refuses to
    /// normalize. Re-validate it the same way HyperlinkNavigationPlanner.TryNormalizeExplicitLocalPath
    /// does before ever handing a file:// URI to a live shell-execute launcher, so a normalization
    /// mismatch between the two can never let a local-file target slip past the "local files are
    /// never shell-executed" guard: whatever shape makes the planner reclassify a hyperlink as
    /// External instead of LocalFile must also be rejected here, or the External branch in both
    /// shells hands it straight to Process.Start/ShellExecute.
    /// </summary>
    private static bool IsWellFormedLocalPath(string localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath) || localPath.Contains('\0', StringComparison.Ordinal))
            return false;

        try
        {
            return !string.IsNullOrWhiteSpace(Path.GetFullPath(localPath));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }
}
