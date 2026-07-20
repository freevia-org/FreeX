using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FreeP.App.Compositor;

namespace FreeP.App.Recording;

public sealed record LinuxRecordingHostMetadata(
    string HostName,
    string AdapterName,
    string PackageRoot,
    string PreferredMicrophoneDeviceId = "",
    string? TemporaryDirectory = null);

public sealed class LinuxNarrationCaptureBackend : ISlideShowRecordingCaptureBackend, IDisposable
{
    private static readonly TimeSpan StartupProbeWindow = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CancelTimeout = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();
    private readonly LinuxRecordingHostMetadata _metadata;
    private readonly LinuxNarrationCaptureDiscovery _discovery;
    private readonly ILinuxRecordingProcessAdapter _processAdapter;
    private readonly string _temporaryDirectory;
    private readonly Dictionary<int, ActiveCapture> _activeCaptures = new();
    private readonly Dictionary<int, string> _startFailures = new();
    private bool _disposed;

    public LinuxNarrationCaptureBackend(LinuxRecordingHostMetadata metadata)
        : this(
            metadata,
            new LinuxRecordingDeviceCatalog(),
            new LinuxRecordingProcessAdapter())
    {
    }

    public LinuxNarrationCaptureBackend(
        LinuxRecordingHostMetadata metadata,
        ILinuxRecordingDeviceCatalog deviceCatalog,
        ILinuxRecordingProcessAdapter processAdapter)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        ArgumentNullException.ThrowIfNull(deviceCatalog);
        _processAdapter = processAdapter ?? throw new ArgumentNullException(nameof(processAdapter));

