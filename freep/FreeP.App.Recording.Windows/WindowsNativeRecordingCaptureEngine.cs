using FreeP.App.Compositor;
using FreeP.App.Recording;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace FreeP.App.Recording.Windows;

/// <summary>
/// Uses the Windows Runtime camera stack for local MP4 capture and the shared MCI path for narration.
/// WinRT work is run on a thread-pool thread, which keeps its awaits off the dispatcher and avoids a
/// SynchronizationContext deadlock. The synchronous recording contract still means
/// <see cref="BeginCapture"/> and <see cref="CompleteCapture"/> block their caller until camera init and
/// teardown finish -- but that wait is bounded by <see cref="_deviceOperationTimeout"/>
/// (<see cref="DefaultDeviceOperationTimeout"/> in production). A driver/device that never answers
/// (claimed by another app, a wedged USB webcam driver, etc.) previously hung the caller -- typically the
/// UI thread, since <see cref="Recording.WindowsRecordingCaptureBackend"/> is invoked synchronously from
/// the slide-show recording UI -- forever, with no way to recover short of killing the process. Now the
/// call unblocks after the timeout and the failure is reported through the same
/// <see cref="_captureFailures"/> degrade path an outright device error already used: the user sees the
/// affected slide's camera capture reported as failed/deferred (recording continues for narration and any
/// other slides) instead of the whole app freezing.
/// </summary>
public class WindowsNativeRecordingCaptureEngine : IWindowsRecordingCaptureEngine
{
    internal static readonly TimeSpan DefaultDeviceOperationTimeout = TimeSpan.FromSeconds(15);

    private readonly WindowsRecordingCaptureEngine _narrationEngine;
    private readonly string _adapterName;
    private readonly Dictionary<(string DeviceId, int SlideIndex), ActiveCameraCapture> _activeCaptures = new();
    private readonly Dictionary<(string DeviceId, int SlideIndex), string> _captureFailures = new();
    private readonly Func<WindowsRecordingCaptureStartRequest, Task<ActiveCameraCapture>> _startCamera;
    private readonly Func<ActiveCameraCapture, Task<byte[]>> _stopCamera;
    private readonly TimeSpan _deviceOperationTimeout;

    public WindowsNativeRecordingCaptureEngine(string adapterName)
        : this(adapterName, StartCameraAsync, StopCameraAsync, DefaultDeviceOperationTimeout)
    {
    }

    /// <summary>
    /// Test seam: lets tests stand in for the real WinRT camera calls (which cannot be made to hang
    /// deterministically in CI) and shrink the timeout so the bound is exercised in milliseconds instead of
    /// <see cref="DefaultDeviceOperationTimeout"/>'s 15 seconds. Production always uses the public
    /// constructor above, which wires the real <see cref="StartCameraAsync"/>/<see cref="StopCameraAsync"/>
    /// WinRT calls and the real timeout.
    /// </summary>
    internal WindowsNativeRecordingCaptureEngine(
        string adapterName,
        Func<WindowsRecordingCaptureStartRequest, Task<ActiveCameraCapture>> startCamera,
        Func<ActiveCameraCapture, Task<byte[]>> stopCamera,
        TimeSpan deviceOperationTimeout)
    {
        ArgumentNullException.ThrowIfNull(startCamera);
        ArgumentNullException.ThrowIfNull(stopCamera);

        _adapterName = string.IsNullOrWhiteSpace(adapterName)
            ? "Windows recording capture adapter"
            : adapterName.Trim();
        _narrationEngine = new WindowsRecordingCaptureEngine(_adapterName);
        _startCamera = startCamera;
        _stopCamera = stopCamera;
        _deviceOperationTimeout = deviceOperationTimeout;
    }

    public void BeginCapture(WindowsRecordingCaptureStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Device.Kind != SlideShowRecordingCaptureDeviceKind.Camera)
        {
            _narrationEngine.BeginCapture(request);
            return;
        }

        var key = Key(request.Device, request.SlideIndex);
        if (_activeCaptures.Remove(key, out var previous))
            previous.Dispose();
        _captureFailures.Remove(key);

