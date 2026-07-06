using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia.Recording;

internal sealed class AvaloniaWindowsRecordingCaptureBackend : ISlideShowRecordingCaptureBackend
{
    internal const string HostName = "Avalonia slideshow";
    internal const string AdapterName = "Avalonia Windows microphone capture adapter";
    private const string PackageRoot = "ppt/media/freep-recordings/avalonia";
    private const string NoDevicesReason = "No Windows microphone devices were reported by the host OS.";
    private const string CameraDeferredReason = "Avalonia Windows camera capture is not implemented yet; microphone narration is available when a Windows microphone is present.";

    private readonly IAvaloniaWindowsRecordingCaptureEngine _captureEngine;

    public AvaloniaWindowsRecordingCaptureBackend()
        : this(new AvaloniaWindowsRecordingDeviceCatalog(), new AvaloniaWindowsRecordingCaptureEngine())
    {
    }

    internal AvaloniaWindowsRecordingCaptureBackend(
        IAvaloniaWindowsRecordingDeviceCatalog deviceCatalog,
        IAvaloniaWindowsRecordingCaptureEngine captureEngine)
    {
        ArgumentNullException.ThrowIfNull(deviceCatalog);
        ArgumentNullException.ThrowIfNull(captureEngine);

        _captureEngine = captureEngine;
        AdapterReadiness = BuildReadiness(deviceCatalog);
        Capabilities = SlideShowRecordingCaptureAdapterPlanner.BuildCapabilities(AdapterReadiness);
    }

    public SlideShowRecordingHostCapabilities Capabilities { get; }

    public SlideShowRecordingCaptureAdapterReadiness AdapterReadiness { get; }

    public void BeginCapture(SlideShowRecordingCaptureStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Kind != SlideShowRecordingMediaArtifactKind.NarrationAudio ||
            !AdapterReadiness.CanCaptureNarration)
        {
            return;
        }

        var device = AdapterReadiness.Devices.First(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Microphone &&
            device.IsAvailable);
        _captureEngine.BeginCapture(new AvaloniaWindowsRecordingCaptureStartRequest(
            device,
            request.SlideIndex,
            request.StartedAtUtc,
            NormalizePackagePath(request.SuggestedFileName, ".wav")));
    }

    public SlideShowRecordingCaptureResult CompleteCapture(SlideShowRecordingCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Kind == SlideShowRecordingMediaArtifactKind.CameraVideo)
            return SlideShowRecordingCaptureResult.Deferred(CameraDeferredReason);

        if (!AdapterReadiness.CanCaptureNarration)
            return SlideShowRecordingCaptureResult.Deferred($"{AdapterName}: {AdapterReadiness.UnavailableReason}");

        var device = AdapterReadiness.Devices.First(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Microphone &&
            device.IsAvailable);
        var capture = _captureEngine.CompleteCapture(new AvaloniaWindowsRecordingCaptureRequest(
            device,
            request.SlideIndex,
            request.DurationMs,
            NormalizePackagePath(request.SuggestedFileName, ".wav")));

        if (!capture.IsCaptured)
            return SlideShowRecordingCaptureResult.Deferred(capture.StatusText);

        var fileName = capture.PackagePath.Split('/').Last();
        return SlideShowRecordingCaptureResult.Captured(
            capture.StatusText,
            capture.PackagePath,
            capture.PayloadBytes.Length,
            Convert.ToHexString(SHA256.HashData(capture.PayloadBytes)).ToLowerInvariant(),
            capture.PayloadBytes,
            fileName,
            "audio/wav");
    }

    private static SlideShowRecordingCaptureAdapterReadiness BuildReadiness(
        IAvaloniaWindowsRecordingDeviceCatalog deviceCatalog)
    {
        try
        {
            var devices = deviceCatalog.EnumerateDevices()
                .Where(device => device.Kind == SlideShowRecordingCaptureDeviceKind.Microphone)
                .ToArray();
            return SlideShowRecordingCaptureAdapterReadiness.FromDevices(
                HostName,
                AdapterName,
                devices,
                requiresUserPermission: true,
                devices.Any(device => device.IsAvailable) ? CameraDeferredReason : NoDevicesReason);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return SlideShowRecordingCaptureAdapterReadiness.FromDevices(
                HostName,
                AdapterName,
                Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>(),
                requiresUserPermission: true,
                $"Windows microphone enumeration failed: {ex.Message}");
        }
    }

    private static string NormalizePackagePath(string suggestedFileName, string extension)
    {
        var fileName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? "slide-narration" + extension
            : suggestedFileName.Trim().Replace('\\', '/').Split('/').Last();
        fileName = Path.ChangeExtension(fileName, extension);

        return $"{PackageRoot}/{fileName}";
    }
}

