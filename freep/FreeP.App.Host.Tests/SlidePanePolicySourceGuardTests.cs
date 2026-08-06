using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class SlidePanePolicySourceGuardTests
{
    [Fact]
    public void SlidePaneIsANativeRealizerForTheWorkareaOwnedSession()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var pane = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "SlidePane.cs"));
        var endpoint = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "MainWindow.WorkareaEndpoint.cs"));

        pane.Should().Contain("private readonly PresentationWorkareaSession _workarea;");
        pane.Should().Contain("SelectionMode = SelectionMode.Extended");
        pane.Should().Contain("_list.SelectionChanged += OnNativeSelectionChanged;");
        pane.Should().Contain("_workarea.SlidePaneSession.Projection");
        pane.Should().Contain("_workarea.ApplySlidePaneNativeSelection(selected, active)");
        pane.Should().Contain("_workarea.BeginSlidePaneDrag(");
        pane.Should().Contain("_workarea.UpdateSlidePaneDrag(");
        pane.Should().Contain("_workarea.CompleteSlidePaneDrag(");
        pane.Should().Contain("_workarea.ExecuteSlidePaneKeyboardAction(intent)");
        pane.Should().Contain("_workarea.BuildSlidePaneContextCommandRoute(");
        pane.Should().Contain("Presentation = _workarea.Presentation");
        pane.Should().Contain("Slide = slide");
        pane.Should().Contain("GetActiveItem()?.Focus()");
        pane.Should().Contain("_list.ScrollIntoView(active)");

        pane.Should().NotContain("private readonly EditingSession");
        pane.Should().NotContain("_editor.Changed +=");
        pane.Should().NotContain("_editor.CurrentSlideChanged +=");
        pane.Should().NotContain("SlidePaneSessionState");
        pane.Should().NotContain("SlidePaneSessionProjection");
        pane.Should().NotContain("SlidePanePlanner.BuildSessionProjection(");
        pane.Should().NotContain("SlidePanePlanner.BuildContextCommandRoute(");
        pane.Should().NotContain("SlidePanePlanner.TryApplyAction(");
        pane.Should().NotContain("SlideSectionPlanner.TryApplyAction(");
        pane.Should().NotContain("new SlidePane(context.Snapshot.Editor)");

        endpoint.Should().Contain("PresentationWorkareaOperation.RefreshSlidePane => RefreshSlidePane");
        endpoint.Should().Contain("PresentationWorkareaOperation.SyncSlidePaneSelection => SyncSlidePaneSelection");
        endpoint.Should().Contain("PresentationWorkareaOperation.RefreshSlidePaneChrome => RefreshSlidePaneChrome");
        endpoint.Should().Contain("(SlidePaneHost?.Child as SlidePane)?.RefreshProjection()");
        endpoint.Should().NotContain("SlidePaneHost.Child = new SlidePane(context.Snapshot.Editor)");
    }
}
