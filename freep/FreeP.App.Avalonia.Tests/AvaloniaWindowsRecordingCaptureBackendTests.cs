using System.Text;
using FreeP.App.Avalonia.Recording;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia.Tests;

public sealed class AvaloniaWindowsRecordingCaptureBackendTests
{
    [Fact]
    public void Readiness_WithMicrophone_ProjectsNarrationCaptureAndDeferredCamera()
    {
        var backend = new AvaloniaWindowsRecordingCaptureBackend(
            new FakeDeviceCatalog(
                new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Microphone,
                    "mic-0",
                    "Studio microphone",
                    IsDefault: true,
                    IsAvailable: true,
                    "audio/mp4")),
            new FakeCaptureEngine());

        backend.Capabilities.HostName.Should().Be("Avalonia slideshow");
        backend.AdapterReadiness.AdapterName.Should().Be("Avalonia Windows microphone capture adapter");
        backend.AdapterReadiness.CanCaptureNarration.Should().BeTrue();
        backend.AdapterReadiness.CanCaptureCamera.Should().BeFalse();
        backend.AdapterReadiness.ReadyStreams.Should().Equal(SlideShowRecordingCaptureStreamKind.NarrationAudio);
        backend.AdapterReadiness.MissingStreams.Should().Equal(SlideShowRecordingCaptureStreamKind.CameraVideo);
        backend.AdapterReadiness.StatusText.Should().NotContain("not registered");
    }

    [Fact]
    public void Readiness_WithNoDevices_RemainsOsBackedButUnavailable()
    {
        var backend = new AvaloniaWindowsRecordingCaptureBackend(
            new FakeDeviceCatalog(),
            new FakeCaptureEngine());

        backend.AdapterReadiness.Devices.Should().BeEmpty();
        backend.AdapterReadiness.CanCaptureNarration.Should().BeFalse();
        backend.AdapterReadiness.CanCaptureCamera.Should().BeFalse();
        backend.AdapterReadiness.MissingStreams.Should().Equal(
            SlideShowRecordingCaptureStreamKind.NarrationAudio,
            SlideShowRecordingCaptureStreamKind.CameraVideo);
        backend.AdapterReadiness.StatusText.Should().Contain("No Windows microphone devices");
        backend.AdapterReadiness.StatusText.Should().NotContain("not registered");
    }

    [Fact]
    public void Planner_WithAvaloniaMicrophoneBackend_StartsAndCompletesNarrationCapture()
    {
        var engine = new FakeCaptureEngine();
        var backend = new AvaloniaWindowsRecordingCaptureBackend(
            new FakeDeviceCatalog(
                new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Microphone,
                    "mic-0",
                    "Studio microphone",
                    IsDefault: true,
                    IsAvailable: true,
                    "audio/mp4")),
            engine);
        var started = new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
        var plan = SlideShowPresenterToolPlanner.BuildPlan(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.NarrationAndMedia);

        var state = SlideShowRecordingExecutionPlanner.CreateState(
            plan,
            currentSlideIndex: 0,
            started,
            backend);
        var moved = SlideShowRecordingExecutionPlanner.MoveToSlide(
            state,
            slideIndex: 1,
            started.AddMilliseconds(1800));

        engine.StartedRequests.Should().HaveCount(2);
        engine.StartedRequests[0].Should().Match<AvaloniaWindowsRecordingCaptureStartRequest>(request =>
            request.Device.DeviceId == "mic-0" &&
            request.SlideIndex == 0 &&
            request.PackagePath == "ppt/media/freep-recordings/avalonia/slide-001-narration.wav");
        engine.StartedRequests[1].Should().Match<AvaloniaWindowsRecordingCaptureStartRequest>(request =>
            request.Device.DeviceId == "mic-0" &&
            request.SlideIndex == 1 &&
            request.PackagePath == "ppt/media/freep-recordings/avalonia/slide-002-narration.wav");
        var segment = moved.Segments.Should().ContainSingle().Subject;
        segment.NarrationCaptured.Should().BeTrue();
        segment.CameraCaptured.Should().BeFalse();
        segment.MediaArtifacts.Should().HaveCount(2);
        var narration = segment.MediaArtifacts.Single(artifact => artifact.Kind == SlideShowRecordingMediaArtifactKind.NarrationAudio);
        narration.Should().Match<SlideShowRecordingMediaArtifact>(artifact =>
            artifact.IsCaptured &&
            !artifact.IsDeferred &&
            artifact.IsPersistable &&
            artifact.SuggestedFileName == "slide-001-narration.wav" &&
            artifact.ContentType == "audio/wav" &&
            artifact.PackagePath == "ppt/media/freep-recordings/avalonia/slide-001-narration.wav" &&
            artifact.PayloadBytes != null &&
            artifact.PayloadBytes.Length == artifact.ContentLengthBytes &&
            artifact.ContentSha256.Length == 64);
        var camera = segment.MediaArtifacts.Single(artifact => artifact.Kind == SlideShowRecordingMediaArtifactKind.CameraVideo);
        camera.IsDeferred.Should().BeTrue();
    }

    private sealed class FakeDeviceCatalog : IAvaloniaWindowsRecordingDeviceCatalog
    {
        private readonly IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> _devices;

        public FakeDeviceCatalog(params SlideShowRecordingCaptureDeviceDescriptor[] devices)
        {
            _devices = devices;
        }

        public IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnumerateDevices() => _devices;
    }

    private sealed class FakeCaptureEngine : IAvaloniaWindowsRecordingCaptureEngine
    {
        public List<AvaloniaWindowsRecordingCaptureStartRequest> StartedRequests { get; } = new();

        public void BeginCapture(AvaloniaWindowsRecordingCaptureStartRequest request)
        {
            StartedRequests.Add(request);
        }

        public AvaloniaWindowsRecordingCaptureResult CompleteCapture(AvaloniaWindowsRecordingCaptureRequest request)
        {
            var payload = Encoding.UTF8.GetBytes(
                $"{request.Device.DeviceId}|{request.SlideIndex}|{request.DurationMs}|{request.PackagePath}");

            return AvaloniaWindowsRecordingCaptureResult.Captured(
                $"Fake Avalonia microphone captured {request.PackagePath}",
                request.PackagePath,
                payload);
        }
    }
}
