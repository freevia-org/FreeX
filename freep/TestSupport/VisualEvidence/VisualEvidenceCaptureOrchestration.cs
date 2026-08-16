using System.Globalization;
using System.IO;
using System.Text.Json;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using Free.ToolsShared;

namespace FreeP.VisualEvidence;

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
    int? WholeWindowLogicalWidth,
    string? Error)
{
    internal bool IsValid => IsRequested && Error is null;
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

internal sealed record FreePVisualEvidenceAppHostPolicy(
    string Host,
    VisualEvidenceCaptureRoute Route,
    string CaptureDescription,
    double TargetDpi,
    int LogicalWidth,
    int LogicalHeight)
{
    internal VisualEvidenceHostOutputPlan CreateOutputPlan(string outputRoot) =>
        FreePVisualEvidenceCaptureOrchestration.CreateHostOutputPlan(outputRoot, Host, Route);

    internal VisualEvidenceScenarioOutputPlan CreateScenarioOutputPlan(
        string outputRoot,
        string scenarioId) =>
        FreePVisualEvidenceCaptureOrchestration.CreateScenarioOutputPlan(outputRoot, Host, scenarioId, Route);

    internal DialogPaneVisualEvidenceCapture CreateBlockedDialogPaneCapture(
        DialogPaneVisualEvidenceScenario scenario,
        Exception exception) =>
        new(
            scenario.Id,
            scenario.RouteId,
            scenario.StateId,
            Host,
            "blocked",
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            string.Empty,
            string.Empty,
            [],
            [],
            [new("capture-completed", false, exception.Message)],
            [$"Capture failed: {exception.GetType().Name}: {exception.Message}"]);

    internal DialogPaneVisualEvidenceHostManifest CreateDialogPaneManifest(
        IReadOnlyList<DialogPaneVisualEvidenceCapture> captures,
        IReadOnlyList<string> limitations) =>
        new(
            1,
            Host,
            CaptureDescription,
            TargetDpi,
            LogicalWidth,
            LogicalHeight,
            FreePVisualEvidenceCaptureOrchestration.UtcTimestamp(),
            captures,
            limitations);

    internal WholeWindowVisualEvidenceHostManifest CreateWholeWindowManifest(
        IReadOnlyList<WholeWindowVisualEvidenceCapture> captures,
        IReadOnlyList<string> limitations) =>
        new(
            1,
            Host,
            CaptureDescription,
            TargetDpi,
            LogicalWidth,
            LogicalHeight,
            FreePVisualEvidenceCaptureOrchestration.UtcTimestamp(),
            captures,
            limitations);

    internal string DescribeFailure(string scenarioId, Exception exception) =>
        $"{scenarioId}: {exception.GetType().Name}: {exception.Message}";
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

    internal static FreePVisualEvidenceAppHostPolicy CreateAppHostPolicy(
        string host,
        VisualEvidenceCaptureRoute route,
        int? wholeWindowLogicalWidth = null) => route.Kind switch
        {
            VisualEvidenceCaptureKind.DialogPane => new(
                host,
                route,
                "visible-app-owned-render-target",
                DialogPaneVisualEvidenceCatalog.TargetDpi,
                DialogPaneVisualEvidenceCatalog.LogicalShellWidth,
                DialogPaneVisualEvidenceCatalog.LogicalShellHeight),
            VisualEvidenceCaptureKind.WholeWindow => new(
                host,
                route,
                "visible-app-owned-full-client-render-target; native-non-client-excluded; scenario-isolated-process",
                WholeWindowVisualEvidenceCatalog.TargetDpi,
                wholeWindowLogicalWidth ?? WholeWindowVisualEvidenceCatalog.LogicalClientWidth,
                WholeWindowVisualEvidenceCatalog.LogicalClientHeight),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };

    internal static VisualEvidenceCaptureRequest ParseRequest(
        string[] args,
        VisualEvidenceCaptureRoute route,
        IEnumerable<string> knownScenarioIds)
    {
        var output = VisualEvidenceArgumentParser.ReadFirst(args, route.OutputArgument);
        if (!output.IsPresent)
            return new(false, null, null, null, null);

        if (output.Value is null)
            return new(true, null, null, null, $"{route.OutputArgument} requires an output directory.");

        var outputRoot = Path.GetFullPath(output.Value);
        var scenario = VisualEvidenceArgumentParser.ReadFirst(args, route.ScenarioArgument);
        if (scenario.IsPresent && scenario.Value is null)
            return new(true, outputRoot, null, null, $"{route.ScenarioArgument} requires a scenario id.");

        var scenarioId = scenario.Value;
        if (scenarioId is not null && !knownScenarioIds.Contains(scenarioId, StringComparer.Ordinal))
            return new(true, outputRoot, scenarioId, null, route.UnknownScenarioMessagePrefix + scenarioId);

        if (route.Kind != VisualEvidenceCaptureKind.WholeWindow)
            return new(true, outputRoot, scenarioId, null, null);

        var width = VisualEvidenceArgumentParser.ReadFirst(args, "--whole-window-visual-evidence-width");
        if (!width.IsPresent)
            return new(true, outputRoot, scenarioId, null, null);

        if (width.Value is null ||
            !int.TryParse(width.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var logicalWidth) ||
            !WholeWindowVisualEvidenceCatalog.ResponsiveChromeWidths.Contains(logicalWidth))
        {
            var allowed = string.Join(", ", WholeWindowVisualEvidenceCatalog.ResponsiveChromeWidths);
            return new(true, outputRoot, scenarioId, null,
                $"--whole-window-visual-evidence-width requires one of: {allowed}.");
        }

        return new(true, outputRoot, scenarioId, logicalWidth, null);
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
        VisualEvidenceProcessPlan.Create(
            executable,
            Path.GetDirectoryName(executable)!,
            [route.OutputArgument, outputRoot, route.ScenarioArgument, scenarioId],
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

}
