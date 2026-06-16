namespace FreeX.App.Services.Updates;

/// <summary>
/// Resolves the GitHub Releases feed and channel policy for self-update.
/// Pure/static so it is unit-testable without touching the network or Velopack.
/// </summary>
public static class UpdateFeed
{
    public const string GitHubRepoUrl = "https://github.com/tony-xmelon/FreeX";

    /// <summary>
    /// The tester channel pulls GitHub pre-releases; stable (or unknown) channels do not.
    /// Channel comes from release/progress.json's "channel" field, threaded in by the host.
    /// </summary>
    public static bool AllowPrereleases(string? channel) =>
        string.Equals(channel, "test", StringComparison.OrdinalIgnoreCase);
}
