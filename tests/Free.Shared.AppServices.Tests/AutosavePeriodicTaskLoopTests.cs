using Free.Shared.AppServices;

namespace Free.Shared.AppServices.Tests;

public sealed class AutosavePeriodicTaskLoopTests
{
    [Fact]
    public async Task StopAsync_CancelsPendingDelayAndSuppressesLaterSnapshots()
    {
        var delayEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshots = 0;
        using var loop = new AutosavePeriodicTaskLoop(
            TimeSpan.FromSeconds(30),
            () => snapshots++,
            async (_, cancellationToken) =>
            {
                delayEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        loop.Start();
        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await loop.StopAsync();
        await Task.Delay(25);

        snapshots.Should().Be(0);
    }

    [Fact]
    public async Task StopAsync_WaitsForAnAlreadyRunningSnapshotAndPreventsAnotherTick()
    {
        var releaseDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var snapshotEntered = new ManualResetEventSlim();
        using var releaseSnapshot = new ManualResetEventSlim();
        var snapshots = 0;
        using var loop = new AutosavePeriodicTaskLoop(
            TimeSpan.FromSeconds(30),
            () =>
            {
                Interlocked.Increment(ref snapshots);
                snapshotEntered.Set();
                releaseSnapshot.Wait();
            },
            (_, cancellationToken) => releaseDelay.Task.WaitAsync(cancellationToken));

        loop.Start();
        releaseDelay.SetResult();
        snapshotEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        var stopTask = loop.StopAsync();
        await Task.Delay(25);
        stopTask.IsCompleted.Should().BeFalse();

        releaseSnapshot.Set();
        await stopTask;
        snapshots.Should().Be(1);
    }

    [Fact]
    public async Task Start_IsIdempotentWhileTheLoopIsRunning()
    {
        var delayCalls = 0;
        var delayEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var loop = new AutosavePeriodicTaskLoop(
            TimeSpan.FromSeconds(17),
            () => { },
            async (interval, cancellationToken) =>
            {
                interval.Should().Be(TimeSpan.FromSeconds(17));
                Interlocked.Increment(ref delayCalls);
                delayEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        loop.Start();
        loop.Start();
        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await loop.StopAsync();

        delayCalls.Should().Be(1);
    }
}
