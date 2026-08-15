using FreeP.App.Compositor;

namespace FreeP.App.Recording;

public sealed record SlideShowRecordingMediaArtifactDescriptor(
    SlideShowRecordingMediaArtifactKind Kind,
    SlideShowRecordingCaptureDeviceKind DeviceKind,
    string Extension,
    string ContentType,
    string DefaultFileName);

/// <summary>
/// Owns portable file and device metadata for slideshow recording artifacts. Capture engines,
/// payload validation, temporary files, and host-specific package roots remain platform-owned.
/// </summary>
public static class SlideShowRecordingMediaArtifactPolicy
{
    private static readonly SlideShowRecordingMediaArtifactDescriptor Narration = new(
        SlideShowRecordingMediaArtifactKind.NarrationAudio,
        SlideShowRecordingCaptureDeviceKind.Microphone,
        ".wav",
        "audio/wav",
        "slide-narration.wav");

    private static readonly SlideShowRecordingMediaArtifactDescriptor Camera = new(
        SlideShowRecordingMediaArtifactKind.CameraVideo,
        SlideShowRecordingCaptureDeviceKind.Camera,
        ".mp4",
        "video/mp4",
        "slide-camera.mp4");

    public static SlideShowRecordingMediaArtifactDescriptor Describe(
        SlideShowRecordingMediaArtifactKind kind) =>
        TryDescribe(kind, out var descriptor)
            ? descriptor
            : throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unsupported recording media kind.");

    public static bool TryDescribe(
        SlideShowRecordingMediaArtifactKind kind,
        out SlideShowRecordingMediaArtifactDescriptor descriptor)
    {
        descriptor = kind switch
        {
            SlideShowRecordingMediaArtifactKind.NarrationAudio => Narration,
            SlideShowRecordingMediaArtifactKind.CameraVideo => Camera,
            _ => null!,
        };
        return descriptor is not null;
    }

    public static bool CanCapture(
        SlideShowRecordingMediaArtifactDescriptor descriptor,
        SlideShowRecordingCaptureAdapterReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(readiness);
        return descriptor.DeviceKind switch
        {
            SlideShowRecordingCaptureDeviceKind.Microphone => readiness.CanCaptureNarration,
            SlideShowRecordingCaptureDeviceKind.Camera => readiness.CanCaptureCamera,
            _ => false,
        };
    }

    public static string NormalizePackagePath(
        SlideShowRecordingMediaArtifactKind kind,
        string? packageRoot,
        string? suggestedFileName,
        string defaultPackageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPackageRoot);
        var descriptor = Describe(kind);
        var fileName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? descriptor.DefaultFileName
            : suggestedFileName.Trim().Replace('\\', '/').Split('/').Last();
        fileName = Path.ChangeExtension(fileName, descriptor.Extension);
        var normalizedRoot = string.IsNullOrWhiteSpace(packageRoot)
            ? defaultPackageRoot
            : packageRoot.Trim().Replace('\\', '/').Trim('/');
        return $"{normalizedRoot}/{fileName}";
    }
}
