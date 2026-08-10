using System.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowPlaybackOwnershipSourceTests
{
    [Fact]
    public void SlideShowRenderersDelegateMaskTimelinesToPresentation()
    {
        foreach (var source in SlideShowWindowSources())
        {
            source.Should().Contain("SlideShowMaskTimelinePlanner.BuildRandomBars(plan, randomBars)");
            source.Should().Contain("SlideShowMaskTimelinePlanner.BuildCheckerboard(plan)");
            source.Should().NotContain("var barStaggerMs =");
            source.Should().NotContain("plan.DurationMs / 3");
            source.Should().NotContain("DelayedAction(plan.DurationMs / 5");
            source.Should().NotContain("new DiscreteDoubleKeyFrame(0.7");
        }
    }

    [Fact]
    public void MediaRenderersDelegateCaptionAndFullscreenPlacementToPresentation()
    {
        foreach (var source in MediaControllerSources())
        {
            source.Should().Contain("PresentationMediaTranscriptPlanner.PlanOverlayPlacement(");
            source.Should().NotContain("Math.Clamp(bounds.Height * 0.2, 36, 86)");
            source.Should().NotContain("FullScreenRect()");
            source.Should().NotContain("FullScreenBounds()");
        }
    }

    [Fact]
    public void PlaybackPlacementOwnersRemainRendererNeutral()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var timeline = Read(root, "freep", "FreeP.App.Presentation", "SlideShowMaskTimelinePlanner.cs");
        var transcript = Read(root, "freep", "FreeP.App.Presentation", "PresentationMediaTranscriptPlanner.cs");

        timeline.Should().Contain("public static class SlideShowMaskTimelinePlanner")
            .And.Contain("SlideShowRandomBarsMaskTimelinePlan")
            .And.NotContain("using System.Windows")
            .And.NotContain("using Avalonia");
        transcript.Should().Contain("public sealed record PresentationMediaOverlayPlacementRequest(")
            .And.Contain("public static PresentationMediaOverlayPlacement PlanOverlayPlacement(")
            .And.NotContain("using System.Windows")
            .And.NotContain("using Avalonia");
    }

    private static IEnumerable<string> SlideShowWindowSources()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        yield return Read(root, "freep", "FreeP.App.Host", "SlideShowWindow.cs");
        yield return Read(root, "freep", "FreeP.App.Avalonia", "SlideShowWindow.cs");
    }

    private static IEnumerable<string> MediaControllerSources()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        yield return Read(root, "freep", "FreeP.App.Host", "SlideShowMediaController.cs");
        yield return Read(root, "freep", "FreeP.App.Avalonia", "AvaloniaSlideShowMediaController.cs");
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
