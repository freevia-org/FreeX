using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationVideoFramePackagePlan(
    PresentationVideoExportPlan ExportPlan,
    string ContentType,
    string DefaultExtensionWithDot,
    bool CanBuildPackage,
    IReadOnlyList<string> DeferredCapabilities,
    string? DisabledReason);

public enum PresentationVideoExportHandoffStatus
{
    EncoderInputPackageReadyHostDeferred,
    HostEncoderReady,
    NoSlides,
}

public sealed record PresentationVideoExportHandoffHostCapabilities(
    string HostName,
    bool CanEncodeMp4,
    bool CanCaptureNarration,
    bool CanCaptureCameraAndMedia,
    string UnavailableReason)
{
    public static PresentationVideoExportHandoffHostCapabilities Deferred(string hostName, string unavailableReason) =>
        new(hostName, CanEncodeMp4: false, CanCaptureNarration: false, CanCaptureCameraAndMedia: false, unavailableReason);
}

public sealed record PresentationVideoExportCapabilityPlan(
    string Name,
    bool IsAvailable,
    bool IsDeferred,
    string StatusText);

public sealed record PresentationVideoExportHandoffPlan(
    PresentationVideoFramePackagePlan PackagePlan,
    PresentationVideoExportHandoffHostCapabilities HostCapabilities,
    PresentationVideoExportHandoffStatus Status,
    bool IsFramePackageReady,
    bool RequiresHostEncoder,
    bool CanOpenHostEncoder,
    bool Mp4EncoderDeferredByHost,
    string StatusText,
    string Reason,
    IReadOnlyList<PresentationVideoExportCapabilityPlan> Capabilities);

public sealed record PresentationVideoFramePackageFrame(
    int SegmentIndex,
    int SlideIndex,
    int SlideNumber,
    string SlideTitle,
    string FileName,
    long ByteCount,
    int WidthPx,
    int HeightPx,
    TimeSpan StartTime,
    TimeSpan Duration,
    PresentationVideoTimingSource TimingSource);

public sealed record PresentationVideoFramePackage(
    PresentationVideoFramePackagePlan Plan,
    IReadOnlyList<PresentationVideoFramePackageFrame> Frames,
    byte[] Bytes);

/// <summary>
/// Shared video encoder-input execution for FreeP. Hosts provide only a slide PNG renderer;
/// range, quality, timing, metadata, and the MP4-deferred boundary stay in the presentation layer.
/// </summary>
public static class PresentationVideoFramePackageExecutor
{
    public const string PackageContentType = "application/zip";
    public const string PackageExtension = ".zip";
    public const string EncoderDeferred = nameof(EncoderDeferred);
    public const string Mp4EncoderDeferred = nameof(Mp4EncoderDeferred);
    public const string NarrationCaptureDeferred = nameof(NarrationCaptureDeferred);
    public const string CameraCaptureDeferred = nameof(CameraCaptureDeferred);
    public const string MediaCaptureDeferred = nameof(MediaCaptureDeferred);
    public const string EncoderDeferredReason =
        "Video frame package execution is available; MP4 encoding, narration, camera, and media capture execution are deferred.";
    public const string HostEncoderDeferredReason =
        "Video frame package is ready for host handoff; MP4 encoder integration is deferred by this host.";
    public const string HostEncoderReadyStatus =
        "Video frame package is ready for host MP4 encoder handoff.";

