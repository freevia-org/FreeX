using System.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class ApplicationFrameAndSlideShowLaunchOwnershipSourceTests
{
    [Fact]
    public void RendererHostsDelegateFrameLabelsAndSlideShowLaunchPolicyToPresentation()
    {
        foreach (var source in MainWindowSources())
        {
            source.Should().Contain("FreePApplicationFrameDescriptor.ResolveDataFolderLabel");
            source.Should().Contain("FreePApplicationFrameDescriptor.Title");
            source.Should().Contain("_customShowSession.TryBuildPlaybackLaunch(");
            source.Should().Contain("_customShowSession.TryBuildNamedPlaybackLaunch(");
            source.Should().NotContain("private static string ResolveDataFolderLabel");
            source.Should().NotContain("AppStoragePathPlanner.GetOptionsFilePathLabelOrFallback");
            source.Should().NotContain("GetSelectedCaptionPlaybackSelection");
            source.Should().NotContain("PresentationMediaTranscriptPlanner.FindSelectedMediaShape(");
        }
    }

    [Fact]
    public void SharedOwnersRemainRendererNeutral()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var frame = Read(root, "freep", "FreeP.App.Presentation", "FreePApplicationFrameDescriptor.cs");
        var launch = Read(root, "freep", "FreeP.App.Presentation", "SlideShowCustomShowSession.cs");

        frame.Should().Contain("AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(pathProvider)")
            .And.Contain("public static ApplicationWindowTitleSpec Title")
            .And.NotContain("FreePApplicationFrameTitleSpec")
            .And.NotContain("ToApplicationWindowTitleSpec()")
            .And.NotContain("using System.Windows")
            .And.NotContain("using Avalonia");
        launch.Should().Contain("public sealed record SlideShowPlaybackLaunchPlan(")
            .And.Contain("public bool TryBuildPlaybackLaunch(")
            .And.Contain("public bool TryBuildNamedPlaybackLaunch(")
            .And.Contain("PresentationMediaTranscriptPlanner.FindSelectedMediaShape(")
            .And.NotContain("using System.Windows")
            .And.NotContain("using Avalonia");
    }

    private static IEnumerable<string> MainWindowSources()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        yield return Read(root, "freep", "FreeP.App.Host", "MainWindow.cs");
        yield return Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs");
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
