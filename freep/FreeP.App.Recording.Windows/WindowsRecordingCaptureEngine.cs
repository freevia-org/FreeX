using System.Runtime.InteropServices;
using System.Text;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.App.Recording;

namespace FreeP.App.Recording.Windows;

public sealed class WindowsRecordingCaptureEngine : IWindowsRecordingCaptureEngine
{
    private readonly Dictionary<(string DeviceId, int SlideIndex), ActiveCapture> _activeCaptures = new();
    private readonly string _adapterName;

    public WindowsRecordingCaptureEngine(string adapterName = "Windows recording capture adapter")
    {
        _adapterName = string.IsNullOrWhiteSpace(adapterName)
            ? "Windows recording capture adapter"
            : adapterName.Trim();
    }

    public void BeginCapture(WindowsRecordingCaptureStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = Key(request.Device, request.SlideIndex);
        if (_activeCaptures.TryGetValue(key, out var existing))
        {
            existing.Dispose();
            _activeCaptures.Remove(key);
        }

        if (request.Device.Kind == SlideShowRecordingCaptureDeviceKind.Camera)
        {
            _activeCaptures[key] = new ActiveCapture(
                string.Empty,
                null,
                request.PackagePath,
                request.StartedAtUtc,
                request.Device.Kind);
            return;
        }

        if (!OperatingSystem.IsWindows())
            return;

        TemporaryFileLease? temporaryFile = null;
        var alias = string.Empty;
        try
        {
            temporaryFile = TemporaryFileLease.CreateForExternalWriter("freep_rec_", ".wav");
            alias = Path.GetFileNameWithoutExtension(temporaryFile.Path);
            MciSend($"open new type waveaudio alias {alias}");
            MciSend($"set {alias} time format milliseconds");
            MciSend($"set {alias} bitspersample 16 channels 1 samplespersec 16000");
            MciSend($"record {alias}");
            _activeCaptures[key] = new ActiveCapture(
                alias,
                temporaryFile,
                request.PackagePath,
                request.StartedAtUtc,
                request.Device.Kind);
        }
        catch
        {
            TryClose(alias);
            temporaryFile?.Dispose();
        }
    }

    public WindowsRecordingCaptureResult CompleteCapture(WindowsRecordingCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = Key(request.Device, request.SlideIndex);
        if (!_activeCaptures.Remove(key, out var capture))
        {
            return WindowsRecordingCaptureResult.Deferred(
                $"{_adapterName}: narration capture was not started for slide {request.SlideIndex + 1}");
        }

        try
        {
            if (capture.Kind == SlideShowRecordingCaptureDeviceKind.Camera)
            {
                return WindowsRecordingCaptureResult.Deferred(
                    $"{_adapterName}: camera device handoff reached for {request.Device.DisplayName}, but local video encoding is not implemented in this no-COM adapter.");
            }

            var temporaryFile = capture.TempFile;
            if (temporaryFile is null)
            {
                return WindowsRecordingCaptureResult.Deferred(
                    $"{_adapterName}: narration capture has no temporary output file.");
            }

            MciSend($"stop {capture.Alias}");
            MciSend($"save {capture.Alias} \"{temporaryFile.Path}\"");
            MciSend($"close {capture.Alias}");
            if (!File.Exists(temporaryFile.Path))
            {
                return WindowsRecordingCaptureResult.Deferred(
                    $"{_adapterName}: Windows did not produce a narration file.");
            }

            var payload = File.ReadAllBytes(temporaryFile.Path);
            if (payload.Length == 0)
            {
                return WindowsRecordingCaptureResult.Deferred(
                    $"{_adapterName}: Windows produced an empty narration file.");
            }

            return WindowsRecordingCaptureResult.Captured(
                $"{_adapterName}: narration captured from {request.Device.DisplayName} to {capture.PackagePath}",
                capture.PackagePath,
                payload);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return WindowsRecordingCaptureResult.Deferred(
                $"{_adapterName}: Windows narration capture failed: {ex.Message}");
        }
        finally
        {
            TryClose(capture.Alias);
            capture.TempFile?.Dispose();
        }
    }

    private static (string DeviceId, int SlideIndex) Key(
        SlideShowRecordingCaptureDeviceDescriptor device,
        int slideIndex) =>
        (device.DeviceId, slideIndex);

    private static void MciSend(string command)
    {
        var error = mciSendString(command, null, 0, IntPtr.Zero);
        if (error != 0)
        {
            var message = new StringBuilder(256);
            _ = mciGetErrorString(error, message, message.Capacity);
            throw new InvalidOperationException(message.Length == 0 ? $"MCI error {error}" : message.ToString());
        }
    }

    private static void TryClose(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            return;

        try
        {
            _ = mciSendString($"close {alias}", null, 0, IntPtr.Zero);
        }
        catch
        {
        }
    }

    [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode)]
    private static extern uint mciSendString(
        string command,
        StringBuilder? returnValue,
        int returnLength,
        IntPtr callback);

    [DllImport("winmm.dll", EntryPoint = "mciGetErrorStringW", CharSet = CharSet.Unicode)]
    private static extern bool mciGetErrorString(
        uint error,
        StringBuilder errorText,
        int errorTextLength);

    private sealed record ActiveCapture(
        string Alias,
        TemporaryFileLease? TempFile,
        string PackagePath,
        DateTimeOffset StartedAtUtc,
        SlideShowRecordingCaptureDeviceKind Kind) : IDisposable
    {
        public void Dispose()
        {
            TryClose(Alias);
            TempFile?.Dispose();
        }
    }
}
