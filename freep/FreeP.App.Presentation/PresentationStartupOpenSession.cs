using Free.Shared.AppServices;

namespace FreeP.App.Compositor;

public static class PresentationStartupOpenPlanner
{
    public static StartupFileOpenPlan Plan(
        IEnumerable<string> startupArguments,
        bool primaryWindowOccupied = false,
        Func<string, bool>? fileExists = null,
        CancellationToken cancellationToken = default) =>
        StartupFileOpenPlanner.Plan(
            startupArguments,
            new StartupFileOpenPolicy(
                PresentationFilePersistenceWorkflow.IsSupportedPresentationPath,
                primaryWindowOccupied),
            fileExists,
            cancellationToken);
}

/// <summary>
/// Owns startup-path execution through the same portable command session used by File/Open. Hosts
/// retain dispatch, sibling-window construction, and the timing of native feedback.
/// </summary>
public sealed class PresentationStartupOpenSession
{
    private readonly PresentationFileCommandSession _fileCommands;

    public PresentationStartupOpenSession(PresentationFileCommandSession fileCommands) =>
        _fileCommands = fileCommands ?? throw new ArgumentNullException(nameof(fileCommands));

    public StartupFileOpenPlan Plan(
        IEnumerable<string> startupArguments,
        bool primaryWindowOccupied = false,
        Func<string, bool>? fileExists = null,
        CancellationToken cancellationToken = default) =>
        PresentationStartupOpenPlanner.Plan(
            startupArguments,
            primaryWindowOccupied,
            fileExists,
            cancellationToken);

    public Task<PresentationFileCommandResult> OpenAsync(
        StartupFileOpenEntry entry,
        bool reportFeedback = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _fileCommands.OpenStartupPathAsync(entry.Path, reportFeedback, cancellationToken);
    }

    public Task<PresentationFileCommandResult?> ReportFirstUnopenableAsync(
        StartupFileOpenPlan plan,
        bool reportFeedback = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.ShouldReportMissingPath
            ? OpenUnopenablePathAsync(plan.FirstMissingPath!, reportFeedback, cancellationToken)
            : Task.FromResult<PresentationFileCommandResult?>(null);
    }

    public Task ReportFeedbackAsync(
        PresentationFileCommandResult result,
        CancellationToken cancellationToken = default) =>
        _fileCommands.ReportResultAsync(result, cancellationToken);

    private async Task<PresentationFileCommandResult?> OpenUnopenablePathAsync(
        string path,
        bool reportFeedback,
        CancellationToken cancellationToken) =>
        await _fileCommands.OpenStartupPathAsync(path, reportFeedback, cancellationToken);
}
