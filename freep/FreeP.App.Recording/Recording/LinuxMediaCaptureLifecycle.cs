using System.Security.Cryptography;
using Free.Shared.AppServices;
using FreeP.App.Compositor;

namespace FreeP.App.Recording;

public sealed record LinuxRecordingHostMetadata(
    string HostName,
    string AdapterName,
    string PackageRoot,
    string PreferredMicrophoneDeviceId = "",
    string? TemporaryDirectory = null);

internal interface ILinuxMediaCapturePolicy
{
    SlideShowRecordingMediaArtifactKind Kind { get; }

    string TemporaryDirectoryName { get; }

    string ContentType { get; }

    bool IsAvailable(SlideShowRecordingCaptureAdapterReadiness readiness);

    SlideShowRecordingCaptureDeviceDescriptor SelectDevice(
        IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> devices,
        LinuxRecordingHostMetadata metadata);

    LinuxNarrationCaptureCommand BuildCommand(
        SlideShowRecordingCaptureDeviceDescriptor device,
        string outputPath);

    string BuildTemporaryFileName(SlideShowRecordingCaptureStartRequest request);

    string NormalizePackagePath(
        LinuxRecordingHostMetadata metadata,
        string suggestedFileName);

    bool HasValidPayload(byte[] payload);

    string WrongKindMessage(string adapterName);

    string UnavailableMessage(string adapterName, string reason);

    string NotStartedMessage(string adapterName, int slideIndex, string? startFailure);

    string ExitedBeforeCompletionMessage(
        string adapterName,
        ILinuxRecordingChildProcess process);

    string DidNotExitMessage(string adapterName);

    string ForcedStopMessage(string adapterName);

    string FailedMessage(string adapterName, LinuxRecordingProcessStopResult stop);

    string MissingOutputMessage(string adapterName);

    string InvalidPayloadMessage(string adapterName);

    string CaptureFailedMessage(string adapterName, Exception exception);

    string StartFailedMessage(string adapterName, Exception exception);

    string CapturedMessage(
        string adapterName,
        SlideShowRecordingCaptureDeviceDescriptor device,
        string packagePath);
}

internal sealed class LinuxMediaCaptureLifecycle : IDisposable
{
    private static readonly TimeSpan StartupProbeWindow = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CancelTimeout = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();
    private readonly LinuxRecordingHostMetadata _metadata;
    private readonly ILinuxMediaCapturePolicy _policy;
    private readonly ILinuxRecordingProcessAdapter _processAdapter;
    private readonly string? _configuredTemporaryDirectory;
    private readonly string _temporaryDirectoryName;
    private TemporaryDirectoryLease? _temporaryDirectoryLease;
    private readonly Dictionary<int, ActiveCapture> _activeCaptures = new();
    private readonly Dictionary<int, string> _startFailures = new();
    private bool _disposed;