        try
        {
            _activeCaptures[key] = RunAsync(() => _startCamera(request));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _captureFailures[key] = WindowsRecordingCaptureStatus.InitializationFailure(
                _adapterName,
                "camera",
                request.Device.DisplayName,
                ex);
        }
    }

    public WindowsRecordingCaptureResult CompleteCapture(WindowsRecordingCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Device.Kind != SlideShowRecordingCaptureDeviceKind.Camera)
            return _narrationEngine.CompleteCapture(request);

        var key = Key(request.Device, request.SlideIndex);
        if (!_activeCaptures.Remove(key, out var capture))
        {
            if (_captureFailures.Remove(key, out var failure))
            {
                return WindowsRecordingCaptureResult.Deferred(
                    failure);
            }

            return WindowsRecordingCaptureResult.Deferred(
                $"{_adapterName}: camera capture was not started for slide {request.SlideIndex + 1}");
        }

        try
        {
            var payload = RunAsync(() => _stopCamera(capture));
            if (payload.Length == 0)
            {
                return WindowsRecordingCaptureResult.Deferred(
                    $"{_adapterName}: Windows produced an empty camera recording.");
            }

            return WindowsRecordingCaptureResult.Captured(
                $"{_adapterName}: camera captured from {request.Device.DisplayName} to {capture.PackagePath}",
                capture.PackagePath,
                payload);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return WindowsRecordingCaptureResult.Deferred(
                WindowsRecordingCaptureStatus.CompletionFailure(
                    _adapterName,
                    "camera",
                    request.Device.DisplayName,
                    ex));
        }
        finally
        {
            capture.Dispose();
        }
    }

    private static async Task<ActiveCameraCapture> StartCameraAsync(
        WindowsRecordingCaptureStartRequest request)
    {
        var mediaCapture = new MediaCapture();
        try
        {
            var settings = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = await ResolveDeviceIdAsync(request.Device).ConfigureAwait(false),
                StreamingCaptureMode = StreamingCaptureMode.Video
            };
            await mediaCapture.InitializeAsync(settings).AsTask().ConfigureAwait(false);

            var stream = new InMemoryRandomAccessStream();
            try
            {
                await mediaCapture.StartRecordToStreamAsync(
                    MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto),
                    stream).AsTask().ConfigureAwait(false);
            }
            catch
            {
                stream.Dispose();
                throw;
            }

            return new ActiveCameraCapture(mediaCapture, stream, request.PackagePath);
        }
        catch
        {
            mediaCapture.Dispose();
            throw;
        }
    }

    private static async Task<byte[]> StopCameraAsync(ActiveCameraCapture capture)
    {
        await capture.MediaCapture.StopRecordAsync().AsTask().ConfigureAwait(false);
        capture.Stream.Seek(0);

        var length = checked((uint)capture.Stream.Size);
        if (length == 0)
            return Array.Empty<byte>();

        using var reader = new DataReader(capture.Stream.GetInputStreamAt(0));
        await reader.LoadAsync(length).AsTask().ConfigureAwait(false);
        var payload = new byte[length];
        reader.ReadBytes(payload);
        return payload;
    }

    private static async Task<string> ResolveDeviceIdAsync(
        SlideShowRecordingCaptureDeviceDescriptor requestedDevice)
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture)
            .AsTask()
            .ConfigureAwait(false);
        var device = devices.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, requestedDevice.DeviceId, StringComparison.OrdinalIgnoreCase))
            ?? devices.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, requestedDevice.DisplayName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"The requested camera '{requestedDevice.DisplayName}' is no longer available.");

        return device.Id;
    }

    private static (string DeviceId, int SlideIndex) Key(
        SlideShowRecordingCaptureDeviceDescriptor device,
        int slideIndex) =>
        (device.DeviceId, slideIndex);

    /// <summary>
    /// Runs <paramref name="operation"/> on a thread-pool thread and blocks the caller for at most
    /// <see cref="_deviceOperationTimeout"/>. Previously this waited on the antecedent task with no bound
    /// at all, so a WinRT call that never completes (device claimed by another app, a wedged driver) hung
    /// the caller -- typically the UI thread -- forever. The bound does not cancel the underlying WinRT
    /// call (there is no cancellation token to thread through <see cref="MediaCapture"/>'s APIs), so a
    /// genuinely wedged call keeps running orphaned in the background; what changes is that the caller is
    /// no longer held hostage to it and the timeout is reported as a normal capture failure through the
    /// existing <see cref="_captureFailures"/> degrade path.
    /// </summary>
    private T RunAsync<T>(Func<Task<T>> operation)
    {
        var task = Task.Run(operation);

        // Task.WaitAny (unlike Task.Wait) does not itself throw when the task faults inside the
        // timeout window -- it just reports that the task completed, so the exception can still be
        // unwrapped normally via GetAwaiter().GetResult() below instead of surfacing as an
        // AggregateException.
        var completedIndex = Task.WaitAny([task], _deviceOperationTimeout);
        if (completedIndex == -1)
        {
            throw new TimeoutException(
                $"{_adapterName}: the Windows camera capture device did not respond within " +
                $"{_deviceOperationTimeout.TotalSeconds:0.#}s.");
        }

        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Internal (not private) so it can appear in the internal test-seam constructor's
    /// <c>Func&lt;..., Task&lt;ActiveCameraCapture&gt;&gt;</c> parameter types above.
    /// </summary>
    internal sealed class ActiveCameraCapture : IDisposable
    {
        public ActiveCameraCapture(
            MediaCapture mediaCapture,
            InMemoryRandomAccessStream stream,
            string packagePath)
        {
            MediaCapture = mediaCapture;
            Stream = stream;
            PackagePath = packagePath;
        }

        public MediaCapture MediaCapture { get; }
        public InMemoryRandomAccessStream Stream { get; }
        public string PackagePath { get; }

        public void Dispose()
        {
            Stream.Dispose();
            MediaCapture.Dispose();
        }
    }
}
