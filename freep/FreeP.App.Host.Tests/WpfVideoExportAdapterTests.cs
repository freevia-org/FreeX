using System.IO;
using System.Text;
using Free.Shared.AppServices.Printing;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.App.Recording;
using FreeP.App.Recording.Windows;

namespace FreeP.App.Host.Tests;

public sealed class RecordingVideoExportAdapterTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.WpfVideoExportTests-");
    private string _tempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [Fact]
    public void EncoderProbe_SelectsPreferredSoftwareEncoder()
    {
        LinuxNativeOutputCapabilityDetector.SelectSoftwareEncoder(
                " V....D libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10\n" +
                " V..... mpeg4 MPEG-4 part 2")
            .Should().Be("libx264");
    }

    [Fact]
    public void WindowsNativeCapability_AdvertisesItsPersistedMediaTracks()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var capability = WindowsNativePrintOutput.Detect().Video;
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
        var capability = WindowsNativePrintOutput.DetectWindowsVideoCapability(
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
        capability.CanMuxTimedCaptions.Should().Be(WindowsNativeVideoExportAdapter.CanUseCaptionFallback);
    }

    [Fact]
    public async Task Export_ExtractsFramesAndAcceptsValidMp4Output()
    {
        var output = Path.Combine(_tempDirectory, "deck.mp4");
        var runner = new SuccessfulVideoProcessRunner();
        var adapter = new LinuxVideoExportAdapter(
            new LinuxVideoEncoderCapability(true, "ffmpeg.exe", "libx264", false, "ready"),
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
        var adapter = new LinuxVideoExportAdapter(
            new LinuxVideoEncoderCapability(true, "ffmpeg.exe", "mpeg4", false, "ready"),
            new InvalidVideoProcessRunner());

        var result = await adapter.ExportAsync(BuildPackage(), output);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("valid non-empty MP4");
        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public async Task Export_MuxesPersistedNarrationAtItsSlideStartTime()
    {
        var output = Path.Combine(_tempDirectory, "narrated.mp4");
        var runner = new SuccessfulVideoProcessRunner();
        var adapter = new LinuxVideoExportAdapter(
            new LinuxVideoEncoderCapability(true, "ffmpeg.exe", "libx264", false, "ready"),
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
        var runner = new SuccessfulVideoProcessRunner();
        var adapter = new LinuxVideoExportAdapter(
            new LinuxVideoEncoderCapability(true, "ffmpeg.exe", "libx264", false, "ready"),
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

    [Fact]
    public async Task WindowsNativeExport_ForwardsPersistedCaptionsToTheTimedTextFallback()
    {
        var output = Path.Combine(_tempDirectory, "captioned-native.mp4");
        var fallback = new CaptionFallback();
        var adapter = new WindowsNativeVideoExportAdapter(
            new LinuxVideoEncoderCapability(
                CanEncodeMp4: true,
                ExecutablePath: WindowsNativeVideoExportAdapter.ExecutablePath,
                EncoderName: "Windows MediaComposition",
                CanCaptureNarration: false,
                Reason: "test"),
            captionFallback: fallback,
            captionFallbackFactory: static () => null);
        var captionBytes = Encoding.UTF8.GetBytes(
            "WEBVTT\n\n00:00:00.000 --> 00:00:00.500\nHello\n");
        var artifact = new PresentationRecordingMediaArtifact(
            PresentationRecordingMediaArtifactKind.NarrationCaption,
            SlideIndex: 0,
            SuggestedFileName: "slide-1-narration-captions.vtt",
            ContentType: "text/vtt",
            PackagePath: "ppt/media/recording-captions/slide-1-narration-captions.vtt",
            ContentLengthBytes: captionBytes.Length,
            ContentSha256: "caption-sha",
            DurationMs: 500,
            CapturedByHost: "test",
            StatusText: "captured",
            PayloadBytes: captionBytes);

        var result = await adapter.ExportAsync(
            BuildPackage(includeNarration: true),
            output,
            CancellationToken.None,
            [artifact]);

        result.Succeeded.Should().BeTrue(result.FailureReason);
        result.MuxedCaptionTrackCount.Should().Be(1);
        fallback.Artifacts.Should().ContainSingle().Which.Kind
            .Should().Be(PresentationRecordingMediaArtifactKind.NarrationCaption);
    }

    [Fact]
    public async Task WindowsNativeExport_ReportsCaptionCapabilityBoundaryInsteadOfDroppingTracks()
    {
        var adapter = new WindowsNativeVideoExportAdapter(
            new LinuxVideoEncoderCapability(
                CanEncodeMp4: true,
                ExecutablePath: WindowsNativeVideoExportAdapter.ExecutablePath,
                EncoderName: "Windows MediaComposition",
                CanCaptureNarration: false,
                Reason: "test"),
            captionFallbackFactory: static () => null);
        var captionBytes = Encoding.UTF8.GetBytes(
            "WEBVTT\n\n00:00:00.000 --> 00:00:00.500\nHello\n");
        var artifact = new PresentationRecordingMediaArtifact(
            PresentationRecordingMediaArtifactKind.NarrationCaption,
            SlideIndex: 0,
            SuggestedFileName: "slide-1-narration-captions.vtt",
            ContentType: "text/vtt",
            PackagePath: "ppt/media/recording-captions/slide-1-narration-captions.vtt",
            ContentLengthBytes: captionBytes.Length,
            ContentSha256: "caption-sha",
            DurationMs: 500,
            CapturedByHost: "test",
            StatusText: "captured",
            PayloadBytes: captionBytes);

        var result = await adapter.ExportAsync(
            BuildPackage(includeNarration: true),
            Path.Combine(_tempDirectory, "captioned-unsupported.mp4"),
            CancellationToken.None,
            [artifact]);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("cannot mux timed caption tracks");
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

    private sealed class SuccessfulVideoProcessRunner : IProcessRunner
    {
        public List<string> Arguments { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Arguments.AddRange(invocation.Arguments);
            File.WriteAllBytes(
                invocation.Arguments[^1],
                Encoding.ASCII.GetBytes("0000ftyp0000moov0000mdat"));
            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
        }
    }

    private sealed class InvalidVideoProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            File.WriteAllText(invocation.Arguments[^1], "not an mp4");
            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
        }
    }

    private sealed class CaptionFallback : ILinuxVideoExportAdapter
    {
        public LinuxVideoEncoderCapability Capability =>
            new(true, "ffmpeg.exe", "mpeg4", false, "test caption fallback");

        public List<PresentationRecordingMediaArtifact> Artifacts { get; } = [];

        public Task<LinuxVideoExportResult> ExportAsync(
            PresentationVideoFramePackage package,
            string outputPath,
            CancellationToken cancellationToken = default,
            IReadOnlyList<PresentationRecordingMediaArtifact>? mediaArtifacts = null)
        {
            Artifacts.AddRange(mediaArtifacts ?? []);
            File.WriteAllBytes(outputPath, Encoding.ASCII.GetBytes("0000ftyp0000moov0000mdat"));
            return Task.FromResult(LinuxVideoExportResult.Success(
                outputPath,
                "mpeg4",
                new FileInfo(outputPath).Length,
                muxedCaptionTrackCount: Artifacts.Count));
        }
    }

    private sealed class FakeRecordingDeviceCatalog(
        params SlideShowRecordingCaptureDeviceDescriptor[] devices) : IWindowsRecordingDeviceCatalog
    {
        public IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnumerateDevices() => devices;
    }
}
