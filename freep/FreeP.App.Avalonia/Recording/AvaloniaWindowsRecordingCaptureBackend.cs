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
    internal const string AdapterName = "Avalonia Windows recording capture adapter";
    private const string PackageRoot = "ppt/media/freep-recordings/avalonia";
    private const string NoDevicesReason = "No Windows microphone or camera devices were reported by the host OS.";

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

        var canCapture = request.Kind switch
        {
            SlideShowRecordingMediaArtifactKind.NarrationAudio => AdapterReadiness.CanCaptureNarration,
            SlideShowRecordingMediaArtifactKind.CameraVideo => AdapterReadiness.CanCaptureCamera,
            _ => false
        };
        if (!canCapture)
        {
            return;
        }

        var device = AdapterReadiness.Devices.First(device =>
            device.Kind == DeviceKind(request.Kind) &&
            device.IsAvailable);
        _captureEngine.BeginCapture(new AvaloniaWindowsRecordingCaptureStartRequest(
            device,
            request.SlideIndex,
            request.StartedAtUtc,
            NormalizePackagePath(request.SuggestedFileName, Extension(request.Kind))));
    }

    public SlideShowRecordingCaptureResult CompleteCapture(SlideShowRecordingCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canCapture = request.Kind switch
        {
            SlideShowRecordingMediaArtifactKind.NarrationAudio => AdapterReadiness.CanCaptureNarration,
            SlideShowRecordingMediaArtifactKind.CameraVideo => AdapterReadiness.CanCaptureCamera,
            _ => false
        };
        if (!canCapture)
            return SlideShowRecordingCaptureResult.Deferred($"{AdapterName}: {AdapterReadiness.UnavailableReason}");

        var device = AdapterReadiness.Devices.First(device =>
            device.Kind == DeviceKind(request.Kind) &&
            device.IsAvailable);
        var capture = _captureEngine.CompleteCapture(new AvaloniaWindowsRecordingCaptureRequest(
            device,
            request.SlideIndex,
            request.DurationMs,
            NormalizePackagePath(request.SuggestedFileName, Extension(request.Kind))));

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
            ContentType(request.Kind));
    }

    private static SlideShowRecordingCaptureAdapterReadiness BuildReadiness(
        IAvaloniaWindowsRecordingDeviceCatalog deviceCatalog)
    {
        try
        {
            var devices = deviceCatalog.EnumerateDevices()
                .Where(device => device.Kind is SlideShowRecordingCaptureDeviceKind.Microphone
                    or SlideShowRecordingCaptureDeviceKind.Camera)
                .ToArray();
            return SlideShowRecordingCaptureAdapterReadiness.FromDevices(
                HostName,
                AdapterName,
                devices,
                requiresUserPermission: true,
                devices.Any(device => device.IsAvailable) ? MissingDeviceReason(devices) : NoDevicesReason);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return SlideShowRecordingCaptureAdapterReadiness.FromDevices(
                HostName,
                AdapterName,
                Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>(),
                requiresUserPermission: true,
                $"Windows recording device enumeration failed: {ex.Message}");
        }
    }

    private static string MissingDeviceReason(IReadOnlyList<SlideShowRecordingCaptureDeviceDescriptor> devices)
    {
        var hasMicrophone = devices.Any(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Microphone &&
            device.IsAvailable);
        var hasCamera = devices.Any(device =>
            device.Kind == SlideShowRecordingCaptureDeviceKind.Camera &&
            device.IsAvailable);

        return (hasMicrophone, hasCamera) switch
        {
            (true, true) => string.Empty,
            (true, false) => "No Windows camera devices were reported by the host OS.",
            (false, true) => "No Windows microphone devices were reported by the host OS.",
            _ => NoDevicesReason
        };
    }

    private static SlideShowRecordingCaptureDeviceKind DeviceKind(SlideShowRecordingMediaArtifactKind kind) =>
        kind == SlideShowRecordingMediaArtifactKind.NarrationAudio
            ? SlideShowRecordingCaptureDeviceKind.Microphone
            : SlideShowRecordingCaptureDeviceKind.Camera;

    private static string Extension(SlideShowRecordingMediaArtifactKind kind) =>
        kind == SlideShowRecordingMediaArtifactKind.NarrationAudio ? ".wav" : ".mp4";

    private static string ContentType(SlideShowRecordingMediaArtifactKind kind) =>
        kind == SlideShowRecordingMediaArtifactKind.NarrationAudio ? "audio/wav" : "video/mp4";

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

        if (request.Device.Kind == SlideShowRecordingCaptureDeviceKind.Camera)
        {
            _activeCaptures[key] = new ActiveCapture(
                string.Empty,
                string.Empty,
                request.PackagePath,
                request.StartedAtUtc,
                request.Device.Kind);
            return;
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
            _activeCaptures[key] = new ActiveCapture(
                alias,
                path,
                request.PackagePath,
                request.StartedAtUtc,
                request.Device.Kind);
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
            if (capture.Kind == SlideShowRecordingCaptureDeviceKind.Camera)
            {
                return AvaloniaWindowsRecordingCaptureResult.Deferred(
                    $"{AvaloniaWindowsRecordingCaptureBackend.AdapterName}: camera device handoff reached for {request.Device.DisplayName}, but local video encoding is not implemented in this no-COM adapter.");
            }

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

    private static void TryDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

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
        DateTimeOffset StartedAtUtc,
        SlideShowRecordingCaptureDeviceKind Kind) : IDisposable
    {
        public void Dispose()
        {
            TryClose(Alias);
            TryDelete(TempPath);
        }
    }
}
