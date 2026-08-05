using System.IO;
using FreeP.App.Compositor;

namespace FreeP.App.Host.Tests;

public sealed class HyperlinkDialogTests
{
    [Fact]
    public void HyperlinkDialog_UsesSharedSessionForSemanticWorkflow()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "HyperlinkDialog.cs"));

        source.Should().Contain("HyperlinkDialogPlanner.BuildDialogRequest(slides, current)");
        source.Should().Contain("HyperlinkDialogPlanner.BuildSurfacePlan()");
        source.Should().Contain("new HyperlinkDialogSession(request)");
        source.Should().Contain("_session.SetInput(");
        source.Should().Contain("_session.SelectTarget(");
        source.Should().Contain("_session.SetUrlText(");
        source.Should().Contain("_session.SelectSlide(");
        source.Should().Contain("_session.SetTooltipText(");
        source.Should().Contain("_session.TryAccept()");
        source.Should().Contain("RenderInputState(state)");
        source.Should().Contain("FocusField(validation.FocusField)");
        source.Should().NotContain("HyperlinkDialogPlanner.BuildResult(");
        source.Should().NotContain("SelectedItem as HyperlinkDialogSlideOption");
        source.Should().NotContain("Result = plan.Result");
        source.Should().NotContain("Uri.TryCreate");
        source.Should().NotContain("new Hyperlink { Url =");
        source.Should().NotContain("new Hyperlink { TargetSlideId =");
        source.Should().NotContain("slide.Title");
        source.Should().NotContain("SlideItem");
        source.Should().NotContain("private void SelectSlide(");
        source.Should().NotContain("\"Web address:\"");
        source.Should().NotContain("\"Slide in this presentation:\"");
        source.Should().NotContain("\"Target slide:\"");
        source.Should().NotContain("\"Tooltip:\"");
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
        source.Should().Contain("public sealed class HyperlinkDialogSession");
        source.Should().Contain("BuildDialogRequest(");
        source.Should().Contain("BuildSlideOptions(");
        source.Should().Contain("BuildSurfacePlan(");
        source.Should().Contain("ResolveSelectedSlideId(");
        source.Should().Contain("ExternalUriLauncher.TryCreateAllowedUri");
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
        source.Should().Contain("TryGetSelectedShapeRunHyperlink");
        source.Should().Contain("TryApplySelectedShapeRunHyperlink");
        source.Should().Contain("Editor.SetShapeHyperlink(applyPlan.Url, applyPlan.TargetSlideId, applyPlan.Tooltip)");
        source.Should().NotContain("new HyperlinkDialog(slides, current)");
        source.Should().NotContain("dialog.Result.Url");
    }

}
