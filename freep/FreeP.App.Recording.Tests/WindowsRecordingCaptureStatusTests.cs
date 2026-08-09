using System.Runtime.InteropServices;
using FreeP.App.Recording;

namespace FreeP.App.Recording.Tests;

public sealed class WindowsRecordingCaptureStatusTests
{
    [Fact]
    public void InitializationFailure_ExplainsPermissionDenial()
    {
        var status = WindowsRecordingCaptureStatus.InitializationFailure(
            "WPF Windows recording capture adapter",
            "camera",
            "Presenter camera",
            new UnauthorizedAccessException("Access denied."));

        status.Should().Contain("permission was denied");
        status.Should().Contain("Enable camera access in Windows Privacy settings");
        status.Should().Contain("Presenter camera");
    }

    [Fact]
    public void InitializationFailure_ExplainsPrivacyPolicyBlock()
    {
        var status = WindowsRecordingCaptureStatus.InitializationFailure(
            "Avalonia Windows recording capture adapter",
            "microphone",
            "Studio microphone",
            new COMException("Blocked by policy.", unchecked((int)0x800704EC)));

        status.Should().Contain("privacy policy blocked microphone access");
        status.Should().Contain("Enable microphone access in Windows Privacy settings");
    }

    [Fact]
    public void CompletionFailure_PreservesCancellationAsNonError()
    {
        var status = WindowsRecordingCaptureStatus.CompletionFailure(
            "WPF Windows recording capture adapter",
            "camera",
            "Presenter camera",
            new OperationCanceledException());

        status.Should().Be("WPF Windows recording capture adapter: Windows camera capture was canceled for 'Presenter camera'.");
    }

    [Fact]
    public void CompletionFailure_PreservesGenericDeviceDetail()
    {
        var status = WindowsRecordingCaptureStatus.CompletionFailure(
            "WPF Windows recording capture adapter",
            "camera",
            "Presenter camera",
            new InvalidOperationException("The device was removed."));

        status.Should().Contain("Windows camera capture failed for 'Presenter camera'");
        status.Should().Contain("The device was removed.");
    }
}
