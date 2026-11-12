using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class SisterAvaloniaUiLoopRecoveryTests
{
    [Fact]
    public void ResumeLoop_RecordsFaultAndReturnsExitCodeOfResumedLoop()
    {
        var fault = new InvalidOperationException("handler threw");
        var recorded = new List<(Exception, string)>();

        var exitCode = SisterAvaloniaUiLoopRecovery.ResumeLoop(
            fault,
            canResume: () => true,
            runLoop: () => 3,
            (exception, source) => recorded.Add((exception, source)),
            maximumRecoveries: 5);

        exitCode.Should().Be(3);
        recorded.Should().Equal((fault, SisterAvaloniaUiLoopRecovery.UiLoopFaultSource));
    }

    [Fact]
    public void ResumeLoop_ReturnsNullWithoutRecordingWhenDesktopCannotResume()
    {
        var recorded = new List<string>();

        SisterAvaloniaUiLoopRecovery.ResumeLoop(
                new InvalidOperationException("fault"),
                canResume: () => false,
                runLoop: () => throw new InvalidOperationException("must not run"),
                (_, source) => recorded.Add(source),
                maximumRecoveries: 5)
            .Should().BeNull();

        recorded.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(FatalFaults))]
    public void ResumeLoop_NeverResumesFatalOrDisplayFaults(Exception fault)
    {
        SisterAvaloniaUiLoopRecovery.ResumeLoop(
                fault,
                canResume: () => true,
                runLoop: () => throw new InvalidOperationException("must not run"),
                (_, _) => { },
                maximumRecoveries: 5)
            .Should().BeNull();
    }

    public static TheoryData<Exception> FatalFaults() =>
    [
        new OutOfMemoryException(),
        new InsufficientExecutionStackException(),
        new InvalidOperationException("Avalonia.Native was not able to start the RenderTimer. Native error code is: -6661")
    ];

    [Fact]
    public void ResumeLoop_RethrowsLatestFaultOnceRecoveryBudgetIsSpent()
    {
        var runs = 0;
        var recorded = new List<string>();

        Action action = () => SisterAvaloniaUiLoopRecovery.ResumeLoop(
            new InvalidOperationException("first"),
            canResume: () => true,
            runLoop: () => throw new InvalidOperationException("again " + ++runs),
            (_, source) => recorded.Add(source),
            maximumRecoveries: 3);

        action.Should().Throw<InvalidOperationException>().WithMessage("again 3");
        runs.Should().Be(3);
        recorded.Should().HaveCount(4).And.OnlyContain(source => source == SisterAvaloniaUiLoopRecovery.UiLoopFaultSource);
    }

    [Fact]
    public void Run_WhenUiLoopFaultIsRecovered_RunsAfterRunAndDoesNotRecordStartupCrash()
    {
        var recordedSources = new List<string>();
        int? afterRunExitCode = null;

        var exitCode = SisterAvaloniaApplicationStartupRunner.Run(
            [],
            new SisterAvaloniaApplicationStartupSpec(
                StartApplication: _ => throw new InvalidOperationException("key handler"),
                RegisterUnhandledExceptionHandlers: () => { },
                RecordCrash: (_, source) => recordedSources.Add(source))
            {
                RegisterRibbonCommandFaultHandler = _ => { },
                RecoverFromUiLoopFault = (fault, record) =>
                {
                    record(fault, SisterAvaloniaUiLoopRecovery.UiLoopFaultSource);
                    return 4;
                },
                AfterRun = code => afterRunExitCode = code,
                CompletedExitCode = 0
            });

        exitCode.Should().Be(0);
        afterRunExitCode.Should().Be(4);
        recordedSources.Should().Equal(SisterAvaloniaUiLoopRecovery.UiLoopFaultSource);
    }

    [Fact]
    public void Run_WhenRecoveryDeclines_RecordsStartupCrashAndRethrows()
    {
        var failure = new InvalidOperationException("startup");
        var recordedSources = new List<string>();

        Action action = () => SisterAvaloniaApplicationStartupRunner.Run(
            [],
            new SisterAvaloniaApplicationStartupSpec(
                StartApplication: _ => throw failure,
                RegisterUnhandledExceptionHandlers: () => { },
                RecordCrash: (_, source) => recordedSources.Add(source))
            {
                RegisterRibbonCommandFaultHandler = _ => { },
                RecoverFromUiLoopFault = (_, _) => null
            });

        action.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        recordedSources.Should().Equal(SisterAvaloniaApplicationStartupRunner.StartupCrashSource);
    }

    [Fact]
    public void TryResumeAfterFault_IsNoOpWithoutRunningDesktopLifetime()
    {
        SisterAvaloniaUiLoopRecovery.TryResumeAfterFault(new InvalidOperationException("x"), (_, _) => { })
            .Should().BeNull();
    }
}
