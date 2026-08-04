using System.IO;
using System.Text;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.App.Recording;
using FreeP.App.Recording.Windows;

namespace FreeP.App.Host.Tests;

public sealed class WpfVideoExportAdapterTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "FreeP.WpfVideoExportTests", Guid.NewGuid().ToString("N"));

    public WpfVideoExportAdapterTests() => Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        try { Directory.Delete(_tempDirectory, recursive: true); } catch { }
    }

    [Fact]
    public void EncoderProbe_SelectsPreferredSoftwareEncoder()
    {
        WpfVideoEncoderCapabilityDetector.SelectSoftwareEncoder(
                " V....D libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10\n" +
                " V..... mpeg4 MPEG-4 part 2")
            .Should().Be("libx264");
    }

    [Fact]
    public void WindowsNativeCapability_AdvertisesItsPersistedMediaTracks()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var capability = WpfVideoEncoderCapabilityDetector.Detect();
        var devices = new WindowsNativeRecordingDeviceCatalog().EnumerateDevices();

        capability.ExecutablePath.Should().Be(WindowsNativeVideoExportAdapter.ExecutablePath);
        capability.CanCaptureNarration.Should().Be(devices.Any(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Microphone && device.IsAvailable));
        capability.CanCaptureCameraAndMedia.Should().Be(devices.Any(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Camera && device.IsAvailable));
        capability.Reason.Should().Contain("narration");
    }

    [Fact]
    public void WindowsNativeCapability_ReportsOnlyInjectedCaptureDevices()
    {
        var capability = WpfVideoEncoderCapabilityDetector.DetectWindowsCaptureCapability(
            new FakeRecordingDeviceCatalog(
                new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Microphone,
                    "mic-0",
                    "Studio microphone",
                    IsDefault: true,
                    IsAvailable: true,
                    "audio/wav")));

        capability.CanEncodeMp4.Should().BeTrue();
        capability.CanCaptureNarration.Should().BeTrue();
        capability.CanCaptureCameraAndMedia.Should().BeFalse();
        capability.Reason.Should().Contain("narration capture");
        capability.Reason.Should().Contain("no camera device");
    }

    [Fact]
    public async Task Export_ExtractsFramesAndAcceptsValidMp4Output()
    {
        var output = Path.Combine(_tempDirectory, "deck.mp4");
        var runner = new SuccessfulVideoProcessRunner(output);
        var adapter = new WpfVideoExportAdapter(
            new WpfVideoEncoderCapability(true, "ffmpeg.exe", "libx264", "ready"),
            runner);

        var result = await adapter.ExportAsync(BuildPackage(), output);

        result.Succeeded.Should().BeTrue(result.FailureReason);
        result.EncoderName.Should().Be("libx264");
        result.ByteCount.Should().BeGreaterThan(0);
        runner.Arguments.Should().ContainInOrder("-f", "concat", "-safe", "0", "-c:v", "libx264");
        runner.Arguments.Should().Contain(argument => argument.EndsWith("frames.txt", StringComparison.OrdinalIgnoreCase));
        File.Exists(output).Should().BeTrue();
    }

    [Fact]
    public async Task Export_RemovesInvalidEncoderOutput()
    {
        var output = Path.Combine(_tempDirectory, "invalid.mp4");
        var adapter = new WpfVideoExportAdapter(
            new WpfVideoEncoderCapability(true, "ffmpeg.exe", "mpeg4", "ready"),
            new InvalidVideoProcessRunner(output));

        var result = await adapter.ExportAsync(BuildPackage(), output);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("valid non-empty MP4");
        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public async Task Export_MuxesPersistedNarrationAtItsSlideStartTime()
    {
        var output = Path.Combine(_tempDirectory, "narrated.mp4");
        var runner = new SuccessfulVideoProcessRunner(output);
        var adapter = new WpfVideoExportAdapter(
            new WpfVideoEncoderCapability(true, "ffmpeg.exe", "libx264", "ready"),
            runner);
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Add(new Slide
        {
            LayoutId = presentation.Layouts[0].Id,
            Title = "Slide 2",
        });
        var narrationBytes = Encoding.ASCII.GetBytes("test narration payload");
        var artifact = new PresentationRecordingMediaArtifact(
            PresentationRecordingMediaArtifactKind.NarrationAudio,
            SlideIndex: 1,
            SuggestedFileName: "slide-2.wav",
            ContentType: "audio/wav",
            PackagePath: "ppt/media/freep-recordings/wpf/slide-1.wav",
            ContentLengthBytes: narrationBytes.Length,
            ContentSha256: "test-sha",
            DurationMs: 200,
            CapturedByHost: "test",
            StatusText: "captured",
            PayloadBytes: narrationBytes);

        var result = await adapter.ExportAsync(
            BuildPackage(presentation, includeNarration: true),
            output,
            CancellationToken.None,
            [artifact]);

        result.Succeeded.Should().BeTrue(result.FailureReason);
        result.MuxedNarrationTrackCount.Should().Be(1);
        result.StatusText.Should().Contain("narration");
        runner.Arguments.Should().Contain("-filter_complex");
        runner.Arguments.Should().Contain(argument => argument.Contains("adelay=1000:all=1", StringComparison.Ordinal));
        runner.Arguments.Should().ContainInOrder("-map", "0:v:0", "-map", "[aout]");
        runner.Arguments.Should().NotContain("-an");
    }

    [Fact]
    public async Task Export_MuxesPersistedCameraAsTimedPictureInPicture()
    {
        var output = Path.Combine(_tempDirectory, "camera.mp4");
        var runner = new SuccessfulVideoProcessRunner(output);
        var adapter = new WpfVideoExportAdapter(
            new WpfVideoEncoderCapability(true, "ffmpeg.exe", "libx264", "ready"),
            runner);
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Add(new Slide
        {
            LayoutId = presentation.Layouts[0].Id,
            Title = "Slide 2",
        });
        var cameraBytes = Encoding.ASCII.GetBytes("camera ftyp moov mdat payload");
        var artifact = new PresentationRecordingMediaArtifact(
            PresentationRecordingMediaArtifactKind.CameraVideo,
            SlideIndex: 1,
            SuggestedFileName: "slide-2-camera.mp4",
            ContentType: "video/mp4",
            PackagePath: "ppt/media/freep-recordings/wpf/slide-1-camera.mp4",
            ContentLengthBytes: cameraBytes.Length,
            ContentSha256: "camera-sha",
            DurationMs: 200,
            CapturedByHost: "test",
            StatusText: "captured",
            PayloadBytes: cameraBytes);

        var result = await adapter.ExportAsync(
            BuildPackage(presentation),
            output,
            CancellationToken.None,
            [artifact]);

        result.Succeeded.Should().BeTrue(result.FailureReason);
        result.MuxedCameraTrackCount.Should().Be(1);
        result.StatusText.Should().Contain("camera");
        runner.Arguments.Should().Contain(argument =>
            argument.EndsWith("camera-0000.mp4", StringComparison.OrdinalIgnoreCase));
        runner.Arguments.Should().Contain(argument =>
            argument.Contains("[1:v]setpts=PTS-STARTPTS,trim=duration=0.2,setpts=PTS+1/TB", StringComparison.Ordinal));
        runner.Arguments.Should().Contain(argument =>
            argument.Contains("overlay=x=main_w-overlay_w-32:y=main_h-overlay_h-32", StringComparison.Ordinal));
        runner.Arguments.Should().ContainInOrder("-map", "[vout]");
        runner.Arguments.Should().Contain("-an");
    }

    private static PresentationVideoFramePackage BuildPackage(bool includeNarration = false) =>
        BuildPackage(Presentation.CreateEmpty(), includeNarration);

    private static PresentationVideoFramePackage BuildPackage(
        Presentation presentation,
        bool includeNarration = false) =>
        PresentationVideoFramePackageExecutor.BuildPackage(
            presentation,
            new PresentationVideoExportRequest(
                Quality: PresentationVideoQualityKind.Standard,
                SecondsPerSlide: 0.2,
                IncludeNarration: includeNarration),
            static (_, _, _, _) => EvenTwoByTwoPng);

    private static readonly byte[] EvenTwoByTwoPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAB0lEQVRj+M/AAEMAzJWb4gAAAABJRU5ErkJggg==");

    private sealed class SuccessfulVideoProcessRunner(string outputPath) : IWpfVideoProcessRunner
    {
        public List<string> Arguments { get; } = [];

        public Task<WpfVideoProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Arguments.AddRange(arguments);
            File.WriteAllBytes(outputPath, Encoding.ASCII.GetBytes("0000ftyp0000moov0000mdat"));
            return Task.FromResult(new WpfVideoProcessResult(0, string.Empty, string.Empty, false));
        }
    }

    private sealed class InvalidVideoProcessRunner(string outputPath) : IWpfVideoProcessRunner
    {
        public Task<WpfVideoProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            File.WriteAllText(outputPath, "not an mp4");
            return Task.FromResult(new WpfVideoProcessResult(0, string.Empty, string.Empty, false));
        }
    }

    private sealed class FakeRecordingDeviceCatalog(
        params SlideShowRecordingCaptureDeviceDescriptor[] devices) : IWindowsRecordingDeviceCatalog
    {
        public IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnumerateDevices() => devices;
    }
}
