using System.Runtime.InteropServices;

namespace FreeP.App.Recording;

/// <summary>
/// Converts native Windows capture failures into actionable presenter status text.
/// The capture engines still own the operation and payload lifecycle; this helper only
/// preserves the failure category that the UI needs to explain what happened.
/// </summary>
public static class WindowsRecordingCaptureStatus
{
    private const int ErrorAccessDenied = unchecked((int)0x80070005);
    private const int ErrorPrivacyPolicyBlocked = unchecked((int)0x800704EC);

    public static string InitializationFailure(
        string adapterName,
        string streamName,
        string deviceName,
        Exception exception) =>
        Format(adapterName, streamName, deviceName, exception, "initialization");

    public static string CompletionFailure(
        string adapterName,
        string streamName,
        string deviceName,
        Exception exception) =>
        Format(adapterName, streamName, deviceName, exception, "capture");

    private static string Format(
        string adapterName,
        string streamName,
        string deviceName,
        Exception exception,
        string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamName);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        ArgumentNullException.ThrowIfNull(exception);

        var adapter = adapterName.Trim();
        var stream = streamName.Trim();
        var device = deviceName.Trim();

        if (IsPrivacyDenied(exception))
        {
            return $"{adapter}: Windows {stream} permission was denied while {operation} on '{device}'. " +
                $"Enable {stream} access in Windows Privacy settings and retry.";
        }

        if (IsPrivacyPolicyBlocked(exception))
        {
            return $"{adapter}: Windows privacy policy blocked {stream} access while {operation} on '{device}'. " +
                $"Enable {stream} access in Windows Privacy settings and retry.";
        }

        if (exception is OperationCanceledException)
        {
            return $"{adapter}: Windows {stream} capture was canceled for '{device}'.";
        }

        return $"{adapter}: Windows {stream} {operation} failed for '{device}': {exception.Message}";
    }

    private static bool IsPrivacyDenied(Exception exception) =>
        exception is UnauthorizedAccessException ||
        exception is COMException { HResult: ErrorAccessDenied };

    private static bool IsPrivacyPolicyBlocked(Exception exception) =>
        exception is COMException { HResult: ErrorPrivacyPolicyBlocked };
}
