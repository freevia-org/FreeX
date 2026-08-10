using System.Text;
using FreeP.App.Compositor;
using FreeP.App.Recording;

namespace FreeP.App.Recording.Tests;

public sealed class PresentationVideoExportOrchestratorTests : IDisposable
{
    private static readonly byte[] EvenTwoByTwoPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAB0lEQVRj+M/AAEMAzJWb4gAAAABJRU5ErkJggg==");

    private readonly TestTemporaryDirectory _temporaryDirectory =
        new("FreeP.VideoExportOrchestratorTests-");

    public void Dispose() => _temporaryDirectory.Dispose();

    [Fact]
    public async Task ExportAsync_PreparesBackendWorkspaceAndCleansOwnedResources()
    {
        var outputPath = Path.Combine(_temporaryDirectory.Path, "prepared.mp4");
        var narrationBytes = Encoding.ASCII.GetBytes("narration payload");
        var artifact = new PresentationRecordingMediaArtifact(
            PresentationRecordingMediaArtifactKind.NarrationAudio,
            SlideIndex: 0,
            SuggestedFileName: "narration.wav",
            ContentType: "audio/wav",
            PackagePath: "ppt/media/freep-recordings/test/narration.wav",
            ContentLengthBytes: narrationBytes.Length,
            ContentSha256: "test-sha",
            DurationMs: 200,
            CapturedByHost: "test",
            StatusText: "captured",
            PayloadBytes: narrationBytes);
        PresentationVideoExportWorkspace? capturedWorkspace = null;
        string? capturedStage = null;
        var backend = new DelegateBackend((workspace, stage, _) =>
        {
            capturedWorkspace = workspace;
            capturedStage = stage.Current;
            File.WriteAllBytes(workspace.FullOutputPath, ValidMp4Bytes());
            return Task.FromResult(PresentationVideoExportBackendResult.Encoded(
                "test-encoder",
                workspace.MediaPlan.MuxedNarrationTrackCount));
        });

        var result = await CreateOrchestrator(backend, buildConcatFile: true)
            .ExportAsync(
                BuildPackage(includeNarration: true),
                outputPath,
                CancellationToken.None,
                [artifact]);

        result.Succeeded.Should().BeTrue(result.FailureReason);
        result.EncoderName.Should().Be("test-encoder");
        result.MuxedNarrationTrackCount.Should().Be(1);
        result.ByteCount.Should().Be(ValidMp4Bytes().LongLength);
        File.Exists(outputPath).Should().BeTrue();
        capturedWorkspace.Should().NotBeNull();
        capturedWorkspace!.Frames.Should().ContainSingle();
        capturedWorkspace.ConcatPath.Should().NotBeNull();
        capturedWorkspace.MediaPlan.NarrationTracks.Should().ContainSingle();
        capturedStage.Should().Be("initializing test export");
        Directory.Exists(capturedWorkspace.TemporaryDirectory).Should().BeFalse();
        File.Exists(capturedWorkspace.Frames[0].Path).Should().BeFalse();
        File.Exists(capturedWorkspace.ConcatPath!).Should().BeFalse();
        File.Exists(capturedWorkspace.MediaPlan.NarrationTracks[0].Path).Should().BeFalse();
    }

