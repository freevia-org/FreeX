using System.IO;
using FreeP.VisualEvidence;
using Free.ToolsShared;

namespace FreeP.RenderCompare;

internal sealed record PairedVisualEvidenceCollection<TManifest>(
    string OutputDirectory,
    TManifest Wpf,
    TManifest Avalonia,
    IReadOnlyList<string> Limitations);

internal sealed record PairedVisualEvidenceArtifactContext<TCapture>(
    string Host,
    string ScenarioId,
    string ScenarioRoot,
    VisualEvidenceScenarioOutputPlan FinalOutput,
    string ProcessResult,
    TCapture Capture);

internal sealed record PairedVisualEvidenceArtifactResult<TCapture>(
    TCapture? Capture,
    IReadOnlyList<string> Limitations)
    where TCapture : class;

internal sealed record PairedVisualEvidenceArtifact(
    string DeclaredPath,
    string DestinationPath,
    bool RequireNonzeroFile = false);

internal sealed record PairedVisualEvidenceProfile<TScenario, TManifest, TCapture>(
    VisualEvidenceCaptureRoute Route,
    IReadOnlyList<TScenario> Scenarios,
    Func<TScenario, string> ScenarioId,
    Func<TManifest, IEnumerable<TCapture>> ManifestCaptures,
    Func<TCapture, string> CaptureScenarioId,
    Func<TManifest, IEnumerable<string>> ManifestLimitations,
    string MissingCaptureMessage,
    string TimedOutProcessTreeDescription,
    Action<VisualEvidenceScenarioOutputPlan> PrepareFinalArtifacts,
    Func<PairedVisualEvidenceArtifactContext<TCapture>, PairedVisualEvidenceArtifactResult<TCapture>> CollectArtifacts,
    Func<string, IReadOnlyList<TCapture>, IReadOnlyList<string>, TManifest> CreateManifest)
    where TManifest : class
    where TCapture : class;

internal static class PairedVisualEvidenceCollector
{
    internal static PairedVisualEvidenceCollection<TManifest> Collect<TScenario, TManifest, TCapture>(
        string outputDirectory,
        string wpfExecutable,
        string avaloniaExecutable,
        TimeSpan timeout,
        PairedVisualEvidenceProfile<TScenario, TManifest, TCapture> profile)
        where TManifest : class
        where TCapture : class
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        wpfExecutable = Path.GetFullPath(wpfExecutable);
        avaloniaExecutable = Path.GetFullPath(avaloniaExecutable);
        ValidateExecutable(wpfExecutable, "WPF capture host was not found.");
        ValidateExecutable(avaloniaExecutable, "Avalonia capture host was not found.");

