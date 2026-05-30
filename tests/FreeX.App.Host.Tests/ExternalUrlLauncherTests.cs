using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ExternalUrlLauncherTests
{
    [Fact]
    public void Open_AllowedScheme_LaunchesAndReportsLaunched()
    {
        var launched = new List<string>();

        var result = ExternalUrlLauncher.Open("https://example.com/help", launched.Add);

        result.Should().Be(ExternalUrlLaunchResult.Launched);
        launched.Should().ContainSingle().Which.Should().Be("https://example.com/help");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<h1>hi</h1>")]
    [InlineData("vbscript:MsgBox(1)")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    public void Open_DisallowedScheme_DoesNotLaunchAndReportsBlocked(string url)
    {
        var launched = new List<string>();

        var result = ExternalUrlLauncher.Open(url, launched.Add);

        result.Should().Be(ExternalUrlLaunchResult.BlockedScheme);
        launched.Should().BeEmpty("disallowed schemes must never reach the shell");
    }

    [Fact]
    public void Open_LaunchThrows_ReportsLaunchFailed()
    {
        var result = ExternalUrlLauncher.Open(
            "https://example.com",
            _ => throw new InvalidOperationException("boom"));

        result.Should().Be(ExternalUrlLaunchResult.LaunchFailed);
    }
}
