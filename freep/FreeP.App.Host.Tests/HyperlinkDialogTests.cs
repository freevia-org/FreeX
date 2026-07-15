using System.IO;
using FreeP.App.Compositor;

namespace FreeP.App.Host.Tests;

public sealed class HyperlinkDialogTests
{
    [Fact]
    public void HyperlinkDialog_UsesSharedPlannerForPolicy()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "HyperlinkDialog.cs"));

        source.Should().Contain("HyperlinkDialogPlanner.BuildDialogRequest(slides, current)");
        source.Should().Contain("HyperlinkDialogPlanner.BuildResult(");
        source.Should().Contain("FocusField(validation.FocusField)");
        source.Should().NotContain("Uri.TryCreate");
        source.Should().NotContain("new Hyperlink { Url =");
        source.Should().NotContain("new Hyperlink { TargetSlideId =");
        source.Should().NotContain("slide.Title");
        source.Should().NotContain("SlideItem");
        source.Should().NotContain("SelectSlide(");
    }

    [Fact]
    public void HyperlinkDialogPlanner_RemainsPresentationOwned()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Presentation",
            "HyperlinkDialogPlanner.cs"));

        source.Should().Contain("public static class HyperlinkDialogPlanner");
        source.Should().Contain("BuildDialogRequest(");
        source.Should().Contain("BuildSlideOptions(");
        source.Should().Contain("Uri.TryCreate");
        source.Should().Contain("new Hyperlink");
    }

    [Fact]
    public void MainWindow_UsesPlannerForHyperlinkDialogRequestAndApplyPayload()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));

        source.Should().Contain("HyperlinkDialogPlanner.BuildDialogRequest(");
        source.Should().Contain("HyperlinkDialogPlanner.BuildApplyPlan(");
        source.Should().Contain("Editor.SetShapeHyperlink(applyPlan.Url, applyPlan.TargetSlideId, applyPlan.Tooltip)");
        source.Should().NotContain("new HyperlinkDialog(slides, current)");
        source.Should().NotContain("dialog.Result.Url");
    }

}