        Directory.CreateDirectory(outputDirectory);
        using var runDirectory = new VisualEvidenceRunDirectory(profile.Route.TemporaryDirectoryPrefix);
        var limitations = new List<string>();
        var wpf = CaptureHost(
            FreePVisualEvidenceCaptureOrchestration.WpfHost,
            wpfExecutable,
            outputDirectory,
            runDirectory.Path,
            timeout,
            limitations,
            profile);
        var avalonia = CaptureHost(
            FreePVisualEvidenceCaptureOrchestration.AvaloniaHost,
            avaloniaExecutable,
            outputDirectory,
            runDirectory.Path,
            timeout,
            limitations,
            profile);
        return new(outputDirectory, wpf, avalonia, limitations);
    }

    internal static TManifest CaptureHost<TScenario, TManifest, TCapture>(
        string host,
        string executable,
        string outputDirectory,
        string runRoot,
        TimeSpan timeout,
        List<string> runnerLimitations,
        PairedVisualEvidenceProfile<TScenario, TManifest, TCapture> profile,
        Func<VisualEvidenceProcessPlan, string>? scenarioRunner = null)
        where TManifest : class
        where TCapture : class
    {
        var outputPlan = FreePVisualEvidenceCaptureOrchestration.CreateHostOutputPlan(
            outputDirectory,
            host,
            profile.Route);
        outputPlan.EnsureDirectories();
        var captures = new List<TCapture>();
        var hostLimitations = new List<string>();

        foreach (var scenario in profile.Scenarios)
        {
            var scenarioId = profile.ScenarioId(scenario);
            var finalOutput = FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(
                outputDirectory,
                host,
                scenarioId,
                profile.Route);
            profile.PrepareFinalArtifacts(finalOutput);

            var scenarioRoot = FreePVisualEvidenceCaptureOrchestration.CreateScenarioRunRoot(
                runRoot,
                host,
                scenarioId);
            Directory.CreateDirectory(scenarioRoot);
            Console.WriteLine($"[{host}] {scenarioId}");
            var processPlan = FreePVisualEvidenceCaptureOrchestration.CreateScenarioProcessPlan(
                executable,
                scenarioRoot,
                profile.Route,
                scenarioId,
                timeout,
                profile.TimedOutProcessTreeDescription);
            var processResult = (scenarioRunner ?? VisualEvidenceToolSupport.RunScenario)(processPlan);
            var scenarioOutput = FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(
                scenarioRoot,
                host,
                scenarioId,
                profile.Route);
            var scenarioManifest = FreePVisualEvidenceCaptureOrchestration.ReadScenarioManifest<TManifest, TCapture>(
                scenarioOutput.HostManifestPath,
                FreePVisualEvidenceCaptureOrchestration.ToolManifestJsonOptions,
                scenarioId,
                profile.ManifestCaptures,
                profile.CaptureScenarioId);
            if (scenarioManifest.Status == VisualEvidenceScenarioManifestStatus.MissingManifest)
            {
                runnerLimitations.Add($"{host} {scenarioId}: {processResult} No host manifest was produced.");
                continue;
            }

            var manifest = scenarioManifest.Manifest;
            var capture = scenarioManifest.Capture;
            if (capture is null)
            {
                runnerLimitations.Add($"{host} {scenarioId}: {processResult} {profile.MissingCaptureMessage}");
                continue;
            }

            var artifactResult = profile.CollectArtifacts(new(
                host,
                scenarioId,
                scenarioRoot,
                finalOutput,
                processResult,
                capture));
            runnerLimitations.AddRange(artifactResult.Limitations);
            if (artifactResult.Capture is null)
                continue;

            captures.Add(artifactResult.Capture);
            if (manifest is not null)
                hostLimitations.AddRange(profile.ManifestLimitations(manifest));
        }

        var hostManifest = profile.CreateManifest(
            host,
            captures,
            hostLimitations.Distinct(StringComparer.Ordinal).ToArray());
        FreePVisualEvidenceCaptureOrchestration.WriteManifest(
            outputPlan.ManifestPath,
            hostManifest,
            FreePVisualEvidenceCaptureOrchestration.ToolManifestJsonOptions);
        return hostManifest;
    }

    internal static string ValidateExecutable(string executable, string missingMessage)
    {
        var fullPath = Path.GetFullPath(executable);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(missingMessage, fullPath);
        return fullPath;
    }

    internal static bool TryCopyArtifacts(
        string scenarioRoot,
        params PairedVisualEvidenceArtifact[] artifacts)
    {
        var resolved = artifacts
            .Select(artifact => (
                Artifact: artifact,
                Source: FreePVisualEvidenceCaptureOrchestration.ResolveDeclaredPath(
                    scenarioRoot,
                    artifact.DeclaredPath)))
            .ToArray();
        if (resolved.Any(item => item.Artifact.RequireNonzeroFile
                ? !FreePVisualEvidenceCaptureOrchestration.IsNonzeroFile(item.Source)
                : !File.Exists(item.Source)))
        {
            return false;
        }

        foreach (var item in resolved)
            File.Copy(item.Source, item.Artifact.DestinationPath, overwrite: true);
        return true;
    }

    internal static TManifest ReadHostManifest<TManifest>(
        string outputDirectory,
        string host,
        VisualEvidenceCaptureRoute route,
        string missingMessage,
        string invalidMessage)
        where TManifest : class
    {
        var path = FreePVisualEvidenceCaptureOrchestration.CreateHostOutputPlan(
            outputDirectory,
            host,
            route).ManifestPath;
        return VisualEvidenceToolSupport.ReadManifest<TManifest>(
            path,
            FreePVisualEvidenceCaptureOrchestration.ToolManifestJsonOptions,
            missingMessage,
            invalidMessage);
    }
}
