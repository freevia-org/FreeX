using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Free.Shared.Shell;
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
    string UnavailableReason,
    bool CanMuxTimedCaptions = false)
{
    public static PresentationVideoExportHandoffHostCapabilities Deferred(string hostName, string unavailableReason) =>
        new(hostName, CanEncodeMp4: false, CanCaptureNarration: false, CanCaptureCameraAndMedia: false, unavailableReason, CanMuxTimedCaptions: false);
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

public sealed record PresentationVideoFramePackageArtifact(
    PresentationVideoFramePackage Package,
    IReadOnlyList<string> ImageDiagnostics);

public sealed record PresentationVideoFramePackageValidation(
    int ByteCount,
    bool HasBytes,
    bool HasZipContainer,
    bool HasManifest,
    bool HasEncoderDeferredMarker,
    int ExpectedFrameCount,
    int ManifestFrameCount,
    int ZipFrameEntryCount,
    bool FrameCountMatchesPackage,
    bool ContentTypeIsZip,
    bool ExtensionIsZip,
    bool PlanCanBuildPackage,
    bool IsValid,
    string? FailureReason);

public sealed record PresentationVideoFramePackageExecutionDescriptor(
    PresentationVideoFramePackagePlan PackagePlan,
    PresentationVideoExportHandoffPlan HandoffPlan,
    PresentationVideoFramePackageValidation Validation,
    string PackageKind,
    string ContentType,
    string DefaultExtensionWithDot,
    string SuggestedPackageName,
    int FrameCount,
    int ByteCount,
    bool IsEncoderInputPackage,
    bool CanMaterialize,
    string? DisabledReason);

public sealed record PresentationVideoFramePackageMaterializationResult(
    PresentationVideoFramePackageExecutionDescriptor Descriptor,
    string TargetPath,
    bool Succeeded,
    string? FailureReason);

/// <summary>
/// Shared video encoder-input execution for FreeP. Hosts provide only a slide PNG renderer;
/// range, quality, timing, metadata, and the MP4-deferred boundary stay in the presentation layer.
/// </summary>
public static class PresentationVideoFramePackageExecutor
{
    public const string PackageContentType = "application/zip";
    public const string PackageExtension = ".zip";
    public const string EncoderInputPackageKind = "FreePVideoEncoderInputPackage";
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
    public const string InvalidPackageReason =
        "Video encoder-input handoff requires a valid ZIP package.";

    private static readonly DateTimeOffset DeterministicZipTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly PresentationVideoExportHandoffHostCapabilities DefaultHostCapabilities =
        PresentationVideoExportHandoffHostCapabilities.Deferred("Host video export host", HostEncoderDeferredReason);

    public static PresentationVideoFramePackagePlan BuildPackagePlan(
        PresentationVideoExportRequest? request,
        int slideCount)
    {
        var exportPlan = PresentationExportPlanner.BuildVideoExportPlan(request, slideCount);
        return BuildPackagePlan(exportPlan);
    }

    public static PresentationVideoFramePackagePlan BuildPackagePlan(
        PresentationVideoExportRequest? request,
        Presentation presentation,
        PresentationVideoExportHandoffHostCapabilities? hostCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var exportPlan = PresentationExportPlanner.BuildVideoExportPlan(
            request,
            presentation,
            hostCapabilities);
        return BuildPackagePlan(exportPlan);
    }

    public static PresentationVideoFramePackage BuildPackage(
        Presentation presentation,
        PresentationVideoExportRequest? request,
        PresentationSlideImageRenderer renderSlideToPng,
        PresentationVideoExportHandoffHostCapabilities? hostCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(renderSlideToPng);

        var plan = BuildPackagePlan(request, presentation, hostCapabilities);
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

    public static PresentationVideoFramePackageArtifact BuildPackageWithDiagnostics(
        Presentation presentation,
        PresentationVideoExportRequest? request,
        PresentationSlideImageRenderer renderSlideToPng,
        PresentationVideoExportHandoffHostCapabilities? hostCapabilities = null)
    {
        var imageDiagnostics = new List<string>();
        using var capture = SlideImageRenderDiagnostics.Capture(imageDiagnostics);
        var package = BuildPackage(presentation, request, renderSlideToPng, hostCapabilities);
        return new PresentationVideoFramePackageArtifact(package, imageDiagnostics);
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

    public static PresentationVideoFramePackageValidation ValidatePackage(PresentationVideoFramePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var bytes = package.Bytes;
        var expectedFrameCount = package.Frames.Count;
        var hasBytes = bytes.Length > 0;
        var hasZipContainer = false;
        var hasManifest = false;
        var hasEncoderDeferredMarker = false;
        var manifestFrameCount = -1;
        var zipFrameEntryCount = 0;
        string? zipFailureReason = null;

        if (hasBytes)
        {
            try
            {
                using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                hasZipContainer = true;
                hasManifest = archive.GetEntry("manifest.json") is not null;
                hasEncoderDeferredMarker = EntryContains(archive.GetEntry("encoder-deferred.txt"), "MP4 encoding");
                zipFrameEntryCount = archive.Entries.Count(entry =>
                    entry.FullName.StartsWith("frames/", StringComparison.Ordinal) &&
                    entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                    entry.Length > 0);
                manifestFrameCount = CountManifestFrames(archive.GetEntry("manifest.json"));
            }
            catch (InvalidDataException)
            {
                zipFailureReason = "Video encoder-input package is not a valid ZIP archive.";
            }
        }

        var frameCountMatchesPackage =
            expectedFrameCount > 0 &&
            manifestFrameCount == expectedFrameCount &&
            zipFrameEntryCount == expectedFrameCount;
        var contentTypeIsZip = string.Equals(package.Plan.ContentType, PackageContentType, StringComparison.OrdinalIgnoreCase);
        var extensionIsZip = string.Equals(package.Plan.DefaultExtensionWithDot, PackageExtension, StringComparison.OrdinalIgnoreCase);
        var planCanBuild = package.Plan.CanBuildPackage && expectedFrameCount > 0;
        var failureReason =
            !planCanBuild ? package.Plan.DisabledReason ?? "Video encoder-input package requires at least one frame." :
            !hasBytes ? "Video encoder-input package contains no bytes." :
            !hasZipContainer ? zipFailureReason ?? "Video encoder-input package is not a valid ZIP archive." :
            !contentTypeIsZip ? "Video encoder-input package content type must be application/zip." :
            !extensionIsZip ? "Video encoder-input package extension must be .zip." :
            !hasManifest ? "Video encoder-input package is missing manifest.json." :
            !hasEncoderDeferredMarker ? "Video encoder-input package is missing encoder-deferred.txt." :
            !frameCountMatchesPackage ? "Video encoder-input package frame counts do not match the manifest and ZIP entries." :
            null;

        return new PresentationVideoFramePackageValidation(
            bytes.Length,
            hasBytes,
            hasZipContainer,
            hasManifest,
            hasEncoderDeferredMarker,
            expectedFrameCount,
            manifestFrameCount,
            zipFrameEntryCount,
            frameCountMatchesPackage,
            contentTypeIsZip,
            extensionIsZip,
            planCanBuild,
            failureReason is null,
            failureReason);
    }

    public static PresentationVideoFramePackageExecutionDescriptor BuildExecutionDescriptor(
        PresentationVideoFramePackage package,
        PresentationVideoExportHandoffHostCapabilities? hostCapabilities = null,
        string? suggestedBaseFileName = null)
    {
        ArgumentNullException.ThrowIfNull(package);

        var handoffPlan = BuildHandoffPlan(package.Plan, hostCapabilities ?? DefaultHostCapabilities);
        var validation = ValidatePackage(package);
        var isEncoderInputPackage = validation.IsValid &&
            string.Equals(package.Plan.ContentType, PackageContentType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(package.Plan.DefaultExtensionWithDot, PackageExtension, StringComparison.OrdinalIgnoreCase);
        var disabledReason = isEncoderInputPackage ? null : validation.FailureReason ?? InvalidPackageReason;

        return new PresentationVideoFramePackageExecutionDescriptor(
            package.Plan,
            handoffPlan,
            validation,
            EncoderInputPackageKind,
            package.Plan.ContentType,
            package.Plan.DefaultExtensionWithDot,
            BuildSuggestedPackageName(suggestedBaseFileName),
            package.Frames.Count,
            validation.ByteCount,
            isEncoderInputPackage,
            isEncoderInputPackage,
            disabledReason);
    }

    public static PresentationVideoFramePackageMaterializationResult MaterializePackageForHandoff(
        PresentationVideoFramePackage package,
        string targetPath,
        PresentationVideoExportHandoffHostCapabilities? hostCapabilities = null,
        string? suggestedBaseFileName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var descriptor = BuildExecutionDescriptor(package, hostCapabilities, suggestedBaseFileName);
        if (!descriptor.CanMaterialize)
        {
            return new PresentationVideoFramePackageMaterializationResult(
                descriptor,
                targetPath,
                Succeeded: false,
                descriptor.DisabledReason);
        }

        ExportAtomicWriter.WriteAllBytes(targetPath, package.Bytes);
        return new PresentationVideoFramePackageMaterializationResult(
            descriptor,
            targetPath,
            Succeeded: true,
            FailureReason: null);
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

    private static string BuildSuggestedPackageName(string? suggestedBaseFileName)
    {
        var baseName = string.IsNullOrWhiteSpace(suggestedBaseFileName)
            ? "Presentation"
            : Path.GetFileNameWithoutExtension(suggestedBaseFileName.Trim());
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(baseName.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "Presentation";

        return $"{sanitized}-video-encoder-input{PackageExtension}";
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
            BuildHostCapability(
                "Timed captions",
                hostCapabilities.CanMuxTimedCaptions,
                "This host cannot mux timed captions; install ffmpeg or use a host with mov_text support."),
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
            PackageKind: EncoderInputPackageKind,
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

    private static bool EntryContains(ZipArchiveEntry? entry, string marker)
    {
        if (entry is null)
            return false;

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Contains(marker, StringComparison.Ordinal);
    }

    private static int CountManifestFrames(ZipArchiveEntry? entry)
    {
        if (entry is null)
            return -1;

        try
        {
            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("Frames", out var frames) &&
                frames.ValueKind == JsonValueKind.Array
                    ? frames.GetArrayLength()
                    : -1;
        }
        catch (JsonException)
        {
            return -1;
        }
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
