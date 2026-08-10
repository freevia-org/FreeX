using Free.Shared.AppServices;

namespace FreeX.App.Services;

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

/// <summary>
/// Plans process-start workbook arguments after startup recovery has completed. Hosts retain native
/// window creation and dispatch; this policy owns path normalization, missing arguments, and whether
/// each existing workbook can reuse the primary window without replacing recovered content.
/// </summary>
public static class StartupFileOpenPlanner
{
    public static StartupFileOpenPlan Plan(
        IEnumerable<string> startupArguments,
        bool recoveryAccepted,
        Func<string, bool>? fileExists = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startupArguments);
        cancellationToken.ThrowIfCancellationRequested();
        fileExists ??= File.Exists;

        var entries = new List<StartupFileOpenEntry>();
        string? firstMissingPath = null;
        var isFirstOpenableFile = true;

        foreach (var argument in startupArguments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!LocalFilePath.TryNormalize(argument, out var normalizedPath) ||
                !fileExists(normalizedPath))
            {
                firstMissingPath ??= argument;
                continue;
            }

            entries.Add(new StartupFileOpenEntry(
                normalizedPath,
                recoveryAccepted || !isFirstOpenableFile));
            isFirstOpenableFile = false;
        }

        return new StartupFileOpenPlan(
            entries.ToArray(),
            firstMissingPath,
            ShouldPrewarm: entries.Count == 0 && !recoveryAccepted);
    }
}
