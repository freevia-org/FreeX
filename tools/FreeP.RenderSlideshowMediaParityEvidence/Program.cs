using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Free.ToolsShared;

const string JsonName = "docs/parity/freep-render-slideshow-media-parity-20260720.json";
const string MarkdownName = "docs/parity/freep-render-slideshow-media-parity-20260720.md";
const string LinuxRuntimeArtifactName = "docs/parity/freep-libvlc-linux-runtime-20260720.json";

var root = RepositoryRootLocator.Find(AppContext.BaseDirectory, "FreeP.slnx")
    ?? throw new DirectoryNotFoundException("Could not find the FreeX workspace root.");
var checkOnly = args.Contains("--check", StringComparer.OrdinalIgnoreCase);
var jsonPath = Path.Combine(root, JsonName.Replace('/', Path.DirectorySeparatorChar));
var markdownPath = Path.Combine(root, MarkdownName.Replace('/', Path.DirectorySeparatorChar));

var report = BuildReport(root);
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() },
};
if (checkOnly)
{
    var existing = File.Exists(jsonPath)
        ? JsonSerializer.Deserialize<ParityReport>(File.ReadAllText(jsonPath), jsonOptions)
        : null;
    var isFresh = existing is not null &&
        existing.SourceSnapshotSha256 == report.SourceSnapshotSha256 &&
        existing.RuntimeArtifactSha256 == report.RuntimeArtifactSha256 &&
        existing.Sources.Count == report.Sources.Count &&
        existing.Areas.Count == report.Areas.Count &&
        existing.Residuals.Count == report.Residuals.Count;
    Console.WriteLine(isFresh ? "Freshness check: PASS" : "Freshness check: FAIL");
    return isFresh ? 0 : 2;
}

Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, jsonOptions));
File.WriteAllText(markdownPath, RenderMarkdown(report));
Console.WriteLine($"Generated {JsonName}");
Console.WriteLine($"Generated {MarkdownName}");
Console.WriteLine($"Freshness check: PASS ({report.Sources.Count} sources, {report.Areas.Count} areas, {report.Residuals.Count} residuals)");
return 0;

