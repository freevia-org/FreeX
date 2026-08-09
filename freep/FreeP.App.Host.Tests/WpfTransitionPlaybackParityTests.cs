using System;
using System.IO;
using FreeP.App.Compositor;
using Xunit;

namespace FreeP.App.Host.Tests;

public sealed class WpfTransitionPlaybackParityTests
{
    [Fact]
    public void Wpf_transition_dispatch_covers_every_shared_playback_action()
    {
        var hostSource = File.ReadAllText(RepoFile("freep/FreeP.App.Host/SlideShowWindow.cs"));
        var coordinatorSource = File.ReadAllText(RepoFile(
            "freep/FreeP.App.Presentation/SlideShowTransitionPlaybackCoordinator.cs"));

        Assert.Contains("SlideShowTransitionPlaybackCoordinator.Play", hostSource);
        Assert.DoesNotContain("switch (plan.ActionKind)", hostSource);
        foreach (var action in Enum.GetValues<SlideShowTransitionPlaybackActionKind>())
        {
            Assert.Contains(
                $"case SlideShowTransitionPlaybackActionKind.{action}:",
                coordinatorSource);
        }
    }

    private static string RepoFile(string relativePath) =>
        TestWorkspaceFileLocator.Find(relativePath);
}