    public LinuxMediaCaptureLifecycle(
        LinuxRecordingHostMetadata metadata,
        SlideShowRecordingCaptureAdapterReadiness readiness,
        ILinuxMediaCapturePolicy policy,
        ILinuxRecordingProcessAdapter processAdapter)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _processAdapter = processAdapter ?? throw new ArgumentNullException(nameof(processAdapter));
        AdapterReadiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _configuredTemporaryDirectory = string.IsNullOrWhiteSpace(metadata.TemporaryDirectory)
            ? null
            : Path.GetFullPath(metadata.TemporaryDirectory.Trim());
        _temporaryDirectoryName = policy.TemporaryDirectoryName;
    }

    public SlideShowRecordingCaptureAdapterReadiness AdapterReadiness { get; }

    public void BeginCapture(SlideShowRecordingCaptureStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        if (request.Kind != _policy.Kind ||
            !_policy.IsAvailable(AdapterReadiness))
        {
            return;
        }

        var device = _policy.SelectDevice(AdapterReadiness.Devices, _metadata);
        string outputPath;
        var packagePath = _policy.NormalizePackagePath(
            _metadata,
            request.SuggestedFileName);
        // Preparing the temp directory sits outside the launch try/catch below, so an unwritable,
        // full or read-only filesystem would throw straight out of BeginCapture — no caller guards
        // it. Record it the same way a failed launch is recorded, so CompleteCapture reports it
        // rather than the start throwing at the UI.
        try
        {
            var temporaryDirectory = ResolveTemporaryDirectory();
            Directory.CreateDirectory(temporaryDirectory);
            outputPath = Path.Combine(
                temporaryDirectory,
                _policy.BuildTemporaryFileName(request));
            TryDelete(outputPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            lock (_sync)
            {
                CancelAndRemove(request.SlideIndex);
                _startFailures[request.SlideIndex] =
                    _policy.StartFailedMessage(_metadata.AdapterName, ex);
            }
            return;
        }

        var outputFile = TemporaryFileLease.Own(outputPath);

        lock (_sync)
        {
            CancelAndRemove(request.SlideIndex);
            _startFailures.Remove(request.SlideIndex);

            ILinuxRecordingChildProcess? launchedProcess = null;
            var ownershipTransferred = false;
            try
            {
                var command = _policy.BuildCommand(device, outputPath);
                launchedProcess = _processAdapter.Start(command);
                var exitedEarly = launchedProcess.WaitForExit(StartupProbeWindow);
                var capture = new ActiveCapture(
                    launchedProcess,
                    device,
                    outputFile,
                    packagePath,
                    exitedEarly);
                _activeCaptures[request.SlideIndex] = capture;
                ownershipTransferred = true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _startFailures[request.SlideIndex] =
                    _policy.StartFailedMessage(_metadata.AdapterName, ex);
            }
            finally
            {
                // Start transfers ownership only after the active-capture record is installed.
                // This also covers WaitForExit and bookkeeping failures after a child launched.
                if (!ownershipTransferred)
                {
                    if (launchedProcess is not null)
                        CancelAndDispose(launchedProcess, outputFile);
                    else
                        outputFile.Dispose();
                }
            }
        }
    }

    public SlideShowRecordingCaptureResult CompleteCapture(
        SlideShowRecordingCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();

        if (request.Kind != _policy.Kind)
        {
            return SlideShowRecordingCaptureResult.Deferred(
                _policy.WrongKindMessage(_metadata.AdapterName));
        }

        if (!_policy.IsAvailable(AdapterReadiness))
        {
            return SlideShowRecordingCaptureResult.Deferred(
                _policy.UnavailableMessage(
                    _metadata.AdapterName,
                    AdapterReadiness.UnavailableReason));
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
                _policy.NotStartedMessage(
                    _metadata.AdapterName,
                    request.SlideIndex,
                    startFailure));
        }

        try
        {
            if (capture.ExitedBeforeCompletion || capture.Process.HasExited)
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    _policy.ExitedBeforeCompletionMessage(
                        _metadata.AdapterName,
                        capture.Process));
            }

            var stop = _processAdapter.Stop(capture.Process, GracefulStopTimeout);
            if (!stop.Exited)
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    _policy.DidNotExitMessage(_metadata.AdapterName));
            }

            if (stop.WasForced)
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    _policy.ForcedStopMessage(_metadata.AdapterName));
            }

            if (!stop.HasExpectedRecorderExitCode)
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    _policy.FailedMessage(_metadata.AdapterName, stop));
            }

            if (!File.Exists(capture.OutputFile.Path))
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    _policy.MissingOutputMessage(_metadata.AdapterName));
            }

            var payload = File.ReadAllBytes(capture.OutputFile.Path);
            if (!_policy.HasValidPayload(payload))
            {
                return SlideShowRecordingCaptureResult.Deferred(
                    _policy.InvalidPayloadMessage(_metadata.AdapterName));
            }

            var fileName = capture.PackagePath.Split('/').Last();
            return SlideShowRecordingCaptureResult.Captured(
                _policy.CapturedMessage(
                    _metadata.AdapterName,
                    capture.Device,
                    capture.PackagePath),
                capture.PackagePath,
                payload.Length,
                Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
                payload,
                fileName,
                _policy.ContentType);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return SlideShowRecordingCaptureResult.Deferred(
                _policy.CaptureFailedMessage(_metadata.AdapterName, ex));
        }
        finally
        {
            capture.Process.Dispose();
            capture.OutputFile.Dispose();
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
                CancelAndDispose(capture.Process, capture.OutputFile);
            _activeCaptures.Clear();
            _startFailures.Clear();
            _temporaryDirectoryLease?.Dispose();
            _temporaryDirectoryLease = null;
            _disposed = true;
        }
    }

    private void CancelAndRemove(int slideIndex)
    {
        if (_activeCaptures.Remove(slideIndex, out var previous))
            CancelAndDispose(previous.Process, previous.OutputFile);
    }

    private void CancelAndDispose(
        ILinuxRecordingChildProcess process,
        TemporaryFileLease outputFile)
    {
        try
        {
            _processAdapter.Cancel(process, CancelTimeout);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }
        finally
        {
            process.Dispose();
            outputFile.Dispose();
        }
    }

    private string ResolveTemporaryDirectory()
    {
        if (_configuredTemporaryDirectory is not null)
            return _configuredTemporaryDirectory;

        lock (_sync)
        {
            _temporaryDirectoryLease ??= TemporaryDirectoryLease.Create(
                _temporaryDirectoryName + "-");
            return _temporaryDirectoryLease.Path;
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

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record ActiveCapture(
        ILinuxRecordingChildProcess Process,
        SlideShowRecordingCaptureDeviceDescriptor Device,
        TemporaryFileLease OutputFile,
        string PackagePath,
        bool ExitedBeforeCompletion);
}

internal static class LinuxMediaCapturePathPolicy
{
    public static string NormalizePackagePath(
        LinuxRecordingHostMetadata metadata,
        string suggestedFileName,
        string defaultFileName,
        string extension)
    {
        var fileName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? defaultFileName
            : suggestedFileName.Trim().Replace('\\', '/').Split('/').Last();
        fileName = Path.ChangeExtension(fileName, extension);
        var packageRoot = string.IsNullOrWhiteSpace(metadata.PackageRoot)
            ? "ppt/media/freep-recordings/avalonia"
            : metadata.PackageRoot.Trim().Replace('\\', '/').Trim('/');
        return $"{packageRoot}/{fileName}";
    }
}

internal static class LinuxMediaCaptureMessagePolicy
{
    public static string ExitDetail(ILinuxRecordingChildProcess process) =>
        ExitDetail(process.ExitCode, process.StandardError);

    public static string ExitDetail(LinuxRecordingProcessStopResult result) =>
        ExitDetail(result.ExitCode, result.StandardError);

    private static string ExitDetail(int? exitCode, string standardError)
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
}
