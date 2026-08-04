using FreeP.App.Compositor;
using FreeP.App.Recording;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace FreeP.App.Recording.Windows;

/// <summary>
/// Uses the Windows Runtime camera stack for local MP4 capture and the shared MCI path for narration.
/// WinRT work is run on a thread-pool thread so the synchronous recording contract never blocks a UI
/// dispatcher.
/// </summary>
public class WindowsNativeRecordingCaptureEngine : IWindowsRecordingCaptureEngine
{
    private readonly WindowsRecordingCaptureEngine _narrationEngine;
    private readonly string _adapterName;
    private readonly Dictionary<(string DeviceId, int SlideIndex), ActiveCameraCapture> _activeCaptures = new();
    private readonly Dictionary<(string DeviceId, int SlideIndex), string> _captureFailures = new();

    public WindowsNativeRecordingCaptureEngine(string adapterName)
    {
        _adapterName = string.IsNullOrWhiteSpace(adapterName)
            ? "Windows recording capture adapter"
            : adapterName.Trim();
        _narrationEngine = new WindowsRecordingCaptureEngine(_adapterName);
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
            _activeCaptures[key] = RunAsync(() => StartCameraAsync(request));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _captureFailures[key] = ex.Message;
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
                    $"{_adapterName}: Windows camera initialization failed: {failure}");
            }

            return WindowsRecordingCaptureResult.Deferred(
                $"{_adapterName}: camera capture was not started for slide {request.SlideIndex + 1}");
        }

        try
        {
            var payload = RunAsync(() => StopCameraAsync(capture));
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
                $"{_adapterName}: Windows camera capture failed: {ex.Message}");
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

    private static T RunAsync<T>(Func<Task<T>> operation) =>
        Task.Run(operation).GetAwaiter().GetResult();

    private sealed class ActiveCameraCapture : IDisposable
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
