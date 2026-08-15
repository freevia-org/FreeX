using FluentAssertions;
using Free.Shared.AppServices.Printing;

namespace FreeX.App.Services.Tests;

public sealed class SystemProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_CapturesOutputErrorAndExitCode()
    {
        var result = await new SystemProcessRunner().RunAsync(CreateShellInvocation(
            "echo standard-output & echo standard-error 1>&2 & exit /b 7",
            "printf standard-output; printf standard-error >&2; exit 7"));

        result.ExitCode.Should().Be(7);
        result.StandardOutput.Should().Contain("standard-output");
        result.StandardError.Should().Contain("standard-error");
    }

    [Fact]
    public async Task RunAsync_CancelsRunningProcess()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var invocation = CreateShellInvocation(
            "ping -n 30 127.0.0.1 > nul",
            "sleep 30");
        Func<Task> run = () => new SystemProcessRunner().RunAsync(invocation, cancellation.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        cancellation.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_TimeoutReturnsTimedOutResultWithoutCancelingCaller()
    {
        var invocation = CreateShellInvocation(
            "ping -n 30 127.0.0.1 > nul",
            "sleep 30") with
        {
            Timeout = TimeSpan.FromMilliseconds(200),
        };

        var result = await new SystemProcessRunner().RunAsync(invocation);

        result.Succeeded.Should().BeFalse();
        result.TimedOut.Should().BeTrue();
        result.ExitCode.Should().Be(-1);
    }

    [Fact]
    public async Task RunAsync_RejectsNonPositiveTimeout()
    {
        var invocation = CreateShellInvocation("exit /b 0", "exit 0") with
        {
            Timeout = TimeSpan.Zero,
        };

        Func<Task> run = () => new SystemProcessRunner().RunAsync(invocation);

        await run.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private static ProcessInvocation CreateShellInvocation(string windowsCommand, string unixCommand) =>
        OperatingSystem.IsWindows()
            ? new ProcessInvocation(
                Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                ["/d", "/s", "/c", windowsCommand])
            : new ProcessInvocation("/bin/sh", ["-c", unixCommand]);
}
