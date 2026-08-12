using System.Text.Json;
using Free.Shared.AppServices;

namespace Free.ToolsShared;

public sealed class VisualEvidenceRunDirectory : IDisposable
{
    private readonly ToolTemporaryDirectory _directory;

    public VisualEvidenceRunDirectory(string prefix)
    {
        _directory = new ToolTemporaryDirectory(prefix);
    }

    public string Path => _directory.Path;

    public void Dispose() => _directory.Dispose();
}

public sealed record VisualEvidenceHostOutputPlan(
    string HostDirectory,
    string ManifestPath,
    string? ProgressPath,
    string? FullDirectory,
    string? ClientDirectory,
    string? TargetsDirectory)
{
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(HostDirectory);
        if (FullDirectory is not null)
            Directory.CreateDirectory(FullDirectory);
        if (ClientDirectory is not null)
            Directory.CreateDirectory(ClientDirectory);
        if (TargetsDirectory is not null)
            Directory.CreateDirectory(TargetsDirectory);
    }
}

public sealed record VisualEvidenceProcessPlan(
    string Executable,
    string WorkingDirectory,
    string Arguments,
    TimeSpan Timeout,
    string TimedOutProcessTreeDescription)
{
    public int TimeoutMilliseconds => checked((int)Timeout.TotalMilliseconds);

    public static VisualEvidenceProcessPlan Create(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        string timedOutProcessTreeDescription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(timedOutProcessTreeDescription);
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        return new(
            executable,
            workingDirectory,
            string.Join(' ', arguments.Select(Quote)),
            timeout,
            timedOutProcessTreeDescription);
    }

    private static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
    }
}

public sealed record VisualEvidenceScenarioRun<TScenario, TCapture>(
    IReadOnlyList<TScenario> Scenarios,
    IReadOnlyList<TCapture> Captures,
    IReadOnlyList<string> Limitations)
    where TCapture : class;

public static class VisualEvidenceCaptureOrchestrator
{
    public static async Task<VisualEvidenceScenarioRun<TScenario, TCapture>> RunScenariosAsync<TScenario, TCapture>(
        IReadOnlyList<TScenario> catalog,
        string? scenarioId,
        Func<TScenario, string> idSelector,
        VisualEvidenceHostOutputPlan outputPlan,
        bool logProgress,
        Func<TScenario, Task<TCapture>> captureScenario,
        Func<TScenario, Exception, TCapture?> createBlockedCapture,
        Func<TScenario, Exception, string?> createLimitation,
        Action<TScenario, Exception>? reportFailure = null)
        where TCapture : class
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(idSelector);
        ArgumentNullException.ThrowIfNull(outputPlan);
        ArgumentNullException.ThrowIfNull(captureScenario);
        ArgumentNullException.ThrowIfNull(createBlockedCapture);
        ArgumentNullException.ThrowIfNull(createLimitation);

        outputPlan.EnsureDirectories();
        if (logProgress)
            VisualEvidenceProgressLog.Reset(outputPlan.ProgressPath);

        var scenarios = SelectScenarios(catalog, scenarioId, idSelector);
        var captures = new List<TCapture>(scenarios.Count);
        var limitations = new List<string>();
        foreach (var scenario in scenarios)
        {
            var id = idSelector(scenario);
            if (logProgress)
            {
                VisualEvidenceProgressLog.Append(
                    outputPlan.ProgressPath,
                    new VisualEvidenceProgressRecord($"start {id}"));
            }

            try
            {
                captures.Add(await captureScenario(scenario));
                if (logProgress)
                {
                    VisualEvidenceProgressLog.Append(
                        outputPlan.ProgressPath,
                        new VisualEvidenceProgressRecord($"complete {id}"));
                }
            }
            catch (Exception exception)
            {
                if (logProgress)
                {
                    VisualEvidenceProgressLog.Append(
                        outputPlan.ProgressPath,
                        new VisualEvidenceProgressRecord($"failed {id}: {exception}"));
                }
                if (createBlockedCapture(scenario, exception) is { } blockedCapture)
                    captures.Add(blockedCapture);
                if (createLimitation(scenario, exception) is { Length: > 0 } limitation)
                    limitations.Add(limitation);
                reportFailure?.Invoke(scenario, exception);
            }
        }

        return new(scenarios, captures, limitations);
    }

    public static int FinalizeHostRun<TScenario, TCapture, TManifest>(
        VisualEvidenceHostOutputPlan outputPlan,
        VisualEvidenceScenarioRun<TScenario, TCapture> run,
        Func<IReadOnlyList<TCapture>, IReadOnlyList<string>, TManifest> createManifest,
        JsonSerializerOptions manifestOptions)
        where TCapture : class
    {
        ArgumentNullException.ThrowIfNull(outputPlan);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(createManifest);
        ArgumentNullException.ThrowIfNull(manifestOptions);

        VisualEvidenceManifestIO.Write(
            outputPlan.ManifestPath,
            createManifest(run.Captures, run.Limitations),
            manifestOptions);
        return run.Captures.Count == run.Scenarios.Count ? 0 : 1;
    }

    private static IReadOnlyList<TScenario> SelectScenarios<TScenario>(
        IReadOnlyList<TScenario> scenarios,
        string? scenarioId,
        Func<TScenario, string> idSelector)
    {
        if (scenarioId is null)
            return scenarios;
        return
        [
            scenarios.Single(scenario => StringComparer.Ordinal.Equals(idSelector(scenario), scenarioId)),
        ];
    }
}