        _discovery = Discover(deviceCatalog);
        _temporaryDirectory = ResolveTemporaryDirectory(metadata.TemporaryDirectory);
        AdapterReadiness = BuildReadiness(metadata, _discovery);
        Capabilities = SlideShowRecordingCaptureAdapterPlanner.BuildCapabilities(AdapterReadiness);
    }

    public SlideShowRecordingHostCapabilities Capabilities { get; }

    public SlideShowRecordingCaptureAdapterReadiness AdapterReadiness { get; }

    public void BeginCapture(SlideShowRecordingCaptureStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        if (request.Kind != SlideShowRecordingMediaArtifactKind.NarrationAudio ||
            !AdapterReadiness.CanCaptureNarration ||
            _discovery.Tool is null)
        {
            return;
        }

        var device = LinuxNarrationCapturePlanner.SelectMicrophone(
            AdapterReadiness.Devices,
            _metadata.PreferredMicrophoneDeviceId);
        var outputPath = BuildTemporaryOutputPath(request);
        var packagePath = NormalizePackagePath(request.SuggestedFileName);
        Directory.CreateDirectory(_temporaryDirectory);
        TryDelete(outputPath);

        lock (_sync)
        {
            CancelAndRemove(request.SlideIndex);
            _startFailures.Remove(request.SlideIndex);

            try
            {
                var command = LinuxNarrationCapturePlanner.BuildCaptureCommand(
                    _discovery.Tool,
                    device,
                    outputPath);
                var process = _processAdapter.Start(command);
                var exitedEarly = process.WaitForExit(StartupProbeWindow);
                _activeCaptures[request.SlideIndex] = new ActiveCapture(
                    process,
                    device,
                    outputPath,
                    packagePath,
                    request.StartedAtUtc,
                    exitedEarly);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                TryDelete(outputPath);
                _startFailures[request.SlideIndex] =
                    $"{_metadata.AdapterName}: could not start Linux narration capture: {ex.Message}";
            }
        }
    }

    public SlideShowRecordingCaptureResult CompleteCapture(SlideShowRecordingCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        if (request.Kind != SlideShowRecordingMediaArtifactKind.NarrationAudio)
        {
            return SlideShowRecordingCaptureResult.Deferred(
                $"{_metadata.AdapterName}: Linux camera capture is not available in the narration adapter.");
        }

        if (!AdapterReadiness.CanCaptureNarration)
        {
            return SlideShowRecordingCaptureResult.Deferred(
                $"{_metadata.AdapterName}: {AdapterReadiness.UnavailableReason}");
        }

        ActiveCapture? capture;
        string? startFailure;
        lock (_sync)
        {
            _activeCaptures.Remove(request.SlideIndex, out capture);
            _startFailures.Remove(request.SlideIndex, out startFailure);
        }

        if (capture is null)
        {
            return SlideShowRecordingCaptureResult.Deferred(
                startFailure ??
                $"{_metadata.AdapterName}: narration capture was not started for slide {request.SlideIndex + 1}.");
        }

        try
        {
            if (capture.ExitedBeforeCompletion || capture.Process.HasExited)
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    $"{_metadata.AdapterName}: Linux recorder exited before narration capture completed" +
                    ExitDetail(capture.Process) + ".");
            }

            var stop = _processAdapter.Stop(capture.Process, GracefulStopTimeout);
            if (!stop.Exited)
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    $"{_metadata.AdapterName}: Linux recorder did not exit after capture stopped.");
            }

            if (stop.WasForced)
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    $"{_metadata.AdapterName}: Linux recorder required forced termination and did not finalize narration audio.");
            }

            if (!stop.HasExpectedRecorderExitCode)
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    $"{_metadata.AdapterName}: Linux recorder failed" + ExitDetail(stop) + ".");
            }

            if (!File.Exists(capture.OutputPath))
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    $"{_metadata.AdapterName}: Linux recorder did not produce a narration file.");
            }

            var payload = File.ReadAllBytes(capture.OutputPath);
            if (!HasNonEmptyWavePayload(payload))
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    $"{_metadata.AdapterName}: Linux recorder produced an empty or invalid WAV narration file.");
            }

            var fileName = capture.PackagePath.Split('/').Last();
            return SlideShowRecordingCaptureResult.Captured(
                $"{_metadata.AdapterName}: narration captured from {capture.Device.DisplayName} to {capture.PackagePath}",
                capture.PackagePath,
                payload.Length,
                Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
                payload,
                fileName,
                "audio/wav");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return SlideShowRecordingCaptureResult.Deferred(
                $"{_metadata.AdapterName}: Linux narration capture failed: {ex.Message}");
        }
        finally
        {
            capture.Process.Dispose();
            TryDelete(capture.OutputPath);
        }
    }

    public void CancelCapture(int slideIndex)
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            CancelAndRemove(slideIndex);
            _startFailures.Remove(slideIndex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_sync)
        {
            foreach (var capture in _activeCaptures.Values)
                CancelAndDispose(capture);
            _activeCaptures.Clear();
            _startFailures.Clear();
            _disposed = true;
        }
    }

    private void CancelAndRemove(int slideIndex)
    {
        if (_activeCaptures.Remove(slideIndex, out var previous))
            CancelAndDispose(previous);
    }

    private void CancelAndDispose(ActiveCapture capture)
    {
        try
        {
            _processAdapter.Cancel(capture.Process, CancelTimeout);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }
        finally
        {
            capture.Process.Dispose();
            TryDelete(capture.OutputPath);
        }
    }

    private static LinuxNarrationCaptureDiscovery Discover(ILinuxRecordingDeviceCatalog deviceCatalog)
    {
        try
        {
            return deviceCatalog.Discover();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return LinuxNarrationCaptureDiscovery.Unavailable(
                $"Linux recording device discovery failed: {ex.Message}");
        }
    }

    private static SlideShowRecordingCaptureAdapterReadiness BuildReadiness(
        LinuxRecordingHostMetadata metadata,
        LinuxNarrationCaptureDiscovery discovery) =>
        SlideShowRecordingCaptureAdapterReadiness.FromDevices(
            metadata.HostName,
            metadata.AdapterName,
            discovery.Devices,
            requiresUserPermission: true,
            discovery.IsAvailable
                ? "Linux camera recording is not available in the narration adapter."
                : discovery.UnavailableReason);

    private string BuildTemporaryOutputPath(SlideShowRecordingCaptureStartRequest request)
    {
        var stamp = request.StartedAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture);
        return Path.Combine(
            _temporaryDirectory,
            $"freep-narration-slide-{request.SlideIndex + 1:D4}-{stamp}.wav");
    }

    private string NormalizePackagePath(string suggestedFileName)
    {
        var fileName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? "slide-narration.wav"
            : suggestedFileName.Trim().Replace('\\', '/').Split('/').Last();
        fileName = Path.ChangeExtension(fileName, ".wav");

        var packageRoot = string.IsNullOrWhiteSpace(_metadata.PackageRoot)
            ? "ppt/media/freep-recordings/avalonia"
            : _metadata.PackageRoot.Trim().Replace('\\', '/').Trim('/');
        return $"{packageRoot}/{fileName}";
    }

    private static string ResolveTemporaryDirectory(string? temporaryDirectory) =>
        string.IsNullOrWhiteSpace(temporaryDirectory)
            ? Path.Combine(Path.GetTempPath(), "freep-narration")
            : Path.GetFullPath(temporaryDirectory.Trim());

    private static bool HasNonEmptyWavePayload(byte[] payload)
    {
        if (payload.Length < 45 ||
            !payload.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !payload.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            return false;
        }

        var offset = 12;
        while (offset + 8 <= payload.Length)
        {
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset + 4, 4));
            var dataOffset = offset + 8;
            if (payload.AsSpan(offset, 4).SequenceEqual("data"u8))
                return chunkSize > 0 && dataOffset + chunkSize <= payload.Length;

            var paddedSize = checked((long)chunkSize + (chunkSize & 1));
            var nextOffset = dataOffset + paddedSize;
            if (nextOffset > payload.Length || nextOffset > int.MaxValue)
                return false;
            offset = (int)nextOffset;
        }

        return false;
    }

    private static string ExitDetail(ILinuxRecordingChildProcess process) =>
        BuildExitDetail(process.ExitCode, process.StandardError);

    private static string ExitDetail(LinuxRecordingProcessStopResult result) =>
        BuildExitDetail(result.ExitCode, result.StandardError);

    private static string BuildExitDetail(int? exitCode, string standardError)
    {
        var code = exitCode is null ? string.Empty : $" with code {exitCode.Value}";
        var error = NormalizeProcessError(standardError);
        return error.Length == 0 ? code : $"{code}: {error}";
    }

    private static string NormalizeProcessError(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = string.Join(
            " ",
            value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()));
        return normalized.Length <= 240 ? normalized : normalized[..237] + "...";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record ActiveCapture(
        ILinuxRecordingChildProcess Process,
        SlideShowRecordingCaptureDeviceDescriptor Device,
        string OutputPath,
        string PackagePath,
        DateTimeOffset StartedAtUtc,
        bool ExitedBeforeCompletion);
}
