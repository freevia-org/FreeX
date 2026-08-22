namespace Free.Shared.Shell.Wpf.Tests;

public sealed class WpfAutosaveTimerTests
{
    [Fact]
    public void Constructor_WiresIntervalAndTickCallback()
    {
        var timer = new RecordingDispatcherTimer();
        var snapshots = 0;

        _ = new WpfAutosaveTimer(timer, TimeSpan.FromMinutes(3), () => snapshots++);
        timer.RaiseTick();

        timer.Interval.Should().Be(TimeSpan.FromMinutes(3));
        snapshots.Should().Be(1);
    }

    [Fact]
    public void StartAndStop_DelegateTimerLifecycle()
    {
        var timer = new RecordingDispatcherTimer();
        var scheduler = new WpfAutosaveTimer(timer, TimeSpan.FromMinutes(1), () => { });

        scheduler.Start();
        scheduler.Stop();

        timer.StartCount.Should().Be(1);
        timer.StopCount.Should().Be(1);
    }

    [Fact]
    public void Constructor_RejectsMissingSnapshotCallback()
    {
        var act = () => new WpfAutosaveTimer(TimeSpan.FromMinutes(1), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private sealed class RecordingDispatcherTimer : IWpfDispatcherTimer
    {
        public TimeSpan Interval { get; set; }

        public event EventHandler Tick = delegate { };

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public void Start() => StartCount++;

        public void Stop() => StopCount++;

        public void RaiseTick() => Tick(this, EventArgs.Empty);
    }
}
