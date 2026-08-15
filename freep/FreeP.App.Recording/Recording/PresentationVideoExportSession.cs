using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Recording;

/// <summary>
/// Owns portable video-export adapter execution, cancellation lifetime, result retention, and
/// command-outcome mapping. Renderer hosts retain native capability and adapter selection.
/// </summary>
public sealed class PresentationVideoExportSession
{
    private readonly Func<ILinuxVideoExportAdapter> _getAdapter;
    private CancellationTokenSource? _activeCancellation;

    public PresentationVideoExportSession(Func<ILinuxVideoExportAdapter> getAdapter)
    {
        _getAdapter = getAdapter ?? throw new ArgumentNullException(nameof(getAdapter));
    }

    public LinuxVideoExportResult? LastResult { get; private set; }

    public bool HasActiveExport => _activeCancellation is not null;

    public void CancelActiveExport() => _activeCancellation?.Cancel();

    public async Task<PresentationNativeCommandResult> ExportAsync(
        PresentationVideoFramePackage package,
        string outputPath,
        IReadOnlyList<PresentationRecordingMediaArtifact> recordingMediaArtifacts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(recordingMediaArtifacts);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeCancellation = linkedCancellation;
        try
        {
            LastResult = await _getAdapter().ExportAsync(
                package,
                outputPath,
                linkedCancellation.Token,
                recordingMediaArtifacts).ConfigureAwait(true);
        }
        finally
        {
            if (ReferenceEquals(_activeCancellation, linkedCancellation))
                _activeCancellation = null;
        }

        return PresentationNativeCommandOutcomePlanner.BuildVideoExportCommandResult(
            LastResult.Succeeded,
            LastResult.Canceled,
            LastResult.FailureReason,
            LastResult.MuxedNarrationTrackCount,
            LastResult.MuxedCameraTrackCount,
            LastResult.MuxedCaptionTrackCount);
    }
}
