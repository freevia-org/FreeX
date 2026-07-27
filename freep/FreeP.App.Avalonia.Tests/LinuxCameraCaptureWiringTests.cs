namespace FreeP.App.Avalonia.Tests;

public sealed class LinuxCameraCaptureWiringTests
{
    [Fact]
    public void Linux_default_slideshow_backend_composes_narration_and_camera_capture()
    {
        var source = File.ReadAllText(RepoFile("freep/FreeP.App.Avalonia/SlideShowWindow.cs"));

        source.Should().Contain("new LinuxRecordingCaptureBackend(");
        source.Should().Contain("new LinuxNarrationCaptureBackend(metadata)");
        source.Should().Contain("new LinuxCameraCaptureBackend(metadata)");
        source.Should().NotContain("return new LinuxNarrationCaptureBackend(");
    }

    [Fact]
    public void Linux_camera_backend_uses_real_v4l2_ffmpeg_capture_and_mp4_validation()
    {
        var planner = File.ReadAllText(RepoFile(
            "freep/FreeP.App.Recording/Recording/LinuxCameraCapturePlanner.cs"));
        var backend = File.ReadAllText(RepoFile(
            "freep/FreeP.App.Recording/Recording/LinuxCameraCaptureBackend.cs"));

        planner.Should().Contain("\"v4l2\"");
        planner.Should().Contain("\"-c:v\"");
        planner.Should().Contain("LinuxNarrationCaptureToolKind.FfmpegCamera");
        backend.Should().Contain("LinuxVideoExportAdapter.HasNonEmptyMp4Payload");
        backend.Should().Contain("SlideShowRecordingCaptureResult.Captured");
    }

    private static string RepoFile(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath));
}
