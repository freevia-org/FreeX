using System.Text;
using Free.Shared.AppServices.Printing;
using FreeP.App.Compositor;

namespace FreeP.App.Recording.Tests;

public sealed class LinuxNativeOutputTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.LinuxNativeOutputTests-");

    public void Dispose() => _temporaryDirectory.Dispose();

    [Fact]
    public void Video_encoder_selection_is_shared_across_hosts()
    {
        LinuxNativeOutputCapabilityDetector.SelectSoftwareEncoder(
                " V..... mpeg4 MPEG-4 part 2\n" +
                " V....D libx264 H.264 / AVC / MPEG-4 AVC")
            .Should().Be("libx264");
    }

    [Fact]
    public void Capability_detection_reports_the_available_software_encoder()
    {
        var detector = new LinuxNativeOutputCapabilityDetector(
            new FakeExecutableLocator(
                ("ffmpeg", "/usr/bin/ffmpeg")),
            new FakeProbeRunner
            {
                EncoderOutput = " V..... libx264              libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10",
            });

        var capabilities = detector.Detect(canCaptureNarrationOverride: true);

        capabilities.Video.CanEncodeMp4.Should().BeTrue();
        capabilities.Video.EncoderName.Should().Be("libx264");
        capabilities.Video.CanCaptureNarration.Should().BeTrue();
        capabilities.Video.CanMuxTimedCaptions.Should().BeTrue();
    }

    [Fact]
    public void Recording_source_does_not_own_printer_discovery_or_submission()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Recording",
            "Recording",
            "LinuxNativeOutput.cs"));

        source.Should().NotContain("LinuxNativePrint")
            .And.NotContain("ILinuxNativePrintHandoffAdapter")
            .And.NotContain("lpstat")
            .And.NotContain("BuildLpArguments")
            .And.NotContain("BuildLprArguments");
    }

    [Fact]
    public async Task Video_adapter_rejects_invalid_encoder_package_without_creating_output()
    {
        var adapter = new LinuxVideoExportAdapter(
            new LinuxVideoEncoderCapability(true, "/usr/bin/ffmpeg", "libx264", false, "ready"));
        var exportPlan = FreeP.App.Compositor.PresentationExportPlanner.BuildVideoExportPlan(
            new FreeP.App.Compositor.PresentationVideoExportRequest(),
            0);
        var package = new FreeP.App.Compositor.PresentationVideoFramePackage(
            new FreeP.App.Compositor.PresentationVideoFramePackagePlan(
                exportPlan,
                "application/zip",
                ".zip",
                false,
                [],
                "no frames"),
            [],
            []);

        var output = Path.Combine(_temporaryDirectory.Path, "invalid-print.mp4");
        var result = await adapter.ExportAsync(package, output);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be("no frames");
        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public async Task Linux_ffmpeg_export_uses_named_zip_frames_and_produces_a_real_mp4_when_available()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var ffmpeg = new PathLinuxRecordingExecutableLocator().FindExecutable("ffmpeg");
        if (ffmpeg is null)
            return;

        var presentation = FreeP.Core.Model.Presentation.CreateEmpty();
        var package = FreeP.App.Compositor.PresentationVideoFramePackageExecutor.BuildPackage(
            presentation,
            new FreeP.App.Compositor.PresentationVideoExportRequest(
                Quality: FreeP.App.Compositor.PresentationVideoQualityKind.Standard,
                SecondsPerSlide: 0.2,
                IncludeNarration: false),
            static (_, _, _, _) => EvenTwoByTwoPng);

        package.Frames.Select(frame => frame.FileName)
            .Should()
            .Equal("frames/slide-01-frame-0001.png");

        var output = Path.Combine(_temporaryDirectory.Path, "real-video.mp4");
        try
        {
            var result = await new LinuxVideoExportAdapter(
                new LinuxVideoEncoderCapability(true, ffmpeg, "mpeg4", false, "ready"))
                .ExportAsync(package, output);

            result.Succeeded.Should().BeTrue(result.FailureReason);
            result.ByteCount.Should().BeGreaterThan(0);
            File.ReadAllBytes(output).Should().ContainInOrder((byte)'f', (byte)'t', (byte)'y', (byte)'p');
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [Fact]
    public async Task Linux_ffmpeg_export_muxes_persisted_narration_at_its_slide_start_time()
    {
        var output = Path.Combine(_temporaryDirectory.Path, "narrated-video.mp4");
        var runner = new CapturingVideoProcessRunner();
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
            PackagePath: "ppt/media/freep-recordings/avalonia/slide-1.wav",
            ContentLengthBytes: narrationBytes.Length,
            ContentSha256: "test-sha",
            DurationMs: 200,
            CapturedByHost: "test",
            StatusText: "captured",
            PayloadBytes: narrationBytes);

        try
        {
            var result = await new LinuxVideoExportAdapter(
                new LinuxVideoEncoderCapability(true, "ffmpeg", "libx264", true, "ready"),
                runner)
                .ExportAsync(
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
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [Fact]
    public async Task Linux_ffmpeg_export_muxes_persisted_caption_as_timed_mov_text()
    {
        var output = Path.Combine(_temporaryDirectory.Path, "captioned-video.mp4");
        var runner = new CapturingVideoProcessRunner();
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Add(new Slide
        {
            LayoutId = presentation.Layouts[0].Id,
            Title = "Slide 2",
        });
        var captionBytes = Encoding.UTF8.GetBytes(
            "WEBVTT\r\n\r\n00:00.000 --> 00:00.200\r\nSlide 2 narration\r\n");
        var artifact = new PresentationRecordingMediaArtifact(
            PresentationRecordingMediaArtifactKind.NarrationCaption,
            SlideIndex: 1,
            SuggestedFileName: "slide-2-narration-captions.vtt",
            ContentType: "text/vtt",
            PackagePath: "ppt/media/recording-captions/slide-2-narration-captions.vtt",
            ContentLengthBytes: captionBytes.Length,
            ContentSha256: "caption-sha",
            DurationMs: 200,
            CapturedByHost: "test",
            StatusText: "captured",
            PayloadBytes: captionBytes);

        try
        {
            var result = await new LinuxVideoExportAdapter(
                new LinuxVideoEncoderCapability(true, "ffmpeg", "libx264", true, "ready"),
                runner)
                .ExportAsync(
                    BuildPackage(presentation, includeNarration: true),
                    output,
                    CancellationToken.None,
                    [artifact]);

            result.Succeeded.Should().BeTrue(result.FailureReason);
            result.MuxedCaptionTrackCount.Should().Be(1);
            result.StatusText.Should().Contain("caption");
            runner.Arguments.Should().ContainInOrder(
                "-itsoffset", "1", "-i");
            runner.Arguments.Should().Contain(argument =>
                argument.EndsWith("caption-0000.vtt", StringComparison.OrdinalIgnoreCase));
            runner.Arguments.Should().ContainInOrder("-map", "1:0", "-c:s", "mov_text");
            runner.Arguments.Should().NotContain("-filter_complex");
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [Fact]
    public async Task Linux_ffmpeg_export_muxes_persisted_camera_as_timed_picture_in_picture()
    {
        var output = Path.Combine(_temporaryDirectory.Path, "camera-video.mp4");
        var runner = new CapturingVideoProcessRunner();
        var presentation = FreeP.Core.Model.Presentation.CreateEmpty();
        presentation.Slides.Add(new FreeP.Core.Model.Slide
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
            PackagePath: "ppt/media/freep-recordings/avalonia/slide-1-camera.mp4",
            ContentLengthBytes: cameraBytes.Length,
            ContentSha256: "camera-sha",
            DurationMs: 200,
            CapturedByHost: "test",
            StatusText: "captured",
            PayloadBytes: cameraBytes);

        try
        {
            var result = await new LinuxVideoExportAdapter(
                new LinuxVideoEncoderCapability(true, "ffmpeg", "libx264", true, "ready"),
                runner)
                .ExportAsync(
                    BuildPackage(presentation, includeNarration: false),
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
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [Fact]
    public async Task Video_export_cancellation_preserves_existing_output_after_runner_is_cancelled()
    {
        var output = Path.Combine(_temporaryDirectory.Path, "cancelled-video.mp4");
        await File.WriteAllTextAsync(output, "existing video");
        var runner = new BlockingProcessRunner();
        using var cts = new CancellationTokenSource();
        var package = FreeP.App.Compositor.PresentationVideoFramePackageExecutor.BuildPackage(
            FreeP.Core.Model.Presentation.CreateEmpty(),
            new FreeP.App.Compositor.PresentationVideoExportRequest(
                Quality: FreeP.App.Compositor.PresentationVideoQualityKind.Standard,
                SecondsPerSlide: 0.2,
                IncludeNarration: false),
            static (_, _, _, _) => EvenTwoByTwoPng);

        var exportTask = new LinuxVideoExportAdapter(
            new LinuxVideoEncoderCapability(true, "ffmpeg", "mpeg4", false, "ready"),
            runner)
            .ExportAsync(package, output, cts.Token);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();
        var result = await exportTask;

        result.Succeeded.Should().BeFalse();
        result.Canceled.Should().BeTrue();
        runner.CancellationObserved.Should().BeTrue();
        File.ReadAllText(output).Should().Be("existing video");
    }

    [Fact]
    public async Task Video_export_deletes_output_when_encoder_returns_invalid_bytes()
    {
        var output = Path.Combine(_temporaryDirectory.Path, "invalid-video.mp4");
        var package = FreeP.App.Compositor.PresentationVideoFramePackageExecutor.BuildPackage(
            FreeP.Core.Model.Presentation.CreateEmpty(),
            new FreeP.App.Compositor.PresentationVideoExportRequest(
                Quality: FreeP.App.Compositor.PresentationVideoQualityKind.Standard,
                SecondsPerSlide: 0.2,
                IncludeNarration: false),
            static (_, _, _, _) => EvenTwoByTwoPng);

        var result = await new LinuxVideoExportAdapter(
            new LinuxVideoEncoderCapability(true, "ffmpeg", "mpeg4", false, "ready"),
            new InvalidOutputProcessRunner())
            .ExportAsync(package, output);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("valid non-empty MP4");
        File.Exists(output).Should().BeFalse();
    }

    private static readonly byte[] EvenTwoByTwoPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAB0lEQVRj+M/AAEMAzJWb4gAAAABJRU5ErkJggg==");

    private static PresentationVideoFramePackage BuildPackage(
        Presentation presentation,
        bool includeNarration) =>
        PresentationVideoFramePackageExecutor.BuildPackage(
            presentation,
            new PresentationVideoExportRequest(
                Quality: PresentationVideoQualityKind.Standard,
                SecondsPerSlide: 0.2,
                IncludeNarration: includeNarration),
            static (_, _, _, _) => EvenTwoByTwoPng);

    private sealed class BlockingProcessRunner : IProcessRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public async Task<ProcessResult> RunAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new ProcessResult(0, string.Empty, string.Empty);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                await File.WriteAllTextAsync(invocation.Arguments[^1], "partial-after-kill");
                throw;
            }
        }
    }

    private sealed class InvalidOutputProcessRunner : IProcessRunner
    {
        public async Task<ProcessResult> RunAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            await File.WriteAllTextAsync(invocation.Arguments[^1], "not an mp4", cancellationToken);
            return new ProcessResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class CapturingVideoProcessRunner : IProcessRunner
    {
        public List<string> Arguments { get; } = [];

        public Task<ProcessResult> RunAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Arguments.AddRange(invocation.Arguments);
            File.WriteAllBytes(invocation.Arguments[^1], Encoding.ASCII.GetBytes("0000ftyp0000moov0000mdat"));
            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
        }
    }

    private sealed class FakeExecutableLocator(params (string Name, string Path)[] values)
        : ILinuxRecordingExecutableLocator
    {
        private readonly IReadOnlyDictionary<string, string> _values =
            values.ToDictionary(value => value.Name, value => value.Path, StringComparer.Ordinal);

        public string? FindExecutable(string executableName) =>
            _values.TryGetValue(executableName, out var path) ? path : null;
    }

    private sealed class FakeProbeRunner : ILinuxRecordingProbeRunner
    {
        public string EncoderOutput { get; init; } = string.Empty;

        public LinuxRecordingProbeResult Run(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout)
        {
            return new LinuxRecordingProbeResult(0, EncoderOutput, string.Empty);
        }
    }
}
