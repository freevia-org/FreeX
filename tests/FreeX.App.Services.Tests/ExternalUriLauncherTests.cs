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

    // R116: a file:// URI's decoded LocalPath can parse fine as Uri text while still being a path
    // Path.GetFullPath refuses to normalize (e.g. one long enough to throw PathTooLongException --
    // verified directly against this repo's actual net10.0 runtime, not just documentation: a
    // 40,000-char segment parses as a valid absolute file:// Uri with an empty host, yet
    // Path.GetFullPath(uri.LocalPath) throws). HyperlinkNavigationPlanner.TryNormalizeExplicitLocalPath
    // already rejects exactly this shape via the identical Path.GetFullPath call, reclassifying the
    // hyperlink as External instead of LocalFile -- so this shared allowlist must reject it too, or
    // the External branch in both shells hands it straight to Process.Start/ShellExecute, bypassing
    // the "never shell-exec local files" guard entirely. This goes through Open(), the real entry
    // point both shells call for an External-kind hyperlink.
    [Fact]
    public void Open_LocalFileUriWithPathTooLongTarget_DoesNotLaunch()
    {
        var target = "file:///C:/" + new string('a', 40_000) + ".exe";
        var launched = new List<Uri>();

        var result = ExternalUriLauncher.Open(target, launched.Add);

        result.Should().Be(ExternalUriLaunchResult.BlockedScheme);
        launched.Should().BeEmpty();
    }

    // No-regression sibling: a syntactically well-formed local file:// URI (the shape FreeW's
    // document-hyperlink and FreeX's LocalFile-classified targets both rely on) must still be let
    // through unchanged.
    [Fact]
    public void TryCreateAllowedUri_WellFormedLocalFileUri_IsAllowed()
    {
        var accepted = ExternalUriLauncher.TryCreateAllowedUri("file:///C:/Temp/book.xlsx", out var uri);

        accepted.Should().BeTrue();
        uri!.LocalPath.Should().Be(@"C:\Temp\book.xlsx");
    }

    [Fact]
    public void TryCreateAllowedUri_PathTooLongLocalPath_IsRejected()
    {
        var target = "file:///C:/" + new string('a', 40_000) + ".exe";

        var accepted = ExternalUriLauncher.TryCreateAllowedUri(target, out var uri);

        accepted.Should().BeFalse();
        uri.Should().BeNull();
    }
}
