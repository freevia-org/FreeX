using System.IO;
using FluentAssertions;

namespace FreeP.App.Host.Tests;

public sealed class PresenterViewControlSourceTests
{
    [Fact]
    public void WpfPresenterView_WiresNavigationToTheExistingSlideshowCommands()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var slideshow = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "SlideShowWindow.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs"));
        var presenter = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "PresenterViewWindow.cs"));

        slideshow.Should().Contain("_runtime.CreatePresenterViewOperations(_setSlideNotesText)");
        slideshow.Should().Contain("private readonly SlideShowRuntimeApplication _runtime;");
        mainWindow.Should().Contain("Editor.SetSlideNotesText");
        slideshow.Should().Contain("_runtime.SetScreenMode(mode)");
        slideshow.Should().Contain("_runtime.SetPointerMode(pointerMode, nowUtc)");
        slideshow.Should().Contain("_runtime.ClearInkStrokes()");
        slideshow.Should().Contain("_runtime.SetTimingIntent(timingIntent, nowUtc)");
        slideshow.Should().Contain("public SlideShowPresenterToolPlan SetPresenterTimingIntent");
        slideshow.Should().Contain("_runtime.SetMediaIntent(mediaIntent, nowUtc)");
        slideshow.Should().Contain("public SlideShowPresenterToolPlan SetPresenterMediaIntent");
        presenter.Should().Contain("SlideShowPresenterViewHostCoordinator");
        presenter.Should().Contain("new SlideShowPresenterViewHostCoordinator(operations)");
        presenter.Should().Contain("SlideShowPresenterViewNativeBinding<TextBlock, TextBox, ComboBox, Button, SlideCanvas>");
        presenter.Should().Contain("_nativeBinding = new(");
        presenter.Should().Contain("_nativeBinding.ExecuteAction(action)");
        presenter.Should().Contain("_nativeBinding.Refresh()");
        presenter.Should().Contain("_nativeBinding.CommitNotes()");
        presenter.Should().Contain("_nativeBinding.SelectPointerMode(mode.Value)");
        presenter.Should().Contain("_nativeBinding.NotifyNotesTextChanged()");
        presenter.Should().NotContain("Func<SlideShowPresenterState> stateProvider");
        presenter.Should().Contain("_notesText.LostKeyboardFocus");
        presenter.Should().Contain("IsReadOnly = !_coordinator.CanSetNotes");
        presenter.Should().Contain("SlideShowPresenterViewHeaderComposition.Compose(");
        presenter.Should().NotContain("_coordinator.ExecuteAction(");
        presenter.Should().NotContain("_coordinator.Refresh(new SlideShowPresenterViewHostRefreshInput(");
        presenter.Should().NotContain("_coordinator.CommitNotes(");
        presenter.Should().NotContain("_coordinator.SelectPointerMode");
        presenter.Should().NotContain("_coordinator.NotifyNotesTextChanged()");
        presenter.Should().NotContain("plan.CanGoBack");
        presenter.Should().NotContain("plan.CanAdvance");
        presenter.Should().NotContain("SlideShowPresenterViewSession");
        presenter.Should().NotContain("SlideShowPresenterViewDispatchRequest");
        presenter.Should().NotContain("SlideShowPresenterViewRefreshRequest");
        presenter.Should().NotContain("_notesDirty");
        presenter.Should().NotContain("_refreshing");
        presenter.Should().NotContain("SlideShowSlideNumberPlanner");
        presenter.Should().NotContain("BuildRecordingSummary");
        slideshow.Should().NotContain("new SlideShowPresenterViewSession(");
    }
}
