using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.App.Recording.Windows;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Round 139 / sweep78-3: <see cref="WindowsNativeRecordingCaptureEngine"/> used to block its caller
/// (<c>BeginCapture</c>/<c>CompleteCapture</c> -- invoked synchronously from the slide-show recording UI
/// thread via <see cref="WindowsRecordingCaptureBackend"/>) on the underlying WinRT camera call with no
/// bound at all: a malfunctioning driver, or a camera claimed by another app, hung the caller forever.
/// <c>RunAsync</c> now bounds that wait; a timed-out operation degrades through the same "camera capture
/// failed" path a real device error already used, instead of freezing the app.
///
/// The real WinRT <c>MediaCapture</c> APIs cannot be made to hang deterministically in CI, so these tests
/// use the internal test-seam constructor to substitute a controllable, finite-delay operation while still
/// driving the real public entry points a user reaches (<c>BeginCapture</c>/<c>CompleteCapture</c>), not
/// the timeout helper directly.
/// </summary>
public sealed class WindowsNativeRecordingCaptureEngineTimeoutTests
{
    private static SlideShowRecordingCaptureDeviceDescriptor CameraDevice(string id = "cam-1") =>
        new(
            SlideShowRecordingCaptureDeviceKind.Camera,
            id,
            "Test Camera",
            IsDefault: true,
            IsAvailable: true,
            ContentType: "video/mp4");

    private static WindowsRecordingCaptureStartRequest StartRequest(
        SlideShowRecordingCaptureDeviceDescriptor device,
        string packagePath) =>
        new(device, SlideIndex: 0, StartedAtUtc: DateTimeOffset.UtcNow, packagePath);

    private static WindowsRecordingCaptureRequest CompleteRequest(
        SlideShowRecordingCaptureDeviceDescriptor device,
        string packagePath) =>
        new(device, SlideIndex: 0, DurationMs: 1000, packagePath);

    private static WindowsNativeRecordingCaptureEngine.ActiveCameraCapture FakeCapture(string packagePath) =>
        new(
            new Windows.Media.Capture.MediaCapture(),
            new Windows.Storage.Streams.InMemoryRandomAccessStream(),
            packagePath);

    // The defect: BeginCapture must not stay blocked for however long the underlying camera call takes.
    // Here the fake "camera" takes a real, finite 1 second -- far longer than the 150ms timeout configured
    // below -- so a correct engine returns after ~150ms with a recorded failure, while the pre-fix engine
    // (unbounded Task.Run(...).GetAwaiter().GetResult()) blocks for the full ~1 second and succeeds.
    [Fact]
    public void BeginCapture_bounds_the_wait_when_the_camera_device_never_answers_in_time()
    {
        var device = CameraDevice();
        const string packagePath = "ppt/media/freep-recordings/windows/cam-slow.mp4";
        var engine = new WindowsNativeRecordingCaptureEngine(
            "test-adapter",
            startCamera: async request =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                return FakeCapture(request.PackagePath);
            },
            stopCamera: _ => Task.FromResult(Array.Empty<byte>()),
            deviceOperationTimeout: TimeSpan.FromMilliseconds(150));

        var stopwatch = Stopwatch.StartNew();
        engine.BeginCapture(StartRequest(device, packagePath));
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromMilliseconds(700),
            "BeginCapture must be bounded by the configured device-operation timeout, not by however " +
            "long the (here, deliberately slow) camera device takes to answer");

        // Graceful degrade: the timed-out slide's camera capture is reported as a failure (Deferred),
        // not silently dropped or left half-open.
        var completion = engine.CompleteCapture(CompleteRequest(device, packagePath));
        completion.IsCaptured.Should().BeFalse();
        completion.StatusText.Should().Contain(
            "did not respond",
            "the timeout failure text should explain what happened, matching the existing " +
            "InitializationFailure/CompletionFailure degrade path for real device errors");
    }

    // Sibling to the test above: proves bounding the wait did not break the normal/fast path -- a camera
    // operation that finishes comfortably inside the timeout still succeeds and returns the recorded
    // payload end to end through BeginCapture + CompleteCapture.
    [Fact]
    public void BeginCapture_and_CompleteCapture_still_succeed_for_a_camera_operation_within_the_timeout()
    {
        var device = CameraDevice("cam-2");
        const string packagePath = "ppt/media/freep-recordings/windows/cam-fast.mp4";
        var payload = new byte[] { 1, 2, 3, 4 };
        var engine = new WindowsNativeRecordingCaptureEngine(
            "test-adapter",
            startCamera: async request =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
                return FakeCapture(request.PackagePath);
            },
            stopCamera: async _ =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
                return payload;
            },
            deviceOperationTimeout: TimeSpan.FromSeconds(2));

        engine.BeginCapture(StartRequest(device, packagePath));
        var completion = engine.CompleteCapture(CompleteRequest(device, packagePath));

        completion.IsCaptured.Should().BeTrue();
        completion.PackagePath.Should().Be(packagePath);
        completion.PayloadBytes.Should().Equal(payload);
    }
}
