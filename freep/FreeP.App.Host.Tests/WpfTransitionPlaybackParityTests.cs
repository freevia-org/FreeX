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
        var source = File.ReadAllText(RepoFile("freep/FreeP.App.Host/SlideShowWindow.cs"));

        Assert.Contains("var plan = SlideShowPlaybackPlanner.PlanTransition", source);
        foreach (var action in Enum.GetValues<SlideShowTransitionPlaybackActionKind>())
        {
            Assert.Contains(
                $"case SlideShowTransitionPlaybackActionKind.{action}:",
                source);
        }
    }

    private static string RepoFile(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath));
}
