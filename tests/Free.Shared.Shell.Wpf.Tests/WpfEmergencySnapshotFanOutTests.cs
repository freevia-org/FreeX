namespace Free.Shared.Shell.Wpf.Tests;

public sealed class WpfEmergencySnapshotFanOutTests
{
    [Fact]
    public void TrySnapshotAllWindows_OnDispatcher_UsesShortcutAndVisitsEveryWindow()
    {
        var runtime = new RecordingRuntime(checkAccess: true, "first", "second");
        var visited = new List<object>();

        WpfEmergencySnapshotFanOut.TrySnapshotAllWindows(
            runtime,
            visited.Add,
            TimeSpan.FromSeconds(2));

        visited.Should().Equal("first", "second");
        runtime.InvokeCount.Should().Be(0);
    }

    [Fact]
    public void TrySnapshotAllWindows_OffDispatcher_UsesBoundedInvoke()
    {
        var runtime = new RecordingRuntime(checkAccess: false, "window");
        var timeout = TimeSpan.FromSeconds(3);
        var visited = new List<object>();

        WpfEmergencySnapshotFanOut.TrySnapshotAllWindows(runtime, visited.Add, timeout);

        visited.Should().Equal("window");
        runtime.InvokeCount.Should().Be(1);
        runtime.LastTimeout.Should().Be(timeout);
    }

    [Fact]
    public void TrySnapshotAllWindows_ContainsPerWindowFailuresAndContinues()
    {
        var runtime = new RecordingRuntime(checkAccess: true, "bad", "good");
        var visited = new List<object>();

        var act = () => WpfEmergencySnapshotFanOut.TrySnapshotAllWindows(
            runtime,
            window =>
            {
                if (Equals(window, "bad"))
                    throw new InvalidOperationException("snapshot failed");

                visited.Add(window);
            },
            TimeSpan.FromSeconds(1));

        act.Should().NotThrow();
        visited.Should().Equal("good");
    }

    [Theory]
    [InlineData(FailurePoint.CheckAccess)]
    [InlineData(FailurePoint.Invoke)]
    [InlineData(FailurePoint.EnumerateWindows)]
    public void TrySnapshotAllWindows_ContainsRuntimeFailures(FailurePoint failurePoint)
    {
        var runtime = new RecordingRuntime(checkAccess: false, "window")
        {
            Failure = failurePoint,
        };

        var act = () => WpfEmergencySnapshotFanOut.TrySnapshotAllWindows(
            runtime,
            _ => { },
            TimeSpan.FromMilliseconds(1));

        act.Should().NotThrow();
    }

    public enum FailurePoint
    {
        None,
        CheckAccess,
        Invoke,
        EnumerateWindows,
    }

    private sealed class RecordingRuntime(bool checkAccess, params object[] windows)
        : IWpfEmergencySnapshotRuntime
    {
        public FailurePoint Failure { get; init; }

        public int InvokeCount { get; private set; }

        public TimeSpan? LastTimeout { get; private set; }

        public bool CheckAccess()
        {
            if (Failure == FailurePoint.CheckAccess)
                throw new InvalidOperationException("dispatcher unavailable");

            return checkAccess;
        }

        public IEnumerable<object> GetWindows()
        {
            if (Failure == FailurePoint.EnumerateWindows)
                throw new InvalidOperationException("windows unavailable");

            return windows;
        }

        public void Invoke(Action action, TimeSpan timeout)
        {
            InvokeCount++;
            LastTimeout = timeout;

            if (Failure == FailurePoint.Invoke)
                throw new TimeoutException("dispatcher timed out");

            action();
        }
    }
}
