namespace Free.Shared.AppServices;

public sealed record StartupFileOpenEntry(string Path, bool OpenInNewWindow);

public sealed record StartupFileOpenPlan(
    IReadOnlyList<StartupFileOpenEntry> Entries,
    string? FirstMissingPath,
    bool ShouldPrewarm)
{
    public bool HasOpenableFiles => Entries.Count > 0;

    public bool ShouldReportMissingPath =>
        !HasOpenableFiles && FirstMissingPath is not null;
}

public sealed record StartupFileOpenPolicy(
    Func<string, bool> IsSupportedPath,
    bool PrimaryWindowOccupied = false,
    int? MaximumOpenableFiles = null)
{
    public static StartupFileOpenPolicy AllLocalFiles(bool primaryWindowOccupied = false) =>
        new(_ => true, primaryWindowOccupied);
}

/// <summary>
/// Plans local document arguments while products retain supported-format and recovery policy and
/// renderers retain window creation, dispatch, and feedback.
/// </summary>
public static class StartupFileOpenPlanner
{
    public static StartupFileOpenPlan Plan(
        IEnumerable<string> startupArguments,
        bool recoveryAccepted,
        Func<string, bool>? fileExists = null,
        CancellationToken cancellationToken = default) =>
        Plan(
            startupArguments,
            StartupFileOpenPolicy.AllLocalFiles(recoveryAccepted),
            fileExists,
            cancellationToken);

    public static StartupFileOpenPlan Plan(
        IEnumerable<string> startupArguments,
        StartupFileOpenPolicy policy,
        Func<string, bool>? fileExists = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startupArguments);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(policy.IsSupportedPath);
        if (policy.MaximumOpenableFiles is < 1)
            throw new ArgumentOutOfRangeException(nameof(policy), "The maximum must be positive when specified.");

        cancellationToken.ThrowIfCancellationRequested();
        fileExists ??= File.Exists;

        var entries = new List<StartupFileOpenEntry>();
        var seenPaths = new HashSet<string>(PlatformPathIdentityComparer.Current);
        string? firstMissingPath = null;

        foreach (var argument in startupArguments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!LocalFilePath.TryNormalize(argument, out var normalizedPath) ||
                !policy.IsSupportedPath(normalizedPath) ||
                !fileExists(normalizedPath))
            {
                firstMissingPath ??= argument;
                continue;
            }

            // A path repeated in the startup arguments (e.g. multi-selecting the same file and
            // dragging it onto the taskbar icon, which Windows delivers as one launch with the
            // path duplicated in argv) must resolve to a single window, not one window per
            // occurrence -- otherwise two windows independently edit the same file with no
            // "already open elsewhere" warning, and whichever saves last silently clobbers the
            // other's changes on disk.
            if (!seenPaths.Add(normalizedPath))
                continue;

            entries.Add(new StartupFileOpenEntry(
                normalizedPath,
                policy.PrimaryWindowOccupied || entries.Count > 0));

            if (entries.Count == policy.MaximumOpenableFiles)
                break;
        }

        return new StartupFileOpenPlan(
            entries.ToArray(),
            firstMissingPath,
            ShouldPrewarm: entries.Count == 0 && !policy.PrimaryWindowOccupied);
    }
}
