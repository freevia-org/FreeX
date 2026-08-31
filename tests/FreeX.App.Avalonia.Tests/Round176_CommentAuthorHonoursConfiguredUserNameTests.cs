using System.IO;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Round 176. The Avalonia shell built its review-session adapter with a hardcoded
/// <c>() =&gt; Environment.UserName</c> author callback, while the WPF shell had always passed
/// <c>AppOptions.NormalizeUserName(_options.UserName)</c>. Two consequences, both silent:
/// Options &gt; User name did nothing for comments in this shell, and every comment inserted on
/// Linux/macOS stamped the machine's OS ACCOUNT name into a document that then gets shared.
///
/// This is a source contract rather than a behavioural test because the callback is constructed
/// inline in a MainWindow property; there is no seam to observe it through without standing up the
/// whole Avalonia shell. What it protects is narrow and stated exactly: the author name must come
/// from the normalizer, and the raw OS account name must not be reintroduced here. NormalizeUserName
/// itself still falls back to Environment.UserName when nothing is configured, so the
/// out-of-the-box behaviour of both shells is identical -- that fallback lives in one place on
/// purpose, and this asserts the shells route through it.
/// </summary>
public sealed class Round176_CommentAuthorHonoursConfiguredUserNameTests
{
    [Fact]
    public void BothShellsResolveTheCommentAuthorThroughNormalizeUserName()
    {
        foreach (var shell in new[] { "FreeX.App.Avalonia", "FreeX.App.Host" })
        {
            var source = File.ReadAllText(
                RepoFile("src", shell, "MainWindow.ReviewSessionController.cs"));

            source.Should().Contain("AppOptions.NormalizeUserName(",
                $"{shell} must resolve the comment author through the shared normalizer so that " +
                "Options > User name is actually honoured");
            source.Should().NotContain("() => Environment.UserName",
                $"{shell} must not stamp the raw OS account name onto comments -- the normalizer " +
                "already falls back to it when no author name is configured");
        }
    }

    /// <summary>
    /// r177. The same divergence at a second site: File > Account. The Avalonia shell called the
    /// 4-argument LocalAccountInfoPlanner.Build overload, which maps its userName straight into the
    /// request UserName -- so the pane showed the OS account name, Options > User name was ignored,
    /// and the separate "local OS account" line the pane supports never rendered because nothing
    /// ever supplied it. WPF has always passed the configured name and the OS account separately.
    /// </summary>
    [Fact]
    public void BothShellsShowTheConfiguredUserNameInTheAccountPane()
    {
        foreach (var shell in new[] { "FreeX.App.Avalonia", "FreeX.App.Host" })
        {
            var source = File.ReadAllText(RepoFile("src", shell, "MainWindow.Backstage.cs"));

            source.Should().Contain("LocalOsUserName:",
                $"{shell} must report the OS account as its own field, not as the account name");
            source.Should().NotContain("userName: SafeEnvironment(() => Environment.UserName)",
                $"{shell} must not pass the raw OS account name as the account's user name");
        }

        File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.Backstage.cs"))
            .Should().Contain("AppOptions.NormalizeUserName(",
                "the Avalonia Account pane must resolve the name through the shared normalizer, which " +
                "still falls back to the OS account when nothing is configured");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
