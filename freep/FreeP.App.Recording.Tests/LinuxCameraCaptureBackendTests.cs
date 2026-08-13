using FreeP.App.Compositor;
using FreeP.App.Recording;

namespace FreeP.App.Recording.Tests;

public sealed class LinuxCameraCaptureBackendTests
{
    [Fact]
    public void Discovery_WithFfmpegAndVideoDevice_ExposesCameraAndBuildsV4l2Command()
    {
        using var temp = new TestTemporaryDirectory("freep-linux-camera-tests-");
        var devicePath = Path.Combine(temp.Path, "video0");
        File.WriteAllText(devicePath, string.Empty);
        var probe = new FakeProbeRunner();

        var discovery = new LinuxCameraDeviceCatalog(
            new FakeExecutableLocator(("ffmpeg", "/usr/bin/ffmpeg")),
            probe,
            () => [devicePath])
            .Discover();

        discovery.IsAvailable.Should().BeTrue(discovery.UnavailableReason);
        discovery.Devices.Should().ContainSingle().Which.Should().Match<SlideShowRecordingCaptureDeviceDescriptor>(
            device => device.Kind == SlideShowRecordingCaptureDeviceKind.Camera &&
                device.IsDefault &&
                device.ContentType == "video/mp4" &&
                device.DeviceId == Path.GetFullPath(devicePath));

        var command = LinuxCameraCapturePlanner.BuildCaptureCommand(
            discovery.Tool!,
            discovery.Devices.Single(),
            Path.Combine(temp.Path, "capture.mp4"));

        command.ToolKind.Should().Be(LinuxNarrationCaptureToolKind.FfmpegCamera);
        command.Arguments.Should().ContainInOrder(
            "-f", "v4l2", "-framerate", "30", "-video_size", "1280x720", "-i", devicePath);
        command.Arguments.Should().ContainInOrder("-c:v", "libx264", "-pix_fmt", "yuv420p");
        probe.Invocations.Should().ContainSingle();
    }

    [Fact]
    public void CompleteCapture_StopsRecorderAndReturnsPersistableMp4Payload()
    {
        using var temp = new TestTemporaryDirectory("freep-linux-camera-tests-");
        var devicePath = Path.Combine(temp.Path, "video0");
        File.WriteAllText(devicePath, string.Empty);
        var processAdapter = new FakeProcessAdapter { PayloadOnStop = Mp4Payload() };
        using var backend = new LinuxCameraCaptureBackend(
            Metadata(temp.Path),
            new FakeCameraDeviceCatalog(devicePath),
            processAdapter);
        var started = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

        backend.BeginCapture(StartRequest(2, started));
        var result = backend.CompleteCapture(CompleteRequest(2, started));

        backend.AdapterReadiness.CanCaptureCamera.Should().BeTrue();
        backend.Capabilities.CanCaptureCamera.Should().BeTrue();
        processAdapter.Commands.Should().ContainSingle(command =>
            command.ToolKind == LinuxNarrationCaptureToolKind.FfmpegCamera &&
            command.OutputPath.EndsWith(".mp4", StringComparison.Ordinal));
        processAdapter.StopCount.Should().Be(1);
        result.Should().Match<SlideShowRecordingCaptureResult>(capture =>
            capture.IsCaptured &&
            !capture.IsDeferred &&
            capture.PackagePath == "ppt/media/freep-recordings/avalonia/slide-003-camera.mp4" &&
            capture.SuggestedFileNameOverride == "slide-003-camera.mp4" &&
            capture.ContentTypeOverride == "video/mp4" &&
            capture.ContentLengthBytes == Mp4Payload().Length &&
            capture.ContentSha256.Length == 64);
        result.PayloadBytes.Should().NotBeNull();
        File.Exists(processAdapter.Commands[0].OutputPath).Should().BeFalse(
            "temporary camera files must be cleaned after materialization");
    }

    [Fact]
    public void CompositeBackend_ExposesNarrationAndCameraStreamsTogether()
    {
        using var temp = new TestTemporaryDirectory("freep-linux-camera-tests-");
        var devicePath = Path.Combine(temp.Path, "video0");
        File.WriteAllText(devicePath, string.Empty);
        using var narration = new LinuxNarrationCaptureBackend(
            Metadata(temp.Path),
            new FakeNarrationDeviceCatalog(),
            new FakeProcessAdapter());
        using var camera = new LinuxCameraCaptureBackend(
            Metadata(temp.Path),
            new FakeCameraDeviceCatalog(devicePath),
            new FakeProcessAdapter());
        using var composite = new LinuxRecordingCaptureBackend(narration, camera);

        composite.AdapterReadiness.ReadyStreams.Should().ContainInOrder(
            SlideShowRecordingCaptureStreamKind.NarrationAudio,
            SlideShowRecordingCaptureStreamKind.CameraVideo);
        composite.Capabilities.CanCaptureNarration.Should().BeTrue();
        composite.Capabilities.CanCaptureCamera.Should().BeTrue();
    }

