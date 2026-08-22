using Free.Shared.AppServices;

namespace Free.Shared.AppServices.Tests;

public sealed class EmergencySnapshotFanOutTests
{
    [Fact]
    public void TrySnapshotAll_InvokesEveryLiveOwnerAndContinuesPastFailures()
    {
        var invoked = new List<string>();
        var fanOut = new EmergencySnapshotFanOut<string>(owner =>
        {
            invoked.Add(owner);
            if (owner == "failing")
                throw new InvalidOperationException("best-effort failure");
        });
        using var first = fanOut.Register("first");
        using var failing = fanOut.Register("failing");
        using var last = fanOut.Register("last");

        fanOut.TrySnapshotAll();

        invoked.Should().Equal("first", "failing", "last");
        fanOut.ActiveCount.Should().Be(3);
    }

    [Fact]
    public void DisposedRegistration_IsRemovedFromFutureFanOut()
    {
        var invoked = 0;
        var fanOut = new EmergencySnapshotFanOut<object>(_ => invoked++);
        var registration = fanOut.Register(new object());

        registration.Dispose();
        registration.Dispose();
        fanOut.TrySnapshotAll();

        invoked.Should().Be(0);
        fanOut.ActiveCount.Should().Be(0);
    }

    [Fact]
    public async Task DisposingRegistration_WaitsForAnInFlightSnapshotBeforeReleasingOwner()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var disposeStarted = new ManualResetEventSlim();
        var fanOut = new EmergencySnapshotFanOut<object>(_ =>
        {
            entered.Set();
            release.Wait();
        });
        var registration = fanOut.Register(new object());

        var fanOutTask = Task.Run(fanOut.TrySnapshotAll);
        entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        var disposeTask = Task.Run(() =>
        {
            disposeStarted.Set();
            registration.Dispose();
        });

        disposeStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        await Task.Delay(25);
        disposeTask.IsCompleted.Should().BeFalse();

        release.Set();
        await Task.WhenAll(fanOutTask, disposeTask);
        fanOut.ActiveCount.Should().Be(0);
    }
}
