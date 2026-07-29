using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class ExternalUriLauncherTests
{
    [Fact]
    public void Open_AllowedUri_LaunchesNormalizedUri()
    {
        var launched = new List<Uri>();

        var result = ExternalUriLauncher.Open(" https://example.test/help ", launched.Add);

        result.Should().Be(ExternalUriLaunchResult.Launched);
        launched.Should().ContainSingle()
            .Which.AbsoluteUri.Should().Be("https://example.test/help");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<h1>hi</h1>")]
    [InlineData("vbscript:MsgBox(1)")]
    [InlineData("file://server/share/book.xlsx")]
    [InlineData("relative/path/file.xlsx")]
    [InlineData("")]
    public void Open_DisallowedUri_DoesNotLaunch(string target)
    {
        var launched = new List<Uri>();

        var result = ExternalUriLauncher.Open(target, launched.Add);

        result.Should().Be(ExternalUriLaunchResult.BlockedScheme);
        launched.Should().BeEmpty();
    }

    [Fact]
    public void Open_LocalFileUri_LaunchesNormalizedUri()
    {
        var launched = new List<Uri>();

        var result = ExternalUriLauncher.Open(" file:///tmp/book.xlsx ", launched.Add);

        result.Should().Be(ExternalUriLaunchResult.Launched);
        launched.Should().ContainSingle()
            .Which.AbsoluteUri.Should().Be("file:///tmp/book.xlsx");
    }

    [Fact]
    public void Open_AllowedUriWithoutLauncherReportsUnavailable()
    {
        var result = ExternalUriLauncher.Open("https://example.test/help", launch: null);

        result.Should().Be(ExternalUriLaunchResult.LauncherUnavailable);
    }

    [Fact]
    public void Open_LaunchThrowsReportsLaunchFailed()
    {
        var result = ExternalUriLauncher.Open(
            "https://example.test/help",
            _ => throw new InvalidOperationException("boom"));

        result.Should().Be(ExternalUriLaunchResult.LaunchFailed);
    }

    [Fact]
    public async Task OpenAsync_AllowedUriLaunchesThroughPlatformDelegate()
    {
        var launched = new List<Uri>();

        var result = await ExternalUriLauncher.OpenAsync(
            "mailto:user@example.test",
            uri =>
            {
                launched.Add(uri);
                return Task.FromResult(true);
            });

        result.Should().Be(ExternalUriLaunchResult.Launched);
        launched.Should().ContainSingle()
            .Which.AbsoluteUri.Should().Be("mailto:user@example.test");
    }

    [Fact]
    public async Task OpenAsync_LauncherRejectsUriReportsLaunchFailed()
    {
        var result = await ExternalUriLauncher.OpenAsync(
            "https://example.test/help",
            _ => Task.FromResult(false));

        result.Should().Be(ExternalUriLaunchResult.LaunchFailed);
    }
}
