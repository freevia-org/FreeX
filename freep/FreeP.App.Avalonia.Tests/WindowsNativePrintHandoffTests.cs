using System.Buffers.Binary;
using FreeP.App.Recording;
using FreeP.App.Recording.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

public sealed class WindowsNativePrintHandoffTests
{
    [Fact]
    public void AvaloniaWindowRoutesWindowsBuildsToNativePrintOutput()
    {
        var repo = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(repo, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("WindowsNativePrintOutput.Detect()");
        source.Should().Contain("WindowsNativePrintOutput.CreateAdapter(capability)");
        source.Should().Contain("WindowsNativePrintOutput.CreateVideoAdapter(capability)");
        source.Should().Contain("FREEP_WINDOWS_CAPTURE");
    }

    [Fact]
    public async Task WindowsAdapterRejectsNonPdfBeforeStartingShell()
    {
        var capability = new LinuxNativePrintCapability(
            CanPrint: true,
            ExecutablePath: "windows-shell-print",
            PrinterName: "test-printer",
            Reason: "ready");
        var adapter = new WindowsNativePrintHandoffAdapter(capability);

        var result = await adapter.PrintAsync([1, 2, 3], "test");

        result.Succeeded.Should().BeFalse();
        result.Canceled.Should().BeFalse();
        result.FailureReason.Should().Contain("valid non-empty PDF");
    }

    [Fact]
    public void WindowsDetectionAdvertisesNativeVideoComposition()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var capability = WindowsNativePrintOutput.Detect().Video;
        var devices = new WindowsNativeRecordingDeviceCatalog().EnumerateDevices();

        capability.CanEncodeMp4.Should().BeTrue();
        capability.ExecutablePath.Should().Be(WindowsNativeVideoExportAdapter.ExecutablePath);
        capability.EncoderName.Should().Be("Windows MediaComposition");
        capability.CanCaptureNarration.Should().Be(devices.Any(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Microphone && device.IsAvailable));
        capability.CanCaptureCameraAndMedia.Should().Be(devices.Any(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Camera && device.IsAvailable));
    }

    [Fact]
    public void WindowsVideoCapabilityReflectsEachEnumeratedCaptureDevice()
    {
        var capability = WindowsNativePrintOutput.DetectWindowsVideoCapability(
            new FakeWindowsRecordingDeviceCatalog(
                new(SlideShowRecordingCaptureDeviceKind.Microphone, "mic", "Mic", true, true, "audio/wav"),
                new(SlideShowRecordingCaptureDeviceKind.Camera, "camera", "Camera", true, false, "video/mp4"),
                new(SlideShowRecordingCaptureDeviceKind.Camera, "camera-2", "Camera 2", true, true, "video/mp4")));

        capability.CanEncodeMp4.Should().BeTrue();
        capability.CanCaptureNarration.Should().BeTrue();
        capability.CanCaptureCameraAndMedia.Should().BeTrue();
        capability.Reason.Should().Contain("multi-track narration");
        capability.Reason.Should().Contain("camera PIP");
    }

    [Fact]
    public void WindowsVideoCapabilityDoesNotAdvertiseUnavailableDevices()
    {
        var capability = WindowsNativePrintOutput.DetectWindowsVideoCapability(
            new FakeWindowsRecordingDeviceCatalog(
                new(SlideShowRecordingCaptureDeviceKind.Microphone, "mic", "Mic", true, false, "audio/wav"),
                new(SlideShowRecordingCaptureDeviceKind.Camera, "camera", "Camera", true, false, "video/mp4")));

        capability.CanCaptureNarration.Should().BeFalse();
        capability.CanCaptureCameraAndMedia.Should().BeFalse();
        capability.Reason.Should().Contain("no microphone device");
        capability.Reason.Should().Contain("no camera device");
    }

    [Fact]
    public void WindowsVideoCapabilitySelectsTheNativeAdapter()
    {
        var capability = new LinuxVideoEncoderCapability(
            CanEncodeMp4: true,
            ExecutablePath: WindowsNativeVideoExportAdapter.ExecutablePath,
            EncoderName: "Windows MediaComposition",
            CanCaptureNarration: false,
            Reason: "ready");

        var adapter = WindowsNativePrintOutput.CreateVideoAdapter(capability);

        adapter.Should().BeOfType<WindowsNativeVideoExportAdapter>();
    }

    [Fact]
    public async Task WindowsNativeVideoAdapter_ExportsAFramePackageOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Add(new Slide());
        var package = PresentationVideoFramePackageExecutor.BuildPackage(
            presentation,
            new PresentationVideoExportRequest(SecondsPerSlide: 0.2),
            static (_, _, _, _) => TinyPng);
        var capability = new LinuxVideoEncoderCapability(
            CanEncodeMp4: true,
            ExecutablePath: WindowsNativeVideoExportAdapter.ExecutablePath,
            EncoderName: "Windows MediaComposition",
            CanCaptureNarration: true,
            Reason: "test");
        var adapter = new WindowsNativeVideoExportAdapter(capability);
        using var temporaryDirectory = new TestTemporaryDirectory("freep-native-video-test-");
        var outputPath = Path.Combine(temporaryDirectory.Path, "video.mp4");
        var narrationOutputPath = Path.Combine(temporaryDirectory.Path, "narration-video.mp4");
        var cameraOutputPath = Path.Combine(temporaryDirectory.Path, "camera-video.mp4");

