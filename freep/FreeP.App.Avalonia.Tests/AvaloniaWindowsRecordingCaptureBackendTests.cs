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
        backend.AdapterReadiness.AdapterName.Should().Be("Avalonia Windows recording capture adapter");
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
        backend.AdapterReadiness.StatusText.Should().Contain("No Windows microphone or camera devices");
        backend.AdapterReadiness.StatusText.Should().NotContain("not registered");
    }

    [Fact]
    public void Readiness_WithMicrophoneAndCamera_ProjectsNarrationAndCameraHandoff()
    {
        var backend = new AvaloniaWindowsRecordingCaptureBackend(
            new FakeDeviceCatalog(
                new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Microphone,
                    "mic-0",
                    "Studio microphone",
                    IsDefault: true,
                    IsAvailable: true,
                    "audio/mp4"),
                new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Camera,
                    "camera-0",
                    "Presenter camera",
                    IsDefault: true,
                    IsAvailable: true,
                    "video/mp4")),
            new FakeCaptureEngine());

        backend.AdapterReadiness.CanCaptureNarration.Should().BeTrue();
        backend.AdapterReadiness.CanCaptureCamera.Should().BeTrue();
        backend.AdapterReadiness.ReadyStreams.Should().Equal(
            SlideShowRecordingCaptureStreamKind.NarrationAudio,
            SlideShowRecordingCaptureStreamKind.CameraVideo);
        backend.AdapterReadiness.MissingStreams.Should().BeEmpty();
        backend.Capabilities.UnavailableReason.Should().BeEmpty();
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

    [Fact]
    public void Planner_WithAvaloniaCameraBackend_StartsAndCompletesVideoWhenEngineProvidesPayload()
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
                    "audio/mp4"),
                new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Camera,
                    "camera-0",
                    "Presenter camera",
                    IsDefault: true,
                    IsAvailable: true,
                    "video/mp4")),
            engine);
        var started = new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
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
            started.AddMilliseconds(2400));

        engine.StartedRequests.Should().HaveCount(4);
        engine.StartedRequests.Where(request => request.Device.Kind == SlideShowRecordingCaptureDeviceKind.Camera)
            .Select(request => request.PackagePath)
            .Should().Equal(
                "ppt/media/freep-recordings/avalonia/slide-001-camera.mp4",
                "ppt/media/freep-recordings/avalonia/slide-002-camera.mp4");
        moved.LastActions.Select(action => action.Kind).Should().ContainInOrder(
            SlideShowRecordingExecutionActionKind.StartNarrationCapture,
            SlideShowRecordingExecutionActionKind.StartCameraCapture);

        var segment = moved.Segments.Should().ContainSingle().Subject;
        segment.CameraCaptured.Should().BeTrue();
        var camera = segment.MediaArtifacts.Single(artifact => artifact.Kind == SlideShowRecordingMediaArtifactKind.CameraVideo);
        camera.Should().Match<SlideShowRecordingMediaArtifact>(artifact =>
            artifact.IsCaptured &&
            !artifact.IsDeferred &&
            artifact.IsPersistable &&
            artifact.SuggestedFileName == "slide-001-camera.mp4" &&
            artifact.ContentType == "video/mp4" &&
            artifact.PackagePath == "ppt/media/freep-recordings/avalonia/slide-001-camera.mp4" &&
            artifact.PayloadBytes != null &&
            artifact.PayloadBytes.Length == artifact.ContentLengthBytes &&
            artifact.ContentSha256.Length == 64);
    }

    [Fact]
    public void DefaultWindowsEngine_CameraCaptureDefersEncodedPayloadAfterHandoff()
    {
        var engine = new AvaloniaWindowsRecordingCaptureEngine();
        var device = new SlideShowRecordingCaptureDeviceDescriptor(
            SlideShowRecordingCaptureDeviceKind.Camera,
            "camera-0",
            "Presenter camera",
            IsDefault: true,
            IsAvailable: true,
            "video/mp4");
        var started = new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);

        engine.BeginCapture(new AvaloniaWindowsRecordingCaptureStartRequest(
            device,
            SlideIndex: 0,
            started,
            "ppt/media/freep-recordings/avalonia/slide-001-camera.mp4"));
        var result = engine.CompleteCapture(new AvaloniaWindowsRecordingCaptureRequest(
            device,
            SlideIndex: 0,
            DurationMs: 1500,
            "ppt/media/freep-recordings/avalonia/slide-001-camera.mp4"));

        result.IsCaptured.Should().BeFalse();
        result.StatusText.Should().Contain("camera device handoff reached");
        result.StatusText.Should().Contain("video encoding is not implemented");
        result.PayloadBytes.Should().BeEmpty();
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
                $"{request.Device.Kind}|{request.Device.DeviceId}|{request.SlideIndex}|{request.DurationMs}|{request.PackagePath}");

            return AvaloniaWindowsRecordingCaptureResult.Captured(
                $"Fake Avalonia {request.Device.Kind} captured {request.PackagePath}",
                request.PackagePath,
                payload);
        }
    }
}
