using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.App.Recording.Windows;

namespace FreeP.App.Host.Tests;

public sealed class WindowsRecordingCaptureEngineTests
{
    private const string AdapterName = "WPF Windows recording capture adapter";

    [Fact]
    public void CameraCapture_PreservesNoComHandoffBehavior()
    {
        var engine = new WindowsRecordingCaptureEngine(AdapterName);
        var device = Camera();
        var packagePath = "ppt/media/freep-recordings/wpf/slide-001-camera.mp4";

        engine.BeginCapture(new WindowsRecordingCaptureStartRequest(
            device,
            SlideIndex: 0,
            new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero),
            packagePath));
        var result = engine.CompleteCapture(new WindowsRecordingCaptureRequest(
            device,
            SlideIndex: 0,
            DurationMs: 1500,
            packagePath));

        result.IsCaptured.Should().BeFalse();
        result.StatusText.Should().Contain("camera device handoff reached");
        result.StatusText.Should().Contain("video encoding is not implemented");
        result.PayloadBytes.Should().BeEmpty();
    }

    [Fact]
    public void CompleteCapture_WithoutBegin_PreservesNotStartedError()
    {
        var result = new WindowsRecordingCaptureEngine(AdapterName).CompleteCapture(
            new WindowsRecordingCaptureRequest(
                Camera(),
                SlideIndex: 2,
                DurationMs: 1500,
                "ppt/media/freep-recordings/wpf/slide-003-camera.mp4"));

        result.IsCaptured.Should().BeFalse();
        result.StatusText.Should().Be($"{AdapterName}: narration capture was not started for slide 3");
        result.PayloadBytes.Should().BeEmpty();
    }

    private static SlideShowRecordingCaptureDeviceDescriptor Camera() =>
        new(
            SlideShowRecordingCaptureDeviceKind.Camera,
            "camera-0",
            "Presenter camera",
            IsDefault: true,
            IsAvailable: true,
            "video/mp4");
}