    private static LinuxRecordingHostMetadata Metadata(string tempPath) =>
        new(
            "Avalonia slideshow",
            "Avalonia Linux recording capture adapter",
            "ppt/media/freep-recordings/avalonia",
            TemporaryDirectory: tempPath);

    private static SlideShowRecordingCaptureStartRequest StartRequest(
        int slideIndex,
        DateTimeOffset started) =>
        new(
            SlideShowRecordingMediaArtifactKind.CameraVideo,
            slideIndex,
            started,
            $"slide-{slideIndex + 1:D3}-camera.mp4",
            "video/mp4");

    private static SlideShowRecordingCaptureRequest CompleteRequest(
        int slideIndex,
        DateTimeOffset started) =>
        new(
            SlideShowRecordingMediaArtifactKind.CameraVideo,
            slideIndex,
            started,
            started.AddSeconds(2),
            2000,
            $"slide-{slideIndex + 1:D3}-camera.mp4",
            "video/mp4");

    private static byte[] Mp4Payload() =>
        "0000ftyp0000moov0000mdat"u8.ToArray();

    private sealed class FakeCameraDeviceCatalog(string devicePath) : ILinuxCameraDeviceCatalog
    {
        public LinuxCameraCaptureDiscovery Discover() =>
            new(
                new LinuxCameraCaptureTool("/usr/bin/ffmpeg", "libx264", "fake ffmpeg"),
                [new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Camera,
                    devicePath,
                    "Camera 1",
                    IsDefault: true,
                    IsAvailable: true,
                    "video/mp4")],
                string.Empty);
    }

    private sealed class FakeNarrationDeviceCatalog : ILinuxRecordingDeviceCatalog
    {
        public LinuxNarrationCaptureDiscovery Discover() =>
            new(
                new LinuxNarrationCaptureTool(
                    LinuxNarrationCaptureToolKind.PipeWire,
                    "/usr/bin/pw-record",
                    "fake PipeWire"),
                [new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Microphone,
                    "mic-1",
                    "Microphone",
                    IsDefault: true,
                    IsAvailable: true,
                    "audio/wav")],
                string.Empty);
    }

    private sealed class FakeExecutableLocator(params (string Name, string Path)[] executables)
        : ILinuxRecordingExecutableLocator
    {
        private readonly IReadOnlyDictionary<string, string> _executables =
            executables.ToDictionary(item => item.Name, item => item.Path, StringComparer.Ordinal);

        public string? FindExecutable(string executableName) =>
            _executables.GetValueOrDefault(executableName);
    }

    private sealed class FakeProbeRunner : ILinuxRecordingProbeRunner
    {
        public List<string> Invocations { get; } = [];

        public LinuxRecordingProbeResult Run(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout)
        {
            Invocations.Add(fileName + " " + string.Join(' ', arguments));
            return new LinuxRecordingProbeResult(
                0,
                " V..... libx264              H.264 / AVC",
                string.Empty);
        }
    }

    private sealed class FakeProcessAdapter : ILinuxRecordingProcessAdapter
    {
        private readonly Dictionary<ILinuxRecordingChildProcess, LinuxNarrationCaptureCommand> _commands = [];

        public List<LinuxNarrationCaptureCommand> Commands { get; } = [];
        public byte[]? PayloadOnStop { get; init; }
        public int StopCount { get; private set; }

        public ILinuxRecordingChildProcess Start(LinuxNarrationCaptureCommand command)
        {
            Commands.Add(command);
            var process = new FakeProcess();
            _commands[process] = command;
            return process;
        }

        public LinuxRecordingProcessStopResult Stop(
            ILinuxRecordingChildProcess process,
            TimeSpan gracefulTimeout)
        {
            StopCount++;
            if (PayloadOnStop is not null)
                File.WriteAllBytes(_commands[process].OutputPath, PayloadOnStop);
            ((FakeProcess)process).MarkExited(0);
            return new LinuxRecordingProcessStopResult(true, false, 0, string.Empty);
        }

        public void Cancel(ILinuxRecordingChildProcess process, TimeSpan gracefulTimeout) =>
            ((FakeProcess)process).MarkExited(130);
    }

    private sealed class FakeProcess : ILinuxRecordingChildProcess
    {
        private bool _hasExited;
        private int? _exitCode;

        public int ProcessId => 42;
        public bool HasExited => _hasExited;
        public int? ExitCode => _exitCode;
        public string StandardError => string.Empty;

        public bool WaitForExit(TimeSpan timeout) => _hasExited;
        public void SendInterrupt() { }
        public void Kill() => MarkExited(137);
        public void MarkExited(int exitCode)
        {
            _hasExited = true;
            _exitCode = exitCode;
        }
        public void Dispose() { }
    }

}
