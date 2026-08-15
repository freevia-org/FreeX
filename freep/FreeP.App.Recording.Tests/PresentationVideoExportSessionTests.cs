using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.Core.Model;

namespace FreeP.App.Recording.Tests;

public sealed class PresentationVideoExportSessionTests
{
    [Fact]
    public async Task ExportAsync_RetainsResultAndMapsPortableCommandOutcome()
    {
        var native = new LinuxVideoExportResult(
            true,
            false,
            "Video exported.",
            null,
            "deck.mp4",
            "test",
            42,
            MuxedNarrationTrackCount: 1,
            MuxedCameraTrackCount: 2,
            MuxedCaptionTrackCount: 3);
        var adapter = new StubVideoExportAdapter((_, _, _, _) => Task.FromResult(native));
        var session = new PresentationVideoExportSession(() => adapter);

        var result = await session.ExportAsync(
            CreatePackage(),
            "deck.mp4",
            Array.Empty<PresentationRecordingMediaArtifact>());

        result.Succeeded.Should().BeTrue();
        result.StatusText.Should().Contain("1 narration")
            .And.Contain("2 camera")
            .And.Contain("3 caption");
        session.LastResult.Should().BeSameAs(native);
        session.HasActiveExport.Should().BeFalse();
    }

    [Fact]
    public async Task CancelActiveExport_CancelsTheLinkedAdapterOperation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new StubVideoExportAdapter(async (_, path, token, _) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("The delay should have been cancelled.");
            }
            catch (OperationCanceledException)
            {
                return LinuxVideoExportResult.CanceledResult(path);
            }
        });
        var session = new PresentationVideoExportSession(() => adapter);

        var export = session.ExportAsync(
            CreatePackage(),
            "deck.mp4",
            Array.Empty<PresentationRecordingMediaArtifact>());
        await started.Task;
        session.HasActiveExport.Should().BeTrue();
        session.CancelActiveExport();

        var result = await export;
        result.Cancelled.Should().BeTrue();
        session.LastResult!.Canceled.Should().BeTrue();
        session.HasActiveExport.Should().BeFalse();
    }

    private static PresentationVideoFramePackage CreatePackage()
    {
        var presentation = Presentation.CreateEmpty();
        return PresentationVideoFramePackageExecutor.BuildPackage(
            presentation,
            request: null,
            (_, _, _, _) => [1, 2, 3],
            new PresentationVideoExportHandoffHostCapabilities(
                "test",
                CanEncodeMp4: true,
                CanCaptureNarration: true,
                CanCaptureCameraAndMedia: true,
                UnavailableReason: string.Empty,
                CanMuxTimedCaptions: true));
    }

    private sealed class StubVideoExportAdapter(
        Func<PresentationVideoFramePackage, string, CancellationToken,
            IReadOnlyList<PresentationRecordingMediaArtifact>?, Task<LinuxVideoExportResult>> export)
        : ILinuxVideoExportAdapter
    {
        public LinuxVideoEncoderCapability Capability { get; } = new(
            true,
            "test",
            "test",
            true,
            "Ready");

        public Task<LinuxVideoExportResult> ExportAsync(
            PresentationVideoFramePackage package,
            string outputPath,
            CancellationToken cancellationToken = default,
            IReadOnlyList<PresentationRecordingMediaArtifact>? mediaArtifacts = null) =>
            export(package, outputPath, cancellationToken, mediaArtifacts);
    }
}