        {
            var result = await adapter.ExportAsync(package, outputPath);

            if (!result.Succeeded)
                throw new Xunit.Sdk.XunitException(
                    $"Native export failed: canceled={result.Canceled}; bytes={result.ByteCount}; " +
                    $"encoder=[{result.EncoderName}]; reason=[{result.FailureReason}]; status=[{result.StatusText}].");
            result.ByteCount.Should().BeGreaterThan(0);
            File.Exists(outputPath).Should().BeTrue();

            var narrationBytes = BuildPcmWav(durationMs: 200);
            var narrationArtifact = new PresentationRecordingMediaArtifact(
                PresentationRecordingMediaArtifactKind.NarrationAudio,
                SlideIndex: 0,
                SuggestedFileName: "narration.wav",
                ContentType: "audio/wav",
                PackagePath: "ppt/media/freep-recordings/test/narration.wav",
                ContentLengthBytes: narrationBytes.LongLength,
                ContentSha256: "test-narration",
                DurationMs: 200,
                CapturedByHost: "test",
                StatusText: "captured",
                PayloadBytes: narrationBytes);
            var secondNarrationBytes = BuildPcmWav(durationMs: 100);
            var secondNarrationArtifact = new PresentationRecordingMediaArtifact(
                PresentationRecordingMediaArtifactKind.NarrationAudio,
                SlideIndex: 1,
                SuggestedFileName: "narration-2.wav",
                ContentType: "audio/wav",
                PackagePath: "ppt/media/freep-recordings/test/narration-2.wav",
                ContentLengthBytes: secondNarrationBytes.LongLength,
                ContentSha256: "test-narration-2",
                DurationMs: 100,
                CapturedByHost: "test",
                StatusText: "captured",
                PayloadBytes: secondNarrationBytes);
            var narrationPackage = PresentationVideoFramePackageExecutor.BuildPackage(
                presentation,
                new PresentationVideoExportRequest(SecondsPerSlide: 0.2, IncludeNarration: true),
                static (_, _, _, _) => TinyPng);
            var narrationResult = await adapter.ExportAsync(
                narrationPackage,
                narrationOutputPath,
                CancellationToken.None,
                [narrationArtifact, secondNarrationArtifact]);
            if (!narrationResult.Succeeded)
                throw new Xunit.Sdk.XunitException(
                    $"Native narration export failed: canceled={narrationResult.Canceled}; " +
                    $"reason=[{narrationResult.FailureReason}].");
            narrationResult.ByteCount.Should().BeGreaterThan(0);
            File.Exists(narrationOutputPath).Should().BeTrue();

            var cameraBytes = await File.ReadAllBytesAsync(outputPath);
            var cameraArtifact = new PresentationRecordingMediaArtifact(
                PresentationRecordingMediaArtifactKind.CameraVideo,
                SlideIndex: 0,
                SuggestedFileName: "camera.mp4",
                ContentType: "video/mp4",
                PackagePath: "ppt/media/freep-recordings/test/camera.mp4",
                ContentLengthBytes: cameraBytes.LongLength,
                ContentSha256: "test-camera",
                DurationMs: 200,
                CapturedByHost: "test",
                StatusText: "captured",
                PayloadBytes: cameraBytes);
            var cameraResult = await adapter.ExportAsync(
                package,
                cameraOutputPath,
                CancellationToken.None,
                [cameraArtifact]);
            if (!cameraResult.Succeeded)
                throw new Xunit.Sdk.XunitException(
                    $"Native camera overlay export failed: canceled={cameraResult.Canceled}; " +
                    $"reason=[{cameraResult.FailureReason}].");
            cameraResult.ByteCount.Should().BeGreaterThan(0);
            File.Exists(cameraOutputPath).Should().BeTrue();
        }
    }

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static byte[] BuildPcmWav(int durationMs)
    {
        const int sampleRate = 8000;
        const short channels = 1;
        const short bitsPerSample = 16;
        var sampleCount = sampleRate * durationMs / 1000;
        var dataLength = sampleCount * channels * (bitsPerSample / 8);
        var wav = new byte[44 + dataLength];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4), (uint)(wav.Length - 8));
        "WAVE"u8.CopyTo(wav.AsSpan(8));
        "fmt "u8.CopyTo(wav.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(22), (ushort)channels);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(28), sampleRate * channels * (bitsPerSample / 8));
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(32), (ushort)(channels * (bitsPerSample / 8)));
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34), (ushort)bitsPerSample);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40), (uint)dataLength);
        return wav;
    }

    private sealed class FakeWindowsRecordingDeviceCatalog(
        params SlideShowRecordingCaptureDeviceDescriptor[] devices) : IWindowsRecordingDeviceCatalog
    {
        public IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> EnumerateDevices() => devices;
    }
}
