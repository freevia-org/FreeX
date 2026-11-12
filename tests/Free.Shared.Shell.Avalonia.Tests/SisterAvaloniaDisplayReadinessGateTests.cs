using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class SisterAvaloniaDisplayReadinessGateTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(SisterAvaloniaDisplayProbeResult.Available)]
    [InlineData(SisterAvaloniaDisplayProbeResult.ProbeFailed)]
    public void WaitForDisplay_ContinuesImmediatelyWhenDisplayAvailableOrProbeUnusable(SisterAvaloniaDisplayProbeResult result)
    {
        var harness = new GateHarness(result);

        harness.Run().Should().Be(SisterAvaloniaDisplayGateOutcome.Continue);

        harness.Sleeps.Should().BeEmpty();
        harness.Logs.Should().BeEmpty();
        harness.ProbeCount.Should().Be(1);
    }

    [Fact]
    public void WaitForDisplay_WaitsAndLogsOnceUntilDisplayWakes()
    {
        var harness = new GateHarness(
            SisterAvaloniaDisplayProbeResult.Unavailable,
            SisterAvaloniaDisplayProbeResult.Unavailable,
            SisterAvaloniaDisplayProbeResult.Unavailable,
            SisterAvaloniaDisplayProbeResult.Available);

        harness.Run().Should().Be(SisterAvaloniaDisplayGateOutcome.Continue);

        harness.Sleeps.Should().Equal(Poll, Poll, Poll);
        harness.Logs.Should().HaveCount(2);
        harness.Logs[0].Should().Contain("waiting");
        harness.Logs[1].Should().Contain("became available");
    }

    [Fact]
    public void WaitForDisplay_ExitsWhenNoDisplayAppearsWithinBudget()
    {
        var harness = new GateHarness(SisterAvaloniaDisplayProbeResult.Unavailable);

        harness.Run().Should().Be(SisterAvaloniaDisplayGateOutcome.Exit);

        harness.Sleeps.Should().HaveCount(5);
        harness.Logs.Should().HaveCount(2);
        harness.Logs[^1].Should().Contain("exiting");
    }

    [Fact]
    public void WaitForDisplay_TreatsThrowingProbeAndLoggerAsNonFatal()
    {
        var outcome = SisterAvaloniaDisplayReadinessGate.WaitForDisplay(
            () => throw new DllNotFoundException("CoreVideo"),
            _ => throw new InvalidOperationException("must not sleep"),
            () => TimeSpan.Zero,
            _ => throw new IOException("stderr closed"),
            Poll,
            MaxWait);

        outcome.Should().Be(SisterAvaloniaDisplayGateOutcome.Continue);
    }

    [Fact]
    public void IsDisplayUnavailableStartupFailure_MatchesAvaloniaNativeRenderTimerFailureOnly()
    {
        var renderTimer = new InvalidOperationException(
            "Avalonia.Native was not able to start the RenderTimer. Native error code is: -6661");

        SisterAvaloniaDisplayReadinessGate.IsDisplayUnavailableStartupFailure(renderTimer).Should().BeTrue();
        SisterAvaloniaDisplayReadinessGate.IsDisplayUnavailableStartupFailure(
            new TypeInitializationException("X", renderTimer)).Should().BeTrue();
        SisterAvaloniaDisplayReadinessGate.IsDisplayUnavailableStartupFailure(
            new InvalidOperationException("boom")).Should().BeFalse();
        SisterAvaloniaDisplayReadinessGate.IsDisplayUnavailableStartupFailure(null).Should().BeFalse();
    }

    [Fact]
    public void Run_WhenDisplayGateExits_DoesNotStartApplication()
    {
        var started = false;

        var exitCode = SisterAvaloniaApplicationStartupRunner.Run(
            [],
            new SisterAvaloniaApplicationStartupSpec(
                StartApplication: _ =>
                {
                    started = true;
                    return 0;
                },
                RegisterUnhandledExceptionHandlers: () => { },
                RecordCrash: (_, _) => { })
            {
                RegisterRibbonCommandFaultHandler = _ => { },
                WaitForDisplay = () => SisterAvaloniaDisplayGateOutcome.Exit,
                CompletedExitCode = 0
            });

        exitCode.Should().Be(SisterAvaloniaDisplayReadinessGate.DisplayUnavailableExitCode);
        started.Should().BeFalse();
    }

    [Fact]
    public void Run_WhenRenderTimerCannotStart_RecordsCrashAndExitsInsteadOfRethrowing()
    {
        var failure = new InvalidOperationException(
            "Avalonia.Native was not able to start the RenderTimer. Native error code is: -6661");
        var recorded = new List<string>();
        var notices = new List<string>();

        var exitCode = SisterAvaloniaApplicationStartupRunner.Run(
            [],
            new SisterAvaloniaApplicationStartupSpec(
                StartApplication: _ => throw failure,
                RegisterUnhandledExceptionHandlers: () => { },
                RecordCrash: (_, source) => recorded.Add(source))
            {
                RegisterRibbonCommandFaultHandler = _ => { },
                WaitForDisplay = () => SisterAvaloniaDisplayGateOutcome.Continue,
                LogStartupNotice = notices.Add
            });

        exitCode.Should().Be(SisterAvaloniaDisplayReadinessGate.DisplayUnavailableExitCode);
        recorded.Should().Equal(SisterAvaloniaApplicationStartupRunner.StartupCrashSource);
        notices.Should().ContainSingle();
    }

    [Fact]
    public void DefaultProgramRuntime_InstallsDisplayGateButTestRuntimesDoNot()
    {
        SisterAvaloniaProgramRuntime.Default.WaitForDisplay.Should().NotBeNull();
        SisterAvaloniaProgramRuntime.Default.RecoverFromUiLoopFault.Should().NotBeNull();
        new SisterAvaloniaProgramRuntime().WaitForDisplay.Should().BeNull();
        new SisterAvaloniaProgramRuntime().RecoverFromUiLoopFault.Should().BeNull();
    }

    [Fact]
    public void MacOsCoreVideoProbe_BindsOnMacOsAndFailsOpenElsewhere()
    {
        var result = MacOsCoreVideoDisplayProbe.Probe();

        if (OperatingSystem.IsMacOS())
            result.Should().NotBe(SisterAvaloniaDisplayProbeResult.ProbeFailed, "CoreVideo must resolve on macOS");
        else
            result.Should().Be(SisterAvaloniaDisplayProbeResult.ProbeFailed);
    }

    private sealed class GateHarness(params SisterAvaloniaDisplayProbeResult[] results)
    {
        private TimeSpan _elapsed;

        public int ProbeCount { get; private set; }

        public List<TimeSpan> Sleeps { get; } = [];

        public List<string> Logs { get; } = [];

        public SisterAvaloniaDisplayGateOutcome Run() =>
            SisterAvaloniaDisplayReadinessGate.WaitForDisplay(
                () => results[Math.Min(ProbeCount++, results.Length - 1)],
                interval =>
                {
                    Sleeps.Add(interval);
                    _elapsed += interval;
                },
                () => _elapsed,
                Logs.Add,
                Poll,
                MaxWait);
    }
}
