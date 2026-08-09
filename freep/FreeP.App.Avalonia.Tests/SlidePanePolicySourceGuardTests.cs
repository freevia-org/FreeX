using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class SlidePanePolicySourceGuardTests
{
    [Fact]
    public void MainWindowRealizesTheWorkareaOwnedSlidePaneSession()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));
        var endpoint = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.WorkareaEndpoint.cs"));
        var start = source.IndexOf("private void RefreshSlidePane", StringComparison.Ordinal);
        var end = source.IndexOf("private void RefreshNotesPane", start, StringComparison.Ordinal);
        var pane = source[start..end];

        source.Should().Contain("SelectionMode = SelectionMode.Multiple");
        pane.Should().Contain("_workareaSession.SlidePaneSession.Projection");
        pane.Should().Contain("foreach (var projected in projection.Items)");
        pane.Should().Contain("var plan = projected.Thumbnail!");
        pane.Should().Contain("BuildSlidePaneSectionHeader(");
        pane.Should().Contain("_workareaSession.ApplySlidePaneNativeSelection(selected, active)");
        pane.Should().Contain("_workareaSession.BeginSlidePaneDrag(");
        pane.Should().Contain("_workareaSession.UpdateSlidePaneDrag(");
        pane.Should().Contain("_workareaSession.CompleteSlidePaneDrag(");
        pane.Should().Contain("_workareaSession.ExecuteSlidePaneKeyboardAction(intent)");
        pane.Should().Contain("_workareaSession.BuildSlidePaneContextCommandRoute(");
        pane.Should().Contain("active.BringIntoView()");
        pane.Should().Contain("GetCurrentSlidePaneItem()?.Focus()");
        pane.Should().Contain("Presentation = _presentation");
        pane.Should().Contain("Slide        = slide");

        source.Should().NotContain("_slidePaneSessionState");
        source.Should().NotContain("_slidePaneProjection");
        source.Should().NotContain("_slidePaneRenderedThumbnailPlans");
        source.Should().NotContain("_slidePaneRenderedSectionHeaderPlans");
        pane.Should().NotContain("SlidePanePlanner.BuildSessionProjection(");
        pane.Should().NotContain("SlidePanePlanner.BuildContextCommandRoute(");
        pane.Should().NotContain("SlidePanePlanner.TryApplyAction(");
        pane.Should().NotContain("SlideSectionPlanner.TryApplyAction(");
        pane.Should().NotContain("Editor.SelectSlide(sourceSlideIndex)");
        pane.Should().NotContain("Editor.MoveSlide(");

        endpoint.Should().Contain("RefreshSlidePane = RefreshSlidePane");
        endpoint.Should().Contain("SyncSlidePaneSelection = SyncSlidePaneSelectionFromEditor");
        endpoint.Should().Contain("RefreshSlidePaneChrome = UpdateSlidePaneItemChrome");
        endpoint.Should().NotContain("SlidePanePlanner.SetSelectedSlide(");
    }
}
