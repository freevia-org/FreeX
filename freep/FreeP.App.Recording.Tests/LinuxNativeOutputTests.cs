using System.Text;

namespace FreeP.App.Recording.Tests;

public sealed class LinuxNativeOutputTests
{
    [Fact]
    public void Capability_detection_requires_a_real_queue_and_software_encoder()
    {
        var detector = new LinuxNativeOutputCapabilityDetector(
            new FakeExecutableLocator(
                ("lp", "/usr/bin/lp"),
                ("lpstat", "/usr/bin/lpstat"),
                ("ffmpeg", "/usr/bin/ffmpeg")),
            new FakeProbeRunner
            {
                DefaultPrinterOutput = "system default destination: office",
                EncoderOutput = " V..... libx264              libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10",
            });

        var capabilities = detector.Detect(canCaptureNarrationOverride: true);

        capabilities.Print.CanPrint.Should().BeTrue();
        capabilities.Print.PrinterName.Should().Be("office");
        capabilities.Video.CanEncodeMp4.Should().BeTrue();
        capabilities.Video.EncoderName.Should().Be("libx264");
        capabilities.Video.CanCaptureNarration.Should().BeTrue();
    }

    [Fact]
    public void Capability_detection_does_not_claim_printing_without_a_cups_queue()
    {
        var detector = new LinuxNativeOutputCapabilityDetector(
            new FakeExecutableLocator(
                ("lp", "/usr/bin/lp"),
                ("lpstat", "/usr/bin/lpstat"),
                ("ffmpeg", "/usr/bin/ffmpeg")),
            new FakeProbeRunner
            {
                DefaultPrinterOutput = "no system default destination",
                QueueOutput = string.Empty,
                EncoderOutput = " V..... mpeg4              MPEG-4 part 2",
            });

        var capabilities = detector.Detect(canCaptureNarrationOverride: false);

        capabilities.Print.CanPrint.Should().BeFalse();
        capabilities.Print.Reason.Should().Contain("No available Linux print queue");
        capabilities.Video.CanEncodeMp4.Should().BeTrue();
        capabilities.Video.CanCaptureNarration.Should().BeFalse();
    }

    [Fact]
    public async Task Print_adapter_rejects_empty_or_non_pdf_payload_without_starting_a_process()
    {
        var adapter = new LinuxNativePrintHandoffAdapter(
            new LinuxNativePrintCapability(true, "/usr/bin/lp", "office", "ready"));

        var result = await adapter.PrintAsync(Array.Empty<byte>(), "Deck");

        result.Succeeded.Should().BeFalse();
        result.Canceled.Should().BeFalse();
        result.FailureReason.Should().Contain("valid non-empty PDF");
    }

    [Fact]
    public async Task Linux_print_adapter_submits_the_pdf_to_the_exact_lp_process_when_available()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"freep-print-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "lp");
        var captured = Path.Combine(directory, "submitted.pdf");
        try
        {
            await File.WriteAllTextAsync(
                executable,
                $"#!/bin/sh\ncp \"$5\" \"{captured}\"\nexit 0\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var result = await new LinuxNativePrintHandoffAdapter(
                new LinuxNativePrintCapability(true, executable, "office", "ready"))
                .PrintAsync(Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n"), "Deck");

            result.Succeeded.Should().BeTrue(result.FailureReason);
            File.ReadAllText(captured).Should().Contain("%PDF-1.7");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
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

        var output = Path.Combine(Path.GetTempPath(), $"freep-invalid-{Guid.NewGuid():N}.mp4");
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

        var output = Path.Combine(Path.GetTempPath(), $"freep-real-video-{Guid.NewGuid():N}.mp4");
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
    public async Task Video_export_cancellation_removes_partial_output_after_runner_is_cancelled()
    {
        var output = Path.Combine(Path.GetTempPath(), $"freep-cancelled-video-{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(output, "partial");
        var runner = new BlockingProcessRunner(output);
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
        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public async Task Video_export_deletes_output_when_encoder_returns_invalid_bytes()
    {
        var output = Path.Combine(Path.GetTempPath(), $"freep-invalid-video-{Guid.NewGuid():N}.mp4");
        var package = FreeP.App.Compositor.PresentationVideoFramePackageExecutor.BuildPackage(
            FreeP.Core.Model.Presentation.CreateEmpty(),
            new FreeP.App.Compositor.PresentationVideoExportRequest(
                Quality: FreeP.App.Compositor.PresentationVideoQualityKind.Standard,
                SecondsPerSlide: 0.2,
                IncludeNarration: false),
            static (_, _, _, _) => EvenTwoByTwoPng);

        var result = await new LinuxVideoExportAdapter(
            new LinuxVideoEncoderCapability(true, "ffmpeg", "mpeg4", false, "ready"),
            new InvalidOutputProcessRunner(output))
            .ExportAsync(package, output);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Contain("valid non-empty MP4");
        File.Exists(output).Should().BeFalse();
    }

    private static readonly byte[] EvenTwoByTwoPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAB0lEQVRj+M/AAEMAzJWb4gAAAABJRU5ErkJggg==");

    private sealed class BlockingProcessRunner(string outputPath) : ILinuxNativeProcessRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public async Task<LinuxNativeProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new LinuxNativeProcessResult(0, string.Empty, string.Empty, false);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                await File.WriteAllTextAsync(outputPath, "partial-after-kill");
                return new LinuxNativeProcessResult(-1, string.Empty, string.Empty, true);
            }
        }
    }

    private sealed class InvalidOutputProcessRunner(string outputPath) : ILinuxNativeProcessRunner
    {
        public async Task<LinuxNativeProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(outputPath, "not an mp4", cancellationToken);
            return new LinuxNativeProcessResult(0, string.Empty, string.Empty, false);
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
        public string DefaultPrinterOutput { get; init; } = string.Empty;
        public string QueueOutput { get; init; } = string.Empty;
        public string EncoderOutput { get; init; } = string.Empty;

        public LinuxRecordingProbeResult Run(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout)
        {
            if (fileName.EndsWith("lpstat", StringComparison.Ordinal))
            {
                var output = arguments.Contains("-d", StringComparer.Ordinal)
                    ? DefaultPrinterOutput
                    : QueueOutput;
                return new LinuxRecordingProbeResult(0, output, string.Empty);
            }

            return new LinuxRecordingProbeResult(0, EncoderOutput, string.Empty);
        }
    }
}
