using FreeP.App.Compositor;
using FreeP.App.Recording;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeP.App.Recording.Tests;

public sealed class WindowsRecordingCaptureBackendTests
{
    private static WindowsRecordingHostMetadata WpfMetadata => new(
        "WPF slideshow",
        "WPF Windows recording capture adapter",
        "ppt/media/freep-recordings/wpf");

    [Fact]
    public void DeviceAvailability_ProjectsAvailableMicrophoneAndCameraOnce()
    {
        var availability = WindowsRecordingDeviceAvailabilityPlanner.Detect(
            new FakeDeviceCatalog(
                new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Microphone,
                    "mic",
                    "Microphone",
                    IsDefault: true,
                    IsAvailable: true,
                    "audio/wav"),
                new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Camera,
                    "camera",
                    "Camera",
                    IsDefault: true,
                    IsAvailable: false,
                    "video/mp4")));

        availability.Devices.Should().HaveCount(2);
        availability.HasMicrophone.Should().BeTrue();
        availability.HasCamera.Should().BeFalse();
        availability.HasAvailableDevice.Should().BeTrue();
        availability.DetectionFailure.Should().BeNull();
    }

    [Fact]
    public void DeviceAvailability_CapturesEnumerationFailureForAllConsumers()
    {
        var availability = WindowsRecordingDeviceAvailabilityPlanner.Detect(
            new ThrowingDeviceCatalog());

        availability.Devices.Should().BeEmpty();
        availability.HasMicrophone.Should().BeFalse();
        availability.HasCamera.Should().BeFalse();
        availability.DetectionFailure.Should().Be("device catalog failed");
    }

    [Fact]
    public void Readiness_WhenDeviceEnumerationFails_PreservesCaptureContext()
    {
        var backend = CreateBackend(
            new ThrowingDeviceCatalog(),
            new FakeCaptureEngine());

        backend.AdapterReadiness.UnavailableReason.Should().Be(
            "Windows recording device enumeration failed: device catalog failed");
    }

    [Fact]
    public void Readiness_WithMicrophone_ProjectsNarrationCaptureAndDeferredCamera()
    {
        var backend = CreateBackend(
            new FakeDeviceCatalog(
                new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Microphone,
                    "mic-0",
                    "Studio microphone",
                    IsDefault: true,
                    IsAvailable: true,
                    "audio/mp4")),
            new FakeCaptureEngine());

        backend.Capabilities.HostName.Should().Be("WPF slideshow");
        backend.AdapterReadiness.AdapterName.Should().Be("WPF Windows recording capture adapter");
        backend.AdapterReadiness.CanCaptureNarration.Should().BeTrue();
        backend.AdapterReadiness.CanCaptureCamera.Should().BeFalse();
        backend.AdapterReadiness.ReadyStreams.Should().Equal(SlideShowRecordingCaptureStreamKind.NarrationAudio);
        backend.AdapterReadiness.MissingStreams.Should().Equal(SlideShowRecordingCaptureStreamKind.CameraVideo);
        backend.AdapterReadiness.StatusText.Should().NotContain("not registered");
    }

    [Fact]
    public void Readiness_WithNoDevices_RemainsOsBackedButUnavailable()
    {
        var backend = CreateBackend(
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

        var evidence = SlideShowRecordingHostAdapterParityPlanner.BuildUnavailableHardwareEvidence(
            new[] { backend.AdapterReadiness });
        evidence.HasWpfUnavailableHardware.Should().BeTrue();
        evidence.ClaimsCapture.Should().BeFalse();
        evidence.ClaimsPowerPointComBaseline.Should().BeFalse();
    }

    [Fact]
    public void Readiness_WithMicrophoneAndCamera_ProjectsNarrationAndCameraHandoff()
    {
        var backend = CreateBackend(
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
    public void Planner_WithWpfMicrophoneBackend_StartsAndCompletesNarrationCapture()
    {
        var engine = new FakeCaptureEngine();
        var backend = CreateBackend(
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
        engine.StartedRequests[0].Should().Match<WindowsRecordingCaptureStartRequest>(request =>
            request.Device.DeviceId == "mic-0" &&
            request.SlideIndex == 0 &&
            request.PackagePath == "ppt/media/freep-recordings/wpf/slide-001-narration.wav");
        engine.StartedRequests[1].Should().Match<WindowsRecordingCaptureStartRequest>(request =>
            request.Device.DeviceId == "mic-0" &&
            request.SlideIndex == 1 &&
            request.PackagePath == "ppt/media/freep-recordings/wpf/slide-002-narration.wav");
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
            artifact.PackagePath == "ppt/media/freep-recordings/wpf/slide-001-narration.wav" &&
            artifact.PayloadBytes != null &&
            artifact.PayloadBytes.Length == artifact.ContentLengthBytes &&
            artifact.ContentSha256.Length == 64);
        var camera = segment.MediaArtifacts.Single(artifact => artifact.Kind == SlideShowRecordingMediaArtifactKind.CameraVideo);
        camera.IsDeferred.Should().BeTrue();
    }

    [Fact]
    public void Planner_WithWpfCameraBackend_StartsAndCompletesVideoWhenEngineProvidesPayload()
    {
        var engine = new FakeCaptureEngine();
        var backend = CreateBackend(
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
                "ppt/media/freep-recordings/wpf/slide-001-camera.mp4",
                "ppt/media/freep-recordings/wpf/slide-002-camera.mp4");
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
            artifact.PackagePath == "ppt/media/freep-recordings/wpf/slide-001-camera.mp4" &&
            artifact.PayloadBytes != null &&
            artifact.PayloadBytes.Length == artifact.ContentLengthBytes &&
            artifact.ContentSha256.Length == 64);
    }

    [Fact]
    public void Planner_WithWpfCameraBackend_PersistsEncodedVideoPayloadThroughPptxPackage()
    {
        var (presentation, cameraArtifact) = BuildPresentationWithCapturedCameraArtifact(
            CreateBackend(
                new FakeDeviceCatalog(
                    new SlideShowRecordingCaptureDeviceDescriptor(
                        SlideShowRecordingCaptureDeviceKind.Camera,
                        "camera-0",
                        "Presenter camera",
                        IsDefault: true,
                        IsAvailable: true,
                        "video/mp4")),
                new FakeCaptureEngine()));
        using var stream = new MemoryStream();

        PptxPackageWriter.Write(presentation, stream);

        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var mediaEntry = archive.GetEntry("ppt/media/freep-recordings/wpf/slide-001-camera.mp4");
            mediaEntry.Should().NotBeNull("the WPF encoded camera payload must be written at the host package path");
            ReadBytes(mediaEntry!).Should().Equal(cameraArtifact.PayloadBytes);

            archive.GetEntry("ppt/media/recordingArtifacts.xml").Should().NotBeNull();
            using var contentTypesStream = archive.GetEntry("[Content_Types].xml")!.Open();
            var contentTypes = XDocument.Load(contentTypesStream);
            var contentTypesNamespace = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types");
            contentTypes.Root!.Elements(contentTypesNamespace + "Default").Any(element =>
                    string.Equals(element.Attribute("Extension")?.Value, "mp4", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(element.Attribute("ContentType")?.Value, "video/mp4", StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue();
        }

        stream.Position = 0;
        var reloaded = PptxPackageReader.Read(stream);
        var reloadedCamera = reloaded.RecordingMediaArtifacts
            .Single(artifact => artifact.Kind == PresentationRecordingMediaArtifactKind.CameraVideo);

        reloadedCamera.PackagePath.Should().Be("ppt/media/freep-recordings/wpf/slide-001-camera.mp4");
        reloadedCamera.ContentType.Should().Be("video/mp4");
        reloadedCamera.ContentSha256.Should().Be(cameraArtifact.ContentSha256);
        reloadedCamera.ContentLengthBytes.Should().Be(cameraArtifact.ContentLengthBytes);
        reloadedCamera.PayloadBytes.Should().Equal(cameraArtifact.PayloadBytes);
    }

    [Fact]
    public void UnavailableFactory_PreservesNonWindowsFallbackLifecycle()
    {
        var backend = WindowsRecordingCaptureBackend.CreateUnavailable(WpfMetadata);
        var started = new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);

        backend.AdapterReadiness.Devices.Should().BeEmpty();
        backend.AdapterReadiness.CanCaptureNarration.Should().BeFalse();
        backend.AdapterReadiness.CanCaptureCamera.Should().BeFalse();

        backend.BeginCapture(new SlideShowRecordingCaptureStartRequest(
            SlideShowRecordingMediaArtifactKind.NarrationAudio,
            SlideIndex: 0,
            started,
            "slide-001-narration.wav",
            "audio/wav"));
        var result = backend.CompleteCapture(new SlideShowRecordingCaptureRequest(
            SlideShowRecordingMediaArtifactKind.NarrationAudio,
            SlideIndex: 0,
            started,
            started.AddSeconds(1),
            DurationMs: 1000,
            "slide-001-narration.wav",
            "audio/wav"));

        result.IsCaptured.Should().BeFalse();
        result.StatusText.Should().Contain(WpfMetadata.AdapterName);
        result.StatusText.Should().Contain("No Windows microphone or camera devices");
    }

    [Fact]
    public void HostMetadata_ControlsPackagePathAndCapturedResult()
    {
        var metadata = new WindowsRecordingHostMetadata(
            "Avalonia slideshow",
            "Avalonia Windows recording capture adapter",
            "ppt/media/freep-recordings/avalonia");
        var engine = new FakeCaptureEngine();
        var backend = new WindowsRecordingCaptureBackend(
            metadata,
            new FakeDeviceCatalog(
                new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Microphone,
                    "mic-0",
                    "Studio microphone",
                    IsDefault: true,
                    IsAvailable: true,
                    "audio/wav")),
            engine);
        var started = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

        backend.BeginCapture(new SlideShowRecordingCaptureStartRequest(
            SlideShowRecordingMediaArtifactKind.NarrationAudio,
            1,
            started,
            "nested\\slide-002-narration.mp4",
            "audio/wav"));
        var result = backend.CompleteCapture(new SlideShowRecordingCaptureRequest(
            SlideShowRecordingMediaArtifactKind.NarrationAudio,
            1,
            started,
            started.AddSeconds(2),
            2000,
            "nested\\slide-002-narration.mp4",
            "audio/wav"));

        engine.StartedRequests.Should().ContainSingle().Which.PackagePath
            .Should().Be("ppt/media/freep-recordings/avalonia/slide-002-narration.wav");
        result.Should().Match<SlideShowRecordingCaptureResult>(capture =>
            capture.IsCaptured &&
            capture.PackagePath == "ppt/media/freep-recordings/avalonia/slide-002-narration.wav" &&
            capture.SuggestedFileNameOverride == "slide-002-narration.wav" &&
            capture.ContentTypeOverride == "audio/wav" &&
            capture.ContentLengthBytes > 0 &&
            capture.ContentSha256.Length == 64);
    }

    [Fact]
    public void PlatformComposition_UsesSharedBackendWithWpfNativeCameraAndAvaloniaLocalAudio()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpfSource = Read(root, "freep", "FreeP.App.Host", "SlideShowWindow.cs");
        var avaloniaSource = Read(root, "freep", "FreeP.App.Avalonia", "SlideShowWindow.cs");

        foreach (var source in new[] { wpfSource, avaloniaSource })
        {
            source.Should().Contain("new WindowsRecordingCaptureBackend(");
            source.Should().Contain("new WindowsRecordingHostMetadata(");
            source.Should().NotContain("WpfWindowsRecording");
            source.Should().NotContain("AvaloniaWindowsRecording");
        }

        avaloniaSource.Should().Contain("OperatingSystem.IsLinux()");
        avaloniaSource.Should().Contain("new LinuxNarrationCaptureBackend(");
        avaloniaSource.Should().Contain("new LinuxRecordingHostMetadata(");
        avaloniaSource.Should().Contain("new WindowsNativeRecordingCaptureEngine(");
        avaloniaSource.Should().Contain("new WindowsNativeRecordingCaptureEngine(windowsMetadata.AdapterName)");
        avaloniaSource.Should().NotContain("new WindowsNativeRecordingCaptureEngine(metadata.AdapterName)");
        avaloniaSource.Should().Contain("new WindowsNativeRecordingDeviceCatalog()");
        avaloniaSource.Should().Contain("WindowsRecordingCaptureBackend.CreateUnavailable(windowsMetadata)");
        wpfSource.Should().NotContain("LinuxNarrationCaptureBackend");
        wpfSource.Should().Contain("new WindowsNativeRecordingCaptureEngine(");
        wpfSource.Should().Contain("new WindowsNativeRecordingDeviceCatalog()");

        Read(root, "freep", "FreeP.App.Host", "FreeP.App.Host.csproj")
            .Should().Contain("FreeP.App.Recording\\FreeP.App.Recording.csproj");
        Read(root, "freep", "FreeP.App.Avalonia", "FreeP.App.Avalonia.csproj")
            .Should().Contain("FreeP.App.Recording\\FreeP.App.Recording.csproj");
        Read(root, "freep", "FreeP.App.Host", "FreeP.App.Host.csproj")
            .Should().Contain("FrameworkReference Include=\"Microsoft.Windows.SDK.NET.Ref\"");
        Read(root, "freep", "FreeP.App.Avalonia", "FreeP.App.Avalonia.csproj")
            .Should().Contain("FreeP.App.Recording.Windows\\FreeP.App.Recording.Windows.csproj");
        Read(root, "freep", "FreeP.App.Recording.Windows", "FreeP.App.Recording.Windows.csproj")
            .Should().Contain("FrameworkReference Include=\"Microsoft.Windows.SDK.NET.Ref\"");
        AssertNoHostLocalRecordingSources(root, "FreeP.App.Avalonia");
        AssertNoHostLocalRecordingSources(root, "FreeP.App.Host");
    }

    [Fact]
    public void RecordingProjectBoundary_KeepsWindowsNativeCodeInWindowsAssembly()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var portableProject = Read(root, "freep", "FreeP.App.Recording", "FreeP.App.Recording.csproj");
        var windowsProject = Read(root, "freep", "FreeP.App.Recording.Windows", "FreeP.App.Recording.Windows.csproj");
        var portableSource = string.Join(
            Environment.NewLine,
            Directory.GetFiles(
                    Path.Combine(root, "freep", "FreeP.App.Recording", "Recording"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        var windowsEngineSource = Read(
            root,
            "freep",
            "FreeP.App.Recording.Windows",
            "WindowsRecordingCaptureEngine.cs");

        portableProject.Should().NotContain("FreeP.App.Recording.Windows");
        windowsProject.Should().Contain("FreeP.App.Recording\\FreeP.App.Recording.csproj");
        portableSource.Should().NotContain("winmm.dll");
        portableSource.Should().NotContain("setupapi.dll");
        portableSource.Should().NotContain("class WindowsRecordingCaptureEngine");
        portableSource.Should().NotContain("class WindowsRecordingDeviceCatalog");
        windowsEngineSource.Should().Contain("class WindowsRecordingCaptureEngine");
        windowsEngineSource.Should().Contain("mciSendStringW");
    }

    [Fact]
    public void NativeCameraEngine_DoesNotSilentlySelectAnotherCameraWhenRequestedIdentityIsGone()
    {
        var source = Read(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep", "FreeP.App.Recording.Windows", "WindowsNativeRecordingCaptureEngine.cs");

        source.Should().Contain("The requested camera '{requestedDevice.DisplayName}' is no longer available.");
        source.Should().NotContain("?? devices.FirstOrDefault();");
    }

    private static (Presentation Presentation, PresentationRecordingMediaArtifact CameraArtifact)
        BuildPresentationWithCapturedCameraArtifact(ISlideShowRecordingCaptureBackend backend)
    {
        var presentation = Presentation.CreateEmpty();
        var started = new DateTimeOffset(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);
        var plan = SlideShowPresenterToolPlanner.BuildPlan(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.NarrationAndMedia);
        var recording = SlideShowRecordingExecutionPlanner.CreateState(
            plan,
            currentSlideIndex: 0,
            started,
            backend);
        recording = SlideShowRecordingExecutionPlanner.MoveToSlide(
            recording,
            slideIndex: 1,
            started.AddMilliseconds(2400));
        var review = SlideShowRecordingReviewPlanner.BuildPlan(presentation, recording);

        SlideShowRecordingReviewPlanner.ApplyPersistableArtifacts(presentation, review);

        var cameraArtifact = presentation.RecordingMediaArtifacts
            .Single(artifact => artifact.Kind == PresentationRecordingMediaArtifactKind.CameraVideo);
        cameraArtifact.Should().Match<PresentationRecordingMediaArtifact>(artifact =>
            artifact.HasPayload &&
            artifact.SuggestedFileName == "slide-001-camera.mp4" &&
            artifact.ContentType == "video/mp4" &&
            artifact.PackagePath == "ppt/media/freep-recordings/wpf/slide-001-camera.mp4" &&
            artifact.CapturedByHost == WpfMetadata.HostName &&
            artifact.ContentSha256.Length == 64);

        return (presentation, cameraArtifact);
    }

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));

    private static void AssertNoHostLocalRecordingSources(string root, string projectName)
    {
        var recordingDirectory = Path.Combine(root, "freep", projectName, "Recording");
        if (Directory.Exists(recordingDirectory))
        {
            Directory.GetFiles(recordingDirectory, "*.cs").Should().BeEmpty();
        }
    }


    private static WindowsRecordingCaptureBackend CreateBackend(
        IWindowsRecordingDeviceCatalog deviceCatalog,
        IWindowsRecordingCaptureEngine captureEngine) =>
        new(WpfMetadata, deviceCatalog, captureEngine);

    private sealed class FakeDeviceCatalog : IWindowsRecordingDeviceCatalog
    {
        private readonly IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> _devices;

        public FakeDeviceCatalog(params SlideShowRecordingCaptureDeviceDescriptor[] devices)
        {
            _devices = devices;
        }

        public IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnumerateDevices() => _devices;
    }

    private sealed class FakeCaptureEngine : IWindowsRecordingCaptureEngine
    {
        public List<WindowsRecordingCaptureStartRequest> StartedRequests { get; } = new();

        public void BeginCapture(WindowsRecordingCaptureStartRequest request)
        {
            StartedRequests.Add(request);
        }

        public WindowsRecordingCaptureResult CompleteCapture(WindowsRecordingCaptureRequest request)
        {
            var payload = System.Text.Encoding.UTF8.GetBytes(
                $"{request.Device.Kind}|{request.Device.DeviceId}|{request.SlideIndex}|{request.DurationMs}|{request.PackagePath}");

            return WindowsRecordingCaptureResult.Captured(
                $"Fake WPF {request.Device.Kind} captured {request.PackagePath}",
                request.PackagePath,
                payload);
        }
    }

    private sealed class ThrowingDeviceCatalog : IWindowsRecordingDeviceCatalog
    {
        public IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnumerateDevices() =>
            throw new InvalidOperationException("device catalog failed");
    }
}
