using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class SisterAvaloniaApplicationStartupRunnerTests
{
    [Fact]
    public void Run_OrdersSharedLifecycleAndProjectsCommandFaults()
    {
        var order = new List<string>();
        var recordedCrashes = new List<(Exception Exception, string Source)>();
        Action<Exception, string>? ribbonFaultHandler = null;
        int? completedLifetimeExitCode = null;

        var exitCode = SisterAvaloniaApplicationStartupRunner.Run(
            ["prepared"],
            new SisterAvaloniaApplicationStartupSpec(
                StartApplication: arguments =>
                {
                    order.Add("start");
                    arguments.Should().Equal("prepared");
                    return 7;
                },
                RegisterUnhandledExceptionHandlers: () => order.Add("register"),
                RecordCrash: (exception, source) => recordedCrashes.Add((exception, source)))
            {
                RegisterRibbonCommandFaultHandler = handler =>
                {
                    order.Add("ribbon");
                    ribbonFaultHandler = handler;
                },
                BeforeRun = () => order.Add("before"),
                AfterRun = lifetimeExitCode =>
                {
                    order.Add("after");
                    completedLifetimeExitCode = lifetimeExitCode;
                },
                CompletedExitCode = 0
            });

        exitCode.Should().Be(0);
        completedLifetimeExitCode.Should().Be(7);
        order.Should().Equal("register", "ribbon", "before", "start", "after");

        var commandFailure = new InvalidOperationException("command");
        ribbonFaultHandler.Should().NotBeNull();
        ribbonFaultHandler!(commandFailure, "test.command");
        recordedCrashes.Should().ContainSingle().Which.Should().Be(
            (commandFailure, SisterAvaloniaApplicationStartupRunner.RibbonCommandCrashSourcePrefix + "test.command"));
    }

    [Fact]
    public void Run_WhenApplicationFails_RecordsConfiguredStartupCrashAndRethrows()
    {
        var failure = new InvalidOperationException("boom");
        Exception? recordedException = null;
        string? recordedSource = null;
        var afterRunCalled = false;

        Action action = () => SisterAvaloniaApplicationStartupRunner.Run(
            [],
            new SisterAvaloniaApplicationStartupSpec(
                StartApplication: _ => throw failure,
                RegisterUnhandledExceptionHandlers: () => { },
                RecordCrash: (exception, source) =>
                {
                    recordedException = exception;
                    recordedSource = source;
                })
            {
                RegisterRibbonCommandFaultHandler = _ => { },
                AfterRun = _ => afterRunCalled = true,
                StartupCrashSource = "test_startup"
            });

        action.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        recordedException.Should().BeSameAs(failure);
        recordedSource.Should().Be("test_startup");
        afterRunCalled.Should().BeFalse();
    }
}