    private static readonly DateTimeOffset DeterministicZipTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
    };

    public static PresentationVideoFramePackagePlan BuildPackagePlan(
        PresentationVideoExportRequest? request,
        int slideCount)
    {
        var exportPlan = PresentationExportPlanner.BuildVideoExportPlan(request, slideCount);
        return BuildPackagePlan(exportPlan);
    }

    public static PresentationVideoFramePackagePlan BuildPackagePlan(
        PresentationVideoExportRequest? request,
        Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var exportPlan = PresentationExportPlanner.BuildVideoExportPlan(request, presentation);
        return BuildPackagePlan(exportPlan);
    }

    public static PresentationVideoFramePackage BuildPackage(
        Presentation presentation,
        PresentationVideoExportRequest? request,
        PresentationSlideImageRenderer renderSlideToPng)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(renderSlideToPng);

        var plan = BuildPackagePlan(request, presentation);
        if (!plan.CanBuildPackage)
            return new PresentationVideoFramePackage(plan, [], []);

        var storyboard = plan.ExportPlan.Storyboard;
        var frameDigits = Math.Max(4, storyboard.Segments.Count.ToString().Length);
        var slideDigits = Math.Max(2, presentation.Slides.Count.ToString().Length);
        var frames = new List<PresentationVideoFramePackageFrame>(storyboard.Segments.Count);
        var frameBytes = new List<(PresentationVideoFramePackageFrame Frame, byte[] Bytes)>(storyboard.Segments.Count);

        for (var index = 0; index < storyboard.Segments.Count; index++)
        {
            var segment = storyboard.Segments[index];
            var bytes = renderSlideToPng(
                presentation,
                segment.SlideIndex,
                storyboard.OutputWidthPx,
                storyboard.OutputHeightPx);
            if (bytes.Length == 0)
                throw new InvalidOperationException($"Video frame renderer returned no bytes for slide {segment.SlideNumber}.");

            var fileName =
                $"frames/slide-{segment.SlideNumber.ToString($"D{slideDigits}")}-frame-{(index + 1).ToString($"D{frameDigits}")}.png";
            var frame = new PresentationVideoFramePackageFrame(
                index,
                segment.SlideIndex,
                segment.SlideNumber,
                segment.SlideTitle,
                fileName,
                bytes.Length,
                storyboard.OutputWidthPx,
                storyboard.OutputHeightPx,
                segment.StartTime,
                segment.Duration,
                segment.TimingSource);

            frames.Add(frame);
            frameBytes.Add((frame, bytes));
        }

        var packageBytes = BuildZipPackage(plan, frames, frameBytes);
        return new PresentationVideoFramePackage(plan, frames, packageBytes);
    }

    public static PresentationVideoExportHandoffPlan BuildHandoffPlan(
        PresentationVideoFramePackagePlan packagePlan,
        PresentationVideoExportHandoffHostCapabilities hostCapabilities)
    {
        ArgumentNullException.ThrowIfNull(packagePlan);
        ArgumentNullException.ThrowIfNull(hostCapabilities);

        var noSlides = !packagePlan.CanBuildPackage;
        var canOpenHostEncoder = packagePlan.CanBuildPackage && hostCapabilities.CanEncodeMp4;
        var status = noSlides
            ? PresentationVideoExportHandoffStatus.NoSlides
            : canOpenHostEncoder
                ? PresentationVideoExportHandoffStatus.HostEncoderReady
                : PresentationVideoExportHandoffStatus.EncoderInputPackageReadyHostDeferred;
        var reason = status switch
        {
            PresentationVideoExportHandoffStatus.NoSlides =>
                packagePlan.DisabledReason ?? "Video export requires at least one slide.",
            PresentationVideoExportHandoffStatus.HostEncoderReady => HostEncoderReadyStatus,
            _ => string.IsNullOrWhiteSpace(hostCapabilities.UnavailableReason)
                ? HostEncoderDeferredReason
                : hostCapabilities.UnavailableReason,
        };

        return new PresentationVideoExportHandoffPlan(
            packagePlan,
            hostCapabilities,
            status,
            IsFramePackageReady: packagePlan.CanBuildPackage,
            RequiresHostEncoder: packagePlan.CanBuildPackage,
            CanOpenHostEncoder: canOpenHostEncoder,
            Mp4EncoderDeferredByHost: packagePlan.CanBuildPackage && !hostCapabilities.CanEncodeMp4,
            FormatHandoffStatusText(status, hostCapabilities.HostName),
            reason,
            BuildCapabilityPlans(packagePlan, hostCapabilities));
    }

    private static PresentationVideoFramePackagePlan BuildPackagePlan(PresentationVideoExportPlan exportPlan)
    {
        var canBuild = exportPlan.Storyboard.Segments.Count > 0;
        return new PresentationVideoFramePackagePlan(
            exportPlan,
            PackageContentType,
            PackageExtension,
            canBuild,
            [
                EncoderDeferred,
                Mp4EncoderDeferred,
                NarrationCaptureDeferred,
                CameraCaptureDeferred,
                MediaCaptureDeferred,
            ],
            canBuild ? null : "Video frame package requires at least one slide.");
    }

    private static IReadOnlyList<PresentationVideoExportCapabilityPlan> BuildCapabilityPlans(
        PresentationVideoFramePackagePlan packagePlan,
        PresentationVideoExportHandoffHostCapabilities hostCapabilities)
    {
        var framePackageStatus = packagePlan.CanBuildPackage
            ? "Encoder input frame package can be built."
            : packagePlan.DisabledReason ?? "Video frame package requires at least one slide.";
        return
        [
            new("Frame package", packagePlan.CanBuildPackage, IsDeferred: false, framePackageStatus),
            BuildHostCapability("MP4 encoder", hostCapabilities.CanEncodeMp4, hostCapabilities.UnavailableReason),
            BuildHostCapability("Narration capture", hostCapabilities.CanCaptureNarration, hostCapabilities.UnavailableReason),
            BuildHostCapability("Camera and media capture", hostCapabilities.CanCaptureCameraAndMedia, hostCapabilities.UnavailableReason),
        ];
    }

    private static PresentationVideoExportCapabilityPlan BuildHostCapability(
        string name,
        bool isAvailable,
        string unavailableReason) =>
        new(
            name,
            isAvailable,
            IsDeferred: !isAvailable,
            isAvailable ? $"{name} available through host adapter." : unavailableReason);

    private static string FormatHandoffStatusText(
        PresentationVideoExportHandoffStatus status,
        string hostName) =>
        status switch
        {
            PresentationVideoExportHandoffStatus.NoSlides => "Video export requires at least one slide.",
            PresentationVideoExportHandoffStatus.HostEncoderReady => $"{hostName}: host MP4 encoder ready",
            _ => $"{hostName}: MP4 encoder deferred; frame package ready",
        };

    private static byte[] BuildZipPackage(
        PresentationVideoFramePackagePlan plan,
        IReadOnlyList<PresentationVideoFramePackageFrame> frames,
        IReadOnlyList<(PresentationVideoFramePackageFrame Frame, byte[] Bytes)> frameBytes)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(
                archive,
                "manifest.json",
                JsonSerializer.Serialize(BuildManifest(plan, frames), ManifestJsonOptions));
            WriteTextEntry(archive, "encoder-deferred.txt", EncoderDeferredReason);

            foreach (var (frame, bytes) in frameBytes)
                WriteBytesEntry(archive, frame.FileName, bytes);
        }

        return stream.ToArray();
    }

    private static PresentationVideoFramePackageManifest BuildManifest(
        PresentationVideoFramePackagePlan plan,
        IReadOnlyList<PresentationVideoFramePackageFrame> frames)
    {
        var export = plan.ExportPlan;
        var storyboard = export.Storyboard;
        return new PresentationVideoFramePackageManifest(
            PackageKind: "FreePVideoFramePackage",
            PackageStatus: "EncoderInputPackageBuilt",
            DeferredCapabilities: plan.DeferredCapabilities,
            EncoderDeferredReason,
            ExportCommandId: export.CommandId,
            Mp4ExportPlanImplemented: export.IsImplemented,
            Mp4ExportCanExecute: export.CanExecute,
            Mp4ExportDisabledReason: export.DisabledReason,
            SlideRange: new PresentationVideoFramePackageRangeManifest(
                export.SlideRange.Kind.ToString(),
                export.SlideRange.DisplayName,
                export.SlideRange.SlideNumbers),
            Quality: new PresentationVideoFramePackageQualityManifest(
                export.Quality.Quality.ToString(),
                export.Quality.DisplayName,
                storyboard.OutputWidthPx,
                storyboard.OutputHeightPx,
                storyboard.PixelsPerSecondHint,
                storyboard.FrameRateHint),
            SecondsPerSlide: export.SecondsPerSlide,
            UseRecordedTimings: export.UseRecordedTimings,
            IncludeNarrationIntent: export.IncludeNarration,
            TotalDuration: storyboard.TotalDuration,
            Frames: frames.Select(frame => new PresentationVideoFramePackageFrameManifest(
                frame.SegmentIndex,
                frame.SlideIndex,
                frame.SlideNumber,
                frame.SlideTitle,
                frame.FileName,
                frame.ByteCount,
                frame.WidthPx,
                frame.HeightPx,
                frame.StartTime,
                frame.Duration,
                frame.TimingSource.ToString())).ToArray());
    }

    private static void WriteTextEntry(ZipArchive archive, string name, string text) =>
        WriteBytesEntry(archive, name, Encoding.UTF8.GetBytes(text));

    private static void WriteBytesEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = DeterministicZipTimestamp;
        using var entryStream = entry.Open();
        entryStream.Write(bytes, 0, bytes.Length);
    }

    private sealed record PresentationVideoFramePackageManifest(
        string PackageKind,
        string PackageStatus,
        IReadOnlyList<string> DeferredCapabilities,
        string EncoderDeferredReason,
        string ExportCommandId,
        bool Mp4ExportPlanImplemented,
        bool Mp4ExportCanExecute,
        string? Mp4ExportDisabledReason,
        PresentationVideoFramePackageRangeManifest SlideRange,
        PresentationVideoFramePackageQualityManifest Quality,
        double SecondsPerSlide,
        bool UseRecordedTimings,
        bool IncludeNarrationIntent,
        TimeSpan TotalDuration,
        IReadOnlyList<PresentationVideoFramePackageFrameManifest> Frames);

    private sealed record PresentationVideoFramePackageRangeManifest(
        string Kind,
        string DisplayName,
        IReadOnlyList<int> SlideNumbers);

    private sealed record PresentationVideoFramePackageQualityManifest(
        string Quality,
        string DisplayName,
        int WidthPx,
        int HeightPx,
        int PixelsPerSecondHint,
        double FrameRateHint);

    private sealed record PresentationVideoFramePackageFrameManifest(
        int SegmentIndex,
        int SlideIndex,
        int SlideNumber,
        string SlideTitle,
        string FileName,
        long ByteCount,
        int WidthPx,
        int HeightPx,
        TimeSpan StartTime,
        TimeSpan Duration,
        string TimingSource);
}