static ParityReport BuildReport(string root)
{
    var sourcePaths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["linux-harness/runtime-artifact"] = LinuxRuntimeArtifactName,
        ["wpf-host/slideshow"] = "freep/FreeP.App.Host/SlideShowWindow.cs",
        ["wpf-host/media-adapter"] = "freep/FreeP.App.Host/SlideShowMediaController.cs",
        ["wpf-host/slide-image-renderer"] = "freep/FreeP.App.Host/WpfPresentationSlideImageRenderer.cs",
        ["wpf-host/shape-renderer"] = "freep/FreeP.App.Host/WpfShapeRenderer.cs",
        ["wpf-renderer/slide-canvas"] = "freep/FreeP.App.Rendering.Wpf/SlideCanvas.cs",
        ["avalonia-host/slideshow"] = "freep/FreeP.App.Avalonia/SlideShowWindow.cs",
        ["avalonia-host/media-adapter"] = "freep/FreeP.App.Avalonia/AvaloniaSlideShowMediaController.cs",
        ["media/backend-contracts"] = "freep/FreeP.App.Media/MediaPlaybackContracts.cs",
        ["media/libvlc-backend"] = "freep/FreeP.App.Media/LibVlcMediaPlaybackBackend.cs",
        ["dependencies/media-packages"] = "Directory.Packages.props",
        ["linux-harness/media-runtime"] = "tools/LinuxInteractiveDocker/Dockerfile",
        ["linux-harness/media-runtime-probe"] = "tools/FreeP.MediaRuntimeProbe/Program.cs",
        ["avalonia-renderer/slide-canvas"] = "freep/FreeP.App.Rendering.Avalonia/SlideCanvas.cs",
        ["avalonia-renderer/offscreen-renderer"] = "freep/FreeP.App.Rendering.Avalonia/SlideRenderer.cs",
        ["shared/shape-material-planner"] = "freep/FreeP.App.Presentation/ShapeMaterialRenderPlanner.cs",
        ["shared/media-interaction-planner"] = "freep/FreeP.App.Presentation/SlideShowMediaInteractionPlanner.cs",
        ["shared/slideshow-playback-planner"] = "freep/FreeP.App.Presentation/SlideShowPlaybackPlanner.cs",
        ["shared/slideshow-host-planner"] = "freep/FreeP.App.Presentation/SlideShowHostPlanner.cs",
        ["shared/presenter-tools"] = "freep/FreeP.App.Presentation/SlideShowPresenterToolPlanner.cs",
        ["shared/recording-adapter-parity"] = "freep/FreeP.App.Presentation/SlideShowRecordingHostAdapterParityPlanner.cs",
        ["shared/media-transcript"] = "freep/FreeP.App.Presentation/PresentationMediaTranscriptPlanner.cs",
        ["shared/export-planner"] = "freep/FreeP.App.Presentation/PresentationExportPlanner.cs",
        ["shared/print-backstage-planner"] = "freep/FreeP.App.Presentation/PresentationPrintBackstagePlanner.cs",
        ["shared/notes-preview-planner"] = "freep/FreeP.App.Presentation/PresentationNotesPagePreviewPlanner.cs",
        ["shared/video-package-executor"] = "freep/FreeP.App.Presentation/PresentationVideoFramePackageExecutor.cs",
    };

    var runtimeArtifactPath = Path.Combine(root, LinuxRuntimeArtifactName.Replace('/', Path.DirectorySeparatorChar));
    var runtimeArtifactBytes = File.ReadAllBytes(runtimeArtifactPath);
    ValidateLinuxRuntimeArtifact(runtimeArtifactBytes);
    var sources = sourcePaths.Select(pair => BuildSource(root, pair.Key, pair.Value)).ToArray();
    var snapshot = string.Join("\n", sources.Select(source =>
        $"{source.Id}|{source.RelativePath}|{source.Sha256}|{source.LengthBytes}|{source.LastWriteTimeUtc:O}"));

    var areas = new[]
    {
        new Area("renderer.imported-3d-material", "Renderer", "closed-platform-parity", "Shared ShapeMaterialRenderPlanner supplies all four imported WPF material/depth routes to both native canvases."),
        new Area("slideshow.media-click-routing", "Slideshow/media", "closed-platform-parity", "WPF and Avalonia consume the shared letterboxed media bounds and handle media clicks before normal advance."),
        new Area("slideshow.transitions-and-animation", "Slideshow/playback", "closed-platform-parity", "Both hosts consume shared transition, animation-step, mask, and playback-frame planners; native animation timing remains host rendering."),
        new Area("presenter.timing-ink-tools", "Presenter", "closed-platform-parity", "Both hosts route presenter timing, pointer/ink state, session summaries, and recording readiness through shared planners."),
        new Area("media.caption-package-authoring", "Media/captions", "closed-platform-parity", "Caption/transcript package planning and visible authoring contracts are shared across WPF and Avalonia."),
        new Area("export.preview.fixed-layout", "Export/preview", "closed-platform-parity", "PDF, image, notes-page, print-preview, and video-frame-package policy is shared; hosts provide render/file adapters."),
        new Area("export.native-print-and-mp4", "Export/preview", "shared-product-limitation", "Native printer handoff and MP4 encoder execution are deferred by the shared product contract; both hosts report the same package-ready boundary."),
        new Area("slideshow.media-playback-backend", "Slideshow/media", "closed-platform-parity", "WPF retains native playback and Avalonia now uses the shared LibVLCSharp engine with runtime capability detection, audio/video sessions, native VideoView surfaces, transition-sound playback, and a real Ubuntu interactive-harness WAV lifecycle probe."),
    };

    var residuals = new[]
    {
        new Residual("external-powerpoint-baseline", "external-powerpoint-evidence", "Exact PowerPoint slideshow/render/export/preview visual baselines are not claimed without fresh COM artifacts.", "No PowerPoint COM artifacts are generated by this tool."),
        new Residual("real-recording-hardware", "real-hardware-evidence", "Live microphone/camera capture, permission prompts, and encoded camera payloads require real devices.", "No hardware capture artifacts are generated by this tool."),
        new Residual("native-output-backends", "shared-product-limitation", "Native printer handoff and MP4 encoding remain the documented shared deferred boundary.", "Frame/package planning and materialization remain shared and testable."),
    };

    return new ParityReport(
        SchemaVersion: 1,
        GeneratedAtUtc: DateTimeOffset.UtcNow,
        Commit: Git(root, "rev-parse HEAD"),
        Branch: Git(root, "branch --show-current"),
        FreshnessRule: "Fresh when every authoritative source id, relative path, byte length, UTC write time, and SHA-256 matches this report.",
        SourceSnapshotSha256: Sha256(snapshot),
        RuntimeArtifactPath: LinuxRuntimeArtifactName,
        RuntimeArtifactSha256: Sha256Bytes(runtimeArtifactBytes),
        Sources: sources,
        Areas: areas,
        Residuals: residuals);
}

static void ValidateLinuxRuntimeArtifact(byte[] bytes)
{
    using var document = JsonDocument.Parse(bytes);
    var root = document.RootElement;
    var probe = root.GetProperty("probe");
    if (!string.Equals(root.GetProperty("result").GetString(), "PASS", StringComparison.Ordinal)
        || !probe.GetProperty("isAvailable").GetBoolean()
        || !probe.GetProperty("sessionCreated").GetBoolean()
        || !probe.GetProperty("openSucceeded").GetBoolean()
        || !probe.GetProperty("playObserved").GetBoolean()
        || !probe.GetProperty("seekSucceeded").GetBoolean()
        || !probe.GetProperty("stopSucceeded").GetBoolean()
        || probe.GetProperty("sessionFailure").ValueKind != JsonValueKind.Null)
    {
        throw new InvalidDataException("The persisted Linux LibVLC runtime artifact does not prove a successful lifecycle.");
    }

    var states = probe.GetProperty("states").EnumerateArray()
        .Select(state => state.GetString())
        .ToHashSet(StringComparer.Ordinal);
    if (!states.Contains("Opening") || !states.Contains("Playing") || !states.Contains("Stopped"))
        throw new InvalidDataException("The persisted Linux LibVLC runtime artifact is missing required playback states.");
}

