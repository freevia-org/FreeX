using System.IO;
using FluentAssertions;

namespace FreeP.App.Avalonia.Tests;

public sealed class PresenterViewControlSourceTests
{
    [Fact]
    public void AvaloniaPresenterView_WiresNavigationToTheExistingSlideshowCommands()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var slideshow = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "SlideShowWindow.cs"));
        var slideshowRuntime = File.ReadAllText(Path.Combine(
            root, "freep", "RendererShared", "SlideShowWindow.RuntimeSession.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var presenter = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "PresenterViewWindow.cs"));

        slideshow.Should().Contain("_runtime.CreatePresenterViewOperations(_setSlideNotesText)");
        slideshow.Should().Contain("private readonly SlideShowRuntimeApplication _runtime;");
        mainWindow.Should().Contain("Editor.SetSlideNotesText");
        slideshowRuntime.Should().Contain("RuntimeSession.SetScreenMode(mode)")
            .And.Contain("public SlideShowPresenterToolPlan SetPresenterPointerMode")
            .And.Contain("RuntimeSession.SetPresenterPointerMode(pointerMode, nowUtc)")
            .And.Contain("public SlideShowInkExecutionResult ClearPresenterInkStrokes()")
            .And.Contain("RuntimeSession.ClearPresenterInkStrokes()")
            .And.Contain("public SlideShowPresenterToolPlan SetPresenterTimingIntent")
            .And.Contain("RuntimeSession.SetPresenterTimingIntent(timingIntent, nowUtc)")
            .And.Contain("public SlideShowPresenterToolPlan SetPresenterMediaIntent")
            .And.Contain("RuntimeSession.SetPresenterMediaIntent(mediaIntent, nowUtc)");
        presenter.Should().Contain("SlideShowPresenterViewHostCoordinator");
        presenter.Should().Contain("new SlideShowPresenterViewHostCoordinator(operations)");
        presenter.Should().Contain("SlideShowPresenterViewNativeBinding");
        presenter.Should().Contain("SlideShowPresenterViewHeaderComposition.Compose(");
        presenter.Should().NotContain("Func<SlideShowPresenterState> stateProvider");
        presenter.Should().Contain("_nativeBinding.ExecuteAction(action)");
        presenter.Should().Contain("_nativeBinding.Refresh()");
        presenter.Should().Contain("_notesText.LostFocus");
        presenter.Should().Contain("_nativeBinding.CommitNotes()");
        presenter.Should().Contain("IsReadOnly = !_coordinator.CanSetNotes");
        presenter.Should().Contain("_nativeBinding.SelectPointerMode(mode.Value)");
        presenter.Should().Contain("_nativeBinding.NotifyNotesTextChanged()");
        presenter.Should().NotContain("_coordinator.ExecuteAction(");
        presenter.Should().NotContain("_coordinator.Refresh(new SlideShowPresenterViewHostRefreshInput(");
        presenter.Should().NotContain("_coordinator.CommitNotes(");
        presenter.Should().NotContain("SlideShowPresenterViewSession");
        presenter.Should().NotContain("SlideShowPresenterViewDispatchRequest");
        presenter.Should().NotContain("SlideShowPresenterViewRefreshRequest");
        presenter.Should().NotContain("_notesDirty");
        presenter.Should().NotContain("_refreshing");
        presenter.Should().NotContain("SlideShowSlideNumberPlanner");
        presenter.Should().NotContain("BuildRecordingSummary");
        slideshow.Should().NotContain("new SlideShowPresenterViewSession(");
        slideshowRuntime.Should().NotContain("new SlideShowPresenterViewSession(");
    }
}
