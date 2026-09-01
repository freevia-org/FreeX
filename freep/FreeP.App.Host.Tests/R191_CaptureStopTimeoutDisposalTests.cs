using System;
using System.Threading;
using System.Threading.Tasks;
using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.App.Recording.Windows;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r191 (backlog item 23): <c>CompleteCapture</c> disposed the capture in its <c>finally</c> even
/// when <c>RunAsync</c> had given up waiting for the stop operation. Per RunAsync's own comment the
/// bound does not cancel the underlying WinRT call -- a wedged driver keeps running orphaned -- so
/// the still-live <c>StopRecordAsync</c> and the stream it writes into were being torn down
/// underneath it, inside the very timeout path that exists to survive a wedged driver.
///
/// r185 fixed the equivalent hazard on the START path by deferring disposal of the late-arriving
/// device. The stop path never got the same treatment; it now hands its capture to the same
/// continuation, which disposes it once the orphaned call actually finishes.
/// </summary>
public sealed class R191_CaptureStopTimeoutDisposalTests
{
    private static SlideShowRecordingCaptureDeviceDescriptor CameraDevice() =>
        new(
            SlideShowRecordingCaptureDeviceKind.Camera,
            "cam-r191",
            "Test Camera",
            IsDefault: true,
            IsAvailable: true,
            ContentType: "video/mp4");

    private static WindowsNativeRecordingCaptureEngine.ActiveCameraCapture FakeCapture(string packagePath) =>
        new(
            new Windows.Media.Capture.MediaCapture(),
            new Windows.Storage.Streams.InMemoryRandomAccessStream(),
            packagePath);

    [Fact]
    public void CompleteCapture_whenTheStopCallOverrunsTheTimeout_doesNotDisposeItWhileItIsStillRunning()
    {
        var device = CameraDevice();
        const string packagePath = "ppt/media/freep-recordings/windows/cam-r191.mp4";
        var capture = FakeCapture(packagePath);

        using var stopFinished = new ManualResetEventSlim(false);
        Exception? observedInsideStop = null;

        var engine = new WindowsNativeRecordingCaptureEngine(
            "test adapter",
            _ => Task.FromResult(capture),
            async live =>
            {
                // Outlive the 150ms bound, then touch the very objects the engine used to dispose
                // out from under this call. A disposed InMemoryRandomAccessStream throws here.
                await Task.Delay(600).ConfigureAwait(false);
                try
                {
                    _ = live.Stream.Size;
                }
                catch (Exception ex)
                {
                    observedInsideStop = ex;
                }

                stopFinished.Set();
                return new byte[] { 1, 2, 3 };
            },
            TimeSpan.FromMilliseconds(150));

        engine.BeginCapture(new WindowsRecordingCaptureStartRequest(
            device, SlideIndex: 0, StartedAtUtc: DateTimeOffset.UtcNow, packagePath));

        var result = engine.CompleteCapture(
            new WindowsRecordingCaptureRequest(device, SlideIndex: 0, DurationMs: 1000, packagePath));

        // The wait is still bounded: the user gets the degrade path, not a freeze.
        Assert.False(result.IsCaptured);

        Assert.True(stopFinished.Wait(TimeSpan.FromSeconds(10)), "the orphaned stop call never finished");
        Assert.Null(observedInsideStop);
    }

    [Fact]
    public void CompleteCapture_afterTheOrphanedStopFinishes_stillDisposesTheCapture()
    {
        // Deferring disposal must not become leaking it: the camera would stay open with its
        // indicator light on until the process exits, which is what the r185 fix exists to prevent.
        var device = CameraDevice();
        const string packagePath = "ppt/media/freep-recordings/windows/cam-r191-late.mp4";
        var capture = FakeCapture(packagePath);

        using var stopFinished = new ManualResetEventSlim(false);

        var engine = new WindowsNativeRecordingCaptureEngine(
            "test adapter",
            _ => Task.FromResult(capture),
            async _ =>
            {
                await Task.Delay(400).ConfigureAwait(false);
                stopFinished.Set();
                return new byte[] { 1 };
            },
            TimeSpan.FromMilliseconds(120));

        engine.BeginCapture(new WindowsRecordingCaptureStartRequest(
            device, SlideIndex: 0, StartedAtUtc: DateTimeOffset.UtcNow, packagePath));
        engine.CompleteCapture(
            new WindowsRecordingCaptureRequest(device, SlideIndex: 0, DurationMs: 1000, packagePath));

        Assert.True(stopFinished.Wait(TimeSpan.FromSeconds(10)));

        // The continuation runs after the task completes; poll rather than race it.
        var disposed = false;
        for (var attempt = 0; attempt < 100 && !disposed; attempt++)
        {
            try
            {
                _ = capture.Stream.Size;
                Thread.Sleep(50);
            }
            catch (Exception)
            {
                disposed = true;
            }
        }

        Assert.True(disposed, "the capture must be released once the orphaned call finishes");
    }

    [Fact]
    public void CompleteCapture_whenTheStopCallReturnsInTime_disposesImmediatelyAsBefore()
    {
        // The ordinary path is unchanged: no deferral, disposal happens before CompleteCapture
        // returns.
        var device = CameraDevice();
        const string packagePath = "ppt/media/freep-recordings/windows/cam-r191-fast.mp4";
        var capture = FakeCapture(packagePath);

        var engine = new WindowsNativeRecordingCaptureEngine(
            "test adapter",
            _ => Task.FromResult(capture),
            _ => Task.FromResult(new byte[] { 9 }),
            TimeSpan.FromSeconds(5));

        engine.BeginCapture(new WindowsRecordingCaptureStartRequest(
            device, SlideIndex: 0, StartedAtUtc: DateTimeOffset.UtcNow, packagePath));

        var result = engine.CompleteCapture(
            new WindowsRecordingCaptureRequest(device, SlideIndex: 0, DurationMs: 1000, packagePath));

        Assert.True(result.IsCaptured);
        Assert.Throws<ObjectDisposedException>(() => _ = capture.Stream.Size);
    }
}