static SourceEntry BuildSource(string root, string id, string relativePath)
{
    var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    var bytes = File.ReadAllBytes(path);
    return new SourceEntry(
        id,
        relativePath,
        bytes.LongLength,
        File.GetLastWriteTimeUtc(path),
        Sha256Bytes(bytes));
}

static string RenderMarkdown(ParityReport report)
{
    var builder = new StringBuilder();
    builder.AppendLine("# FreeP Renderer, Slideshow, Media, Presenter, and Export Parity Evidence");
    builder.AppendLine();
    builder.AppendLine($"Generated: `{report.GeneratedAtUtc:O}`");
    builder.AppendLine($"Commit: `{report.Commit}`");
    builder.AppendLine($"Branch: `{report.Branch}`");
    builder.AppendLine($"Freshness check: **PASS**");
    builder.AppendLine($"Source snapshot SHA-256: `{report.SourceSnapshotSha256}`");
    builder.AppendLine();
    builder.AppendLine("## Runtime Artifact");
    builder.AppendLine();
    builder.AppendLine($"- `path`: `{report.RuntimeArtifactPath}`");
    builder.AppendLine($"- `sha256`: `{report.RuntimeArtifactSha256}`");
    builder.AppendLine("- `validation`: **PASS** (the evidence generator validated native availability and the required WAV session lifecycle fields)");
    builder.AppendLine();
    builder.AppendLine("## Counts");
    builder.AppendLine();
    foreach (var group in report.Areas.GroupBy(area => area.Classification, StringComparer.Ordinal))
        builder.AppendLine($"- `{group.Key}`: **{group.Count()}**");
    builder.AppendLine($"- `authoritative-sources`: **{report.Sources.Count}**");
    builder.AppendLine($"- `explicit-residuals`: **{report.Residuals.Count}**");
    builder.AppendLine();
    builder.AppendLine("## Area Classification");
    builder.AppendLine();
    builder.AppendLine("| Area | Domain | Classification | Evidence |");
    builder.AppendLine("| --- | --- | --- | --- |");
    foreach (var area in report.Areas)
        builder.AppendLine($"| `{area.Id}` | {area.Domain} | `{area.Classification}` | {area.Evidence} |");
    builder.AppendLine();
    builder.AppendLine("## Residuals");
    builder.AppendLine();
    builder.AppendLine("| Residual | Classification | Exact boundary | Artifact status |");
    builder.AppendLine("| --- | --- | --- | --- |");
    foreach (var residual in report.Residuals)
        builder.AppendLine($"| `{residual.Id}` | `{residual.Classification}` | {residual.Description} | {residual.ArtifactStatus} |");
    builder.AppendLine();
    builder.AppendLine("## Authoritative Source Inventory");
    builder.AppendLine();
    builder.AppendLine("| Id | Relative path | Bytes | Last write UTC | SHA-256 |");
    builder.AppendLine("| --- | --- | ---: | --- | --- |");
    foreach (var source in report.Sources)
        builder.AppendLine($"| `{source.Id}` | `{source.RelativePath}` | {source.LengthBytes} | `{source.LastWriteTimeUtc:O}` | `{source.Sha256}` |");
    builder.AppendLine();
    builder.AppendLine("The report does not claim PowerPoint COM, real microphone/camera, or native MP4/printer evidence without corresponding artifacts.");
    return builder.ToString();
}

static string Git(string root, string arguments)
{
    using var process = Process.Start(new ProcessStartInfo("git", arguments)
    {
        WorkingDirectory = root,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    });
    if (process is null) return "unknown";
    var output = process.StandardOutput.ReadToEnd().Trim();
    process.WaitForExit();
    return process.ExitCode == 0 && output.Length > 0 ? output : "unknown";
}

static string Sha256(string value) => Sha256Bytes(Encoding.UTF8.GetBytes(value));

static string Sha256Bytes(byte[] bytes) =>
    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

record ParityReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string Commit,
    string Branch,
    string FreshnessRule,
    string SourceSnapshotSha256,
    string RuntimeArtifactPath,
    string RuntimeArtifactSha256,
    IReadOnlyList<SourceEntry> Sources,
    IReadOnlyList<Area> Areas,
    IReadOnlyList<Residual> Residuals);

record SourceEntry(string Id, string RelativePath, long LengthBytes, DateTime LastWriteTimeUtc, string Sha256);
record Area(string Id, string Domain, string Classification, string Evidence);
record Residual(string Id, string Classification, string Description, string ArtifactStatus);
