using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using Free.ToolsShared;

namespace FreeP.VisualEvidence;

internal sealed class VisualEvidenceRunDirectory : IDisposable
{
    private const int MaximumAttempts = 60;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    internal VisualEvidenceRunDirectory(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (prefix.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("The temporary-directory prefix must be a valid file-name prefix.", nameof(prefix));

        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            string.Concat(prefix, System.IO.Path.GetRandomFileName()));
        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    public void Dispose()
    {
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
                return;
            }
            catch (Exception exception) when (
                (exception is IOException or UnauthorizedAccessException) && attempt < MaximumAttempts)
            {
                Thread.Sleep(RetryDelay);
            }
        }
    }
}

internal enum VisualEvidenceCaptureKind
{
    DialogPane,
    WholeWindow,
}

internal sealed record VisualEvidenceCaptureRoute(
    VisualEvidenceCaptureKind Kind,
    string OutputArgument,
    string ScenarioArgument,
    string UnknownScenarioMessagePrefix,
    string TemporaryDirectoryPrefix);

internal static class FreePVisualEvidenceRoutes
{
    internal static readonly VisualEvidenceCaptureRoute DialogPane = new(
        VisualEvidenceCaptureKind.DialogPane,
        "--dialog-pane-visual-evidence-output",
        "--dialog-pane-visual-evidence-scenario",
        "Unknown visual evidence scenario: ",
        "freep-dialog-pane-evidence-");

    internal static readonly VisualEvidenceCaptureRoute WholeWindow = new(
        VisualEvidenceCaptureKind.WholeWindow,
        "--whole-window-visual-evidence-output",
        "--whole-window-visual-evidence-scenario",
        "Unknown whole-window visual evidence scenario: ",
        "freep-whole-window-evidence-");
}

internal sealed record VisualEvidenceCaptureRequest(
    bool IsRequested,
    string? OutputRoot,
    string? ScenarioId,
    string? Error)
{
    internal bool IsValid => IsRequested && Error is null;
}

