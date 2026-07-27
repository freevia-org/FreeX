using System.Security.Cryptography;
using FreeP.App.Compositor;

namespace FreeP.App.Recording;

public sealed class LinuxCameraCaptureBackend : ISlideShowRecordingCaptureBackend, IDisposable
{
    private static readonly TimeSpan StartupProbeWindow = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CancelTimeout = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();
    private readonly LinuxRecordingHostMetadata _metadata;
    private readonly LinuxCameraCaptureDiscovery _discovery;
    private readonly ILinuxRecordingProcessAdapter _processAdapter;
    private readonly string _temporaryDirectory;
    private readonly Dictionary<int, ActiveCapture> _activeCaptures = new();
    private readonly Dictionary<int, string> _startFailures = new();
    private bool _disposed;

    public LinuxCameraCaptureBackend(LinuxRecordingHostMetadata metadata)
        : this(
            metadata,
            new LinuxCameraDeviceCatalog(),
            new LinuxRecordingProcessAdapter())
    {
    }

    public LinuxCameraCaptureBackend(
        LinuxRecordingHostMetadata metadata,
        ILinuxCameraDeviceCatalog deviceCatalog,
        ILinuxRecordingProcessAdapter processAdapter)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        ArgumentNullException.ThrowIfNull(deviceCatalog);
        _processAdapter = processAdapter ?? throw new ArgumentNullException(nameof(processAdapter));
        _discovery = deviceCatalog.Discover();
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

        if (request.Kind != SlideShowRecordingMediaArtifactKind.CameraVideo ||
            !AdapterReadiness.CanCaptureCamera ||
            _discovery.Tool is null)
        {
            return;
        }

        var device = AdapterReadiness.Devices.First(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Camera &&
            device.IsAvailable);
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
                var command = LinuxCameraCapturePlanner.BuildCaptureCommand(
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
                    $"{_metadata.AdapterName}: could not start Linux camera capture: {ex.Message}";
            }
        }
    }

    public SlideShowRecordingCaptureResult CompleteCapture(SlideShowRecordingCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        if (request.Kind != SlideShowRecordingMediaArtifactKind.CameraVideo)
        {
            return SlideShowRecordingCaptureResult.Deferred(
                $"{_metadata.AdapterName}: Linux camera adapter received a narration request.");
        }

        if (!AdapterReadiness.CanCaptureCamera)
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
                $"{_metadata.AdapterName}: camera capture was not started for slide {request.SlideIndex + 1}.");
        }

        try
        {
            if (capture.ExitedBeforeCompletion || capture.Process.HasExited)
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    $"{_metadata.AdapterName}: Linux camera recorder exited before capture completed" +
                    ExitDetail(capture.Process) + ".");
            }

            var stop = _processAdapter.Stop(capture.Process, GracefulStopTimeout);
            if (!stop.Exited)
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    $"{_metadata.AdapterName}: Linux camera recorder did not exit after capture stopped.");
            }

            if (stop.WasForced)
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    $"{_metadata.AdapterName}: Linux camera recorder required forced termination and did not finalize the MP4.");
            }

            if (!stop.HasExpectedRecorderExitCode)
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    $"{_metadata.AdapterName}: Linux camera recorder failed" + ExitDetail(stop) + ".");
            }

            if (!File.Exists(capture.OutputPath))
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    $"{_metadata.AdapterName}: Linux camera recorder did not produce an MP4 file.");
            }

            var payload = File.ReadAllBytes(capture.OutputPath);
            if (!LinuxVideoExportAdapter.HasNonEmptyMp4Payload(payload))
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    $"{_metadata.AdapterName}: Linux camera recorder produced an empty or invalid MP4 payload.");
            }

            var fileName = capture.PackagePath.Split('/').Last();
            return SlideShowRecordingCaptureResult.Captured(
                $"{_metadata.AdapterName}: camera captured from {capture.Device.DisplayName} to {capture.PackagePath}",
                capture.PackagePath,
                payload.Length,
                Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
                payload,
                fileName,
                "video/mp4");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return SlideShowRecordingCaptureResult.Deferred(
                $"{_metadata.AdapterName}: Linux camera capture failed: {ex.Message}");
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

    private static SlideShowRecordingCaptureAdapterReadiness BuildReadiness(
        LinuxRecordingHostMetadata metadata,
        LinuxCameraCaptureDiscovery discovery) =>
        SlideShowRecordingCaptureAdapterReadiness.FromDevices(
            metadata.HostName,
            metadata.AdapterName,
            discovery.Devices,
            requiresUserPermission: true,
            discovery.IsAvailable
                ? string.Empty
                : discovery.UnavailableReason);

    private string BuildTemporaryOutputPath(SlideShowRecordingCaptureStartRequest request)
    {
        var stamp = request.StartedAtUtc.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Path.Combine(
            _temporaryDirectory,
            $"freep-camera-slide-{request.SlideIndex + 1:D4}-{stamp}.mp4");
    }

    private string NormalizePackagePath(string suggestedFileName)
    {
        var fileName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? "slide-camera.mp4"
            : suggestedFileName.Trim().Replace('\\', '/').Split('/').Last();
        fileName = Path.ChangeExtension(fileName, ".mp4");

        var packageRoot = string.IsNullOrWhiteSpace(_metadata.PackageRoot)
            ? "ppt/media/freep-recordings/avalonia"
            : _metadata.PackageRoot.Trim().Replace('\\', '/').Trim('/');
        return $"{packageRoot}/{fileName}";
    }

    private static string ResolveTemporaryDirectory(string? temporaryDirectory) =>
        string.IsNullOrWhiteSpace(temporaryDirectory)
            ? Path.Combine(Path.GetTempPath(), "freep-camera")
            : Path.GetFullPath(temporaryDirectory.Trim());

    private static string ExitDetail(ILinuxRecordingChildProcess process) =>
        BuildExitDetail(process.ExitCode, process.StandardError);

    private static string ExitDetail(LinuxRecordingProcessStopResult result) =>
        BuildExitDetail(result.ExitCode, result.StandardError);

    private static string BuildExitDetail(int? exitCode, string standardError)
    {
        var code = exitCode is null ? string.Empty : $" with code {exitCode.Value}";
        var error = string.Join(
            " ",
            (standardError ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));
        return error.Length == 0 ? code : $"{code}: {error}";
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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record ActiveCapture(
        ILinuxRecordingChildProcess Process,
        SlideShowRecordingCaptureDeviceDescriptor Device,
        string OutputPath,
        string PackagePath,
        DateTimeOffset StartedAtUtc,
        bool ExitedBeforeCompletion);
}
