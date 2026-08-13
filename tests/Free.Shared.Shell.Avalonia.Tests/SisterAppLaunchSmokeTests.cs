using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class SisterAppLaunchSmokeTests
{
    [Fact]
    public void CoordinatorDefaultsPreserveEstablishedPollingContract()
    {
        SisterAppLaunchSmokeCoordinator.DefaultMaxAttempts.Should().Be(60);
        SisterAppLaunchSmokeCoordinator.DefaultPollMilliseconds.Should().Be(200);
    }

    [Fact]
    public void OptionsParserRemovesSharedSmokeArgumentsAndPreservesStartupArguments()
    {
        var parsed = SisterAppLaunchSmokeOptions.TryParse(
            [
                "Document.fxw",
                "--launch-smoke",
                "reports/launch.txt",
                "--launch-smoke-diagnostics-dir",
                "reports/diagnostics",
                "--read-only",
            ],
            out var options,
            out var startupArguments,
            out var error);

        parsed.Should().BeTrue();
        error.Should().BeEmpty();
        options.Should().Be(new SisterAppLaunchSmokeOptions(
            "reports/launch.txt",
            "reports/diagnostics"));
        startupArguments.Should().Equal("Document.fxw", "--read-only");
    }
}