internal sealed record VisualEvidenceHostOutputPlan(
    string HostDirectory,
    string ManifestPath,
    string? ProgressPath,
    string? FullDirectory,
    string? ClientDirectory,
    string? TargetsDirectory)
{
    internal void EnsureDirectories()
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

internal sealed record VisualEvidenceScenarioOutputPlan(
    string HostManifestPath,
    string? ImagePath,
    string? ImageRelativePath,
    string? ComparisonImagePath,
    string? ComparisonImageRelativePath,
    string? FullImagePath,
    string? FullImageRelativePath,
    string? ClientImagePath,
    string? ClientImageRelativePath);

internal sealed record VisualEvidenceProcessPlan(
    string Executable,
    string WorkingDirectory,
    string Arguments,
    TimeSpan Timeout,
    string TimedOutProcessTreeDescription)
{
    internal int TimeoutMilliseconds => (int)Timeout.TotalMilliseconds;
}

internal enum VisualEvidenceScenarioManifestStatus
{
    Ready,
    MissingManifest,
    MissingCapture,
}

internal sealed record VisualEvidenceScenarioManifest<TManifest, TCapture>(
    VisualEvidenceScenarioManifestStatus Status,
    TManifest? Manifest,
    TCapture? Capture)
    where TManifest : class
    where TCapture : class;

internal static class FreePVisualEvidenceCaptureOrchestration
{
    internal const string WpfHost = "wpf";
    internal const string AvaloniaHost = "avalonia";

    internal static readonly JsonSerializerOptions HostManifestJsonOptions = CreateManifestJsonOptions();
    internal static readonly JsonSerializerOptions ToolManifestJsonOptions = CreateManifestJsonOptions(
        propertyNameCaseInsensitive: true);

    internal static VisualEvidenceCaptureRequest ParseRequest(
        string[] args,
        VisualEvidenceCaptureRoute route,
        IEnumerable<string> knownScenarioIds)
    {
        var output = VisualEvidenceArgumentParser.ReadFirst(args, route.OutputArgument);
        if (!output.IsPresent)
            return new(false, null, null, null);

        if (output.Value is null)
            return new(true, null, null, $"{route.OutputArgument} requires an output directory.");

        var outputRoot = Path.GetFullPath(output.Value);
        var scenario = VisualEvidenceArgumentParser.ReadFirst(args, route.ScenarioArgument);
        if (scenario.IsPresent && scenario.Value is null)
            return new(true, outputRoot, null, $"{route.ScenarioArgument} requires a scenario id.");

        var scenarioId = scenario.Value;
        if (scenarioId is not null && !knownScenarioIds.Contains(scenarioId, StringComparer.Ordinal))
            return new(true, outputRoot, scenarioId, route.UnknownScenarioMessagePrefix + scenarioId);

        return new(true, outputRoot, scenarioId, null);
    }

    internal static IReadOnlyList<TScenario> SelectScenarios<TScenario>(
        IReadOnlyList<TScenario> scenarios,
        string? scenarioId,
        Func<TScenario, string> idSelector)
    {
        if (scenarioId is null)
            return scenarios;
        return new[]
        {
            scenarios.Single(scenario => StringComparer.Ordinal.Equals(idSelector(scenario), scenarioId)),
        };
    }

    internal static VisualEvidenceHostOutputPlan CreateHostOutputPlan(
        string outputRoot,
        string host,
        VisualEvidenceCaptureRoute route)
    {
        var hostDirectory = Path.Combine(outputRoot, host);
        return route.Kind switch
        {
            VisualEvidenceCaptureKind.DialogPane => new(
                hostDirectory,
                Path.Combine(hostDirectory, "manifest.json"),
                Path.Combine(hostDirectory, "capture-progress.log"),
                null,
                null,
                Path.Combine(hostDirectory, "targets")),
            VisualEvidenceCaptureKind.WholeWindow => new(
                hostDirectory,
                Path.Combine(hostDirectory, "manifest.json"),
                null,
                Path.Combine(hostDirectory, "full"),
                Path.Combine(hostDirectory, "client"),
                null),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
    }

    internal static VisualEvidenceScenarioOutputPlan CreateScenarioOutputPlan(
        string outputRoot,
        string host,
        string scenarioId,
        VisualEvidenceCaptureRoute route)
    {
        var hostPlan = CreateHostOutputPlan(outputRoot, host, route);
        var fileName = ToSafeFileName(scenarioId) + ".png";
        if (route.Kind == VisualEvidenceCaptureKind.DialogPane)
        {
            return new(
                hostPlan.ManifestPath,
                Path.Combine(hostPlan.HostDirectory, fileName),
                Relative(host, fileName),
                Path.Combine(hostPlan.TargetsDirectory!, fileName),
                Relative(host, "targets", fileName),
                null,
                null,
                null,
                null);
        }

        return new(
            hostPlan.ManifestPath,
            null,
            null,
            null,
            null,
            Path.Combine(hostPlan.FullDirectory!, fileName),
            Relative(host, "full", fileName),
            Path.Combine(hostPlan.ClientDirectory!, fileName),
            Relative(host, "client", fileName));
    }

    internal static string CreateScenarioRunRoot(string runRoot, string host, string scenarioId) =>
        Path.Combine(runRoot, host, scenarioId);

    internal static VisualEvidenceProcessPlan CreateScenarioProcessPlan(
        string executable,
        string outputRoot,
        VisualEvidenceCaptureRoute route,
        string scenarioId,
        TimeSpan timeout,
        string timedOutProcessTreeDescription) =>
        new(
            executable,
            Path.GetDirectoryName(executable)!,
            $"{Quote(route.OutputArgument)} {Quote(outputRoot)} {Quote(route.ScenarioArgument)} {Quote(scenarioId)}",
            timeout,
            timedOutProcessTreeDescription);

    internal static JsonSerializerOptions CreateManifestJsonOptions(bool propertyNameCaseInsensitive = false) =>
        VisualEvidenceManifestIO.CreateJsonOptions(
            propertyNameCaseInsensitive: propertyNameCaseInsensitive);

    internal static T ReadManifest<T>(
        string path,
        JsonSerializerOptions options,
        string missingMessage,
        string invalidMessage)
        where T : class
    {
        return VisualEvidenceManifestIO.Read<T>(path, options, missingMessage, invalidMessage);
    }

    internal static VisualEvidenceScenarioManifest<TManifest, TCapture> ReadScenarioManifest<TManifest, TCapture>(
        string path,
        JsonSerializerOptions options,
        string scenarioId,
        Func<TManifest, IEnumerable<TCapture>> captures,
        Func<TCapture, string> scenarioIdSelector)
        where TManifest : class
        where TCapture : class
    {
        var manifest = VisualEvidenceManifestIO.ReadIfExists<TManifest>(path, options);
        if (manifest is null && !File.Exists(path))
            return new(VisualEvidenceScenarioManifestStatus.MissingManifest, null, null);

        var capture = manifest is null
            ? null
            : captures(manifest).SingleOrDefault(candidate =>
                StringComparer.Ordinal.Equals(scenarioIdSelector(candidate), scenarioId));
        return capture is null
            ? new(VisualEvidenceScenarioManifestStatus.MissingCapture, manifest, null)
            : new(VisualEvidenceScenarioManifestStatus.Ready, manifest, capture);
    }

    internal static void WriteManifest<T>(string path, T manifest, JsonSerializerOptions options) =>
        VisualEvidenceManifestIO.Write(path, manifest, options);

    internal static string ResolveDeclaredPath(string outputRoot, string relativePath) =>
        VisualEvidencePathPolicy.ResolveContainedPath(
            outputRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

    internal static bool IsNonzeroFile(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0;

    internal static void ResetProgress(VisualEvidenceHostOutputPlan plan)
        => VisualEvidenceProgressLog.Reset(plan.ProgressPath);

    internal static void AppendProgress(VisualEvidenceHostOutputPlan plan, string message)
        => VisualEvidenceProgressLog.Append(
            plan.ProgressPath,
            new VisualEvidenceProgressRecord(message));

    internal static string Sha256(string path) =>
        VisualEvidenceHash.Sha256File(path);

    internal static string UtcTimestamp() =>
        DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    internal static string ToSafeFileName(string value) =>
        VisualEvidenceTextPolicy.ToSafeArtifactName(value);

    internal static string NormalizeLabel(string? label, string? fallback = null) =>
        VisualEvidenceTextPolicy.NormalizeLabel(label, fallback);

    internal static string SemanticActionId(string label) =>
        VisualEvidenceTextPolicy.SemanticActionId(label);

    private static string Relative(params string[] parts) =>
        Path.Combine(parts).Replace('\\', '/');

    private static string Quote(string value) =>
        '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
}