    [Fact]
    public async Task ExportAsync_CancellationDeletesPartialOutputAndCleansWorkspace()
    {
        var outputPath = Path.Combine(_temporaryDirectory.Path, "canceled.mp4");
        PresentationVideoExportWorkspace? capturedWorkspace = null;
        var backend = new DelegateBackend((workspace, _, cancellationToken) =>
        {
            capturedWorkspace = workspace;
            File.WriteAllText(workspace.FullOutputPath, "partial");
            throw new OperationCanceledException(cancellationToken);
        });

        var result = await CreateOrchestrator(backend).ExportAsync(BuildPackage(), outputPath);

        result.Succeeded.Should().BeFalse();
        result.Canceled.Should().BeTrue();
        File.Exists(outputPath).Should().BeFalse();
        capturedWorkspace.Should().NotBeNull();
        Directory.Exists(capturedWorkspace!.TemporaryDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task ExportAsync_BackendFailureUsesCurrentStageAndDeletesPartialOutput()
    {
        var outputPath = Path.Combine(_temporaryDirectory.Path, "failed.mp4");
        PresentationVideoExportWorkspace? capturedWorkspace = null;
        var backend = new DelegateBackend((workspace, stage, _) =>
        {
            capturedWorkspace = workspace;
            File.WriteAllText(workspace.FullOutputPath, "partial");
            stage.Set("rendering native video");
            throw new InvalidOperationException("native failure");
        });

        var result = await CreateOrchestrator(backend).ExportAsync(BuildPackage(), outputPath);

        result.Succeeded.Should().BeFalse();
        result.Canceled.Should().BeFalse();
        result.FailureReason.Should().Be(
            "test export failed while rendering native video with InvalidOperationException: native failure");
        File.Exists(outputPath).Should().BeFalse();
        capturedWorkspace.Should().NotBeNull();
        Directory.Exists(capturedWorkspace!.TemporaryDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task ExportAsync_InvalidBackendOutputUsesConfiguredFailureAndDeletesFile()
    {
        var outputPath = Path.Combine(_temporaryDirectory.Path, "invalid.mp4");
        var backend = new DelegateBackend((workspace, _, _) =>
        {
            File.WriteAllText(workspace.FullOutputPath, "not an mp4");
            return Task.FromResult(PresentationVideoExportBackendResult.Encoded("test-encoder"));
        });

        var result = await CreateOrchestrator(backend).ExportAsync(BuildPackage(), outputPath);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().Be("test encoder produced invalid output");
        File.Exists(outputPath).Should().BeFalse();
    }

    [Fact]
    public async Task ExportAsync_InvalidOutputPathReturnsConfiguredFailureOutcome()
    {
        var result = await CreateOrchestrator(new DelegateBackend((_, _, _) =>
                throw new InvalidOperationException("backend must not run")))
            .ExportAsync(BuildPackage(), "invalid\0output.mp4");

        result.Succeeded.Should().BeFalse();
        result.Canceled.Should().BeFalse();
        result.FailureReason.Should().Contain("initializing test export");
        result.FailureReason.Should().Contain("ArgumentException");
    }

    [Fact]
    public async Task ExportAsync_BackendFailurePreservesReasonAndDeletesPartialOutput()
    {
        var outputPath = Path.Combine(_temporaryDirectory.Path, "backend-failed.mp4");
        var backend = new DelegateBackend((workspace, _, _) =>
        {
            File.WriteAllText(workspace.FullOutputPath, "partial");
            return Task.FromResult(PresentationVideoExportBackendResult.Failed(
                "native backend is unavailable"));
        });

        var result = await CreateOrchestrator(backend).ExportAsync(BuildPackage(), outputPath);

        result.Succeeded.Should().BeFalse();
        result.Canceled.Should().BeFalse();
        result.FailureReason.Should().Be("native backend is unavailable");
        File.Exists(outputPath).Should().BeFalse();
    }

    [Fact]
    public async Task ExportAsync_ValidCompletedBackendResultPreservesFallbackOutcome()
    {
        var outputPath = Path.Combine(_temporaryDirectory.Path, "fallback.mp4");
        var fallbackResult = new LinuxVideoExportResult(
            Succeeded: true,
            Canceled: false,
            StatusText: "caption fallback completed",
            FailureReason: null,
            OutputPath: outputPath,
            EncoderName: "fallback-encoder",
            ByteCount: ValidMp4Bytes().LongLength,
            MuxedCaptionTrackCount: 1);
        var backend = new DelegateBackend((workspace, _, _) =>
        {
            File.WriteAllBytes(workspace.FullOutputPath, ValidMp4Bytes());
            return Task.FromResult(PresentationVideoExportBackendResult.Completed(fallbackResult));
        });

        var result = await CreateOrchestrator(backend).ExportAsync(BuildPackage(), outputPath);

        result.Should().BeSameAs(fallbackResult);
        File.Exists(outputPath).Should().BeTrue();
    }

    [Fact]
    public void WindowsNativeAdapter_KeepsOnlyNativeEncodingAndFallbackResponsibilities()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Recording.Windows",
            "WindowsNativeVideoExportAdapter.cs"));

        source.Should().Contain("new PresentationVideoExportOrchestrator(");
        source.Should().Contain("WindowsMediaCompositionVideoExportBackend");
        source.Should().Contain("new MediaComposition()");
        source.Should().Contain("StorageFile.GetFileFromPathAsync");
        source.Should().Contain("RenderToFileAsync");
        source.Should().Contain("MediaTrimmingPreference.Precise");
        source.Should().Contain("VideoEncodingQuality.HD1080p");
        source.Should().Contain("VideoEncodingQuality.HD720p");
        source.Should().Contain("audioTrack.Delay = narration.StartTime;");
        source.Should().Contain("AudioEnabled = false");
        source.Should().Contain("Delay = camera.StartTime");
        source.Should().Contain(
            "Windows MediaComposition cannot mux timed caption tracks. Install ffmpeg to export this captioned video.");
        source.Should().NotContain("TemporaryDirectoryLease");
        source.Should().NotContain("PresentationVideoFramePackageExecutor.ValidatePackage");
        source.Should().NotContain("ZipArchive");
        source.Should().NotContain("File.ReadAllBytesAsync");
        source.Should().NotContain("HasNonEmptyMp4Payload");
        source.Should().NotContain("catch (OperationCanceledException)");
        source.Should().NotContain("LinuxVideoExportResult.CanceledResult(");
        source.Should().NotContain("LinuxVideoExportResult.Failed(");
    }

    private static PresentationVideoExportOrchestrator CreateOrchestrator(
        IPresentationVideoExportBackend backend,
        bool buildConcatFile = false) =>
        new(
            new LinuxVideoEncoderCapability(
                CanEncodeMp4: true,
                ExecutablePath: "test-encoder",
                EncoderName: "test-encoder",
                CanCaptureNarration: true,
                Reason: "ready"),
            backend,
            new PresentationVideoExportOrchestrationOptions(
                TemporaryDirectoryPrefix: "freep-video-orchestrator-test-",
                InitialStage: "initializing test export",
                InvalidOutputReason: "test encoder produced invalid output",
                CanExport: static capability => capability.CanEncodeMp4,
                FormatFailureReason: static (stage, ex) =>
                    $"test export failed while {stage} with {ex.GetType().Name}: {ex.Message}",
                BuildFfmpegConcatFile: buildConcatFile,
                RequireNonEmptyFrames: true,
                FramePreparationStage: static frame => $"preparing {frame.FileName}"));

    private static PresentationVideoFramePackage BuildPackage(bool includeNarration = false) =>
        PresentationVideoFramePackageExecutor.BuildPackage(
            Presentation.CreateEmpty(),
            new PresentationVideoExportRequest(
                Quality: PresentationVideoQualityKind.Standard,
                SecondsPerSlide: 0.2,
                IncludeNarration: includeNarration),
            static (_, _, _, _) => EvenTwoByTwoPng);

    private static byte[] ValidMp4Bytes() =>
        Encoding.ASCII.GetBytes("0000ftyp0000moov0000mdat");

    private sealed class DelegateBackend(
        Func<
            PresentationVideoExportWorkspace,
            PresentationVideoExportStage,
            CancellationToken,
            Task<PresentationVideoExportBackendResult>> encodeAsync)
        : IPresentationVideoExportBackend
    {
        public Task<PresentationVideoExportBackendResult> EncodeAsync(
            PresentationVideoExportWorkspace workspace,
            PresentationVideoExportStage stage,
            CancellationToken cancellationToken) =>
            encodeAsync(workspace, stage, cancellationToken);
    }
}