internal sealed record AvaloniaWindowsRecordingCaptureStartRequest(
    SlideShowRecordingCaptureDeviceDescriptor Device,
    int SlideIndex,
    DateTimeOffset StartedAtUtc,
    string PackagePath);

internal sealed record AvaloniaWindowsRecordingCaptureRequest(
    SlideShowRecordingCaptureDeviceDescriptor Device,
    int SlideIndex,
    int DurationMs,
    string PackagePath);

internal sealed record AvaloniaWindowsRecordingCaptureResult(
    bool IsCaptured,
    string StatusText,
    string PackagePath,
    byte[] PayloadBytes)
{
    public static AvaloniaWindowsRecordingCaptureResult Deferred(string statusText) =>
        new(false, statusText, string.Empty, Array.Empty<byte>());

    public static AvaloniaWindowsRecordingCaptureResult Captured(
        string statusText,
        string packagePath,
        byte[] payloadBytes) =>
        new(true, statusText, packagePath, payloadBytes);
}

internal interface IAvaloniaWindowsRecordingCaptureEngine
{
    void BeginCapture(AvaloniaWindowsRecordingCaptureStartRequest request);

    AvaloniaWindowsRecordingCaptureResult CompleteCapture(AvaloniaWindowsRecordingCaptureRequest request);
}

internal sealed class AvaloniaWindowsRecordingCaptureEngine : IAvaloniaWindowsRecordingCaptureEngine
{
    private readonly Dictionary<(string DeviceId, int SlideIndex), ActiveCapture> _activeCaptures = new();

    public void BeginCapture(AvaloniaWindowsRecordingCaptureStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = Key(request.Device, request.SlideIndex);
        if (_activeCaptures.TryGetValue(key, out var existing))
        {
            existing.Dispose();
            _activeCaptures.Remove(key);
        }

        if (!OperatingSystem.IsWindows())
            return;

        var alias = "freep_rec_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var path = Path.Combine(Path.GetTempPath(), alias + ".wav");
        try
        {
            MciSend($"open new type waveaudio alias {alias}");
            MciSend($"set {alias} time format milliseconds");
            MciSend($"set {alias} bitspersample 16 channels 1 samplespersec 16000");
            MciSend($"record {alias}");
            _activeCaptures[key] = new ActiveCapture(alias, path, request.PackagePath, request.StartedAtUtc);
        }
        catch
        {
            TryClose(alias);
            TryDelete(path);
        }
    }

    public AvaloniaWindowsRecordingCaptureResult CompleteCapture(AvaloniaWindowsRecordingCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = Key(request.Device, request.SlideIndex);
        if (!_activeCaptures.Remove(key, out var capture))
        {
            return AvaloniaWindowsRecordingCaptureResult.Deferred(
                $"{AvaloniaWindowsRecordingCaptureBackend.AdapterName}: narration capture was not started for slide {request.SlideIndex + 1}");
        }

        try
        {
            MciSend($"stop {capture.Alias}");
            MciSend($"save {capture.Alias} \"{capture.TempPath}\"");
            MciSend($"close {capture.Alias}");
            if (!File.Exists(capture.TempPath))
            {
                return AvaloniaWindowsRecordingCaptureResult.Deferred(
                    $"{AvaloniaWindowsRecordingCaptureBackend.AdapterName}: Windows did not produce a narration file.");
            }

            var payload = File.ReadAllBytes(capture.TempPath);
            if (payload.Length == 0)
            {
                return AvaloniaWindowsRecordingCaptureResult.Deferred(
                    $"{AvaloniaWindowsRecordingCaptureBackend.AdapterName}: Windows produced an empty narration file.");
            }

            return AvaloniaWindowsRecordingCaptureResult.Captured(
                $"{AvaloniaWindowsRecordingCaptureBackend.AdapterName}: narration captured from {request.Device.DisplayName} to {capture.PackagePath}",
                capture.PackagePath,
                payload);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return AvaloniaWindowsRecordingCaptureResult.Deferred(
                $"{AvaloniaWindowsRecordingCaptureBackend.AdapterName}: Windows narration capture failed: {ex.Message}");
        }
        finally
        {
            TryClose(capture.Alias);
            TryDelete(capture.TempPath);
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
        try
        {
            _ = mciSendString($"close {alias}", null, 0, IntPtr.Zero);
        }
        catch
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
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
        string TempPath,
        string PackagePath,
        DateTimeOffset StartedAtUtc) : IDisposable
    {
        public void Dispose()
        {
            TryClose(Alias);
            TryDelete(TempPath);
        }
    }
}
