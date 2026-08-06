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
        var mainWindow = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var presenter = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "PresenterViewWindow.cs"));

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
        presenter.Should().Contain("SlideShowPresenterViewSession");
        presenter.Should().Contain("_session.Surface");
        presenter.Should().Contain("_session.Dispatch(new SlideShowPresenterViewDispatchRequest(");
        presenter.Should().Contain("ExecuteAction(SlideShowPresenterViewAction.Previous)");
        presenter.Should().Contain("ExecuteAction(SlideShowPresenterViewAction.Next)");
        presenter.Should().Contain("ExecuteAction(SlideShowPresenterViewAction.GoToSlide)");
        presenter.Should().Contain("_notesText.LostFocus");
        presenter.Should().Contain("_session.CommitNotes(");
        presenter.Should().Contain("IsReadOnly = !_session.CanSetNotes");
        presenter.Should().Contain("ExecuteAction(SlideShowPresenterViewAction.BlackScreen)");
        presenter.Should().Contain("ExecuteAction(SlideShowPresenterViewAction.WhiteScreen)");
        presenter.Should().Contain("ExecuteAction(SlideShowPresenterViewAction.ClearInk)");
        presenter.Should().Contain("_session.SelectPointerMode");
        presenter.Should().Contain("ExecuteAction(SlideShowPresenterViewAction.RecordTimings)");
        presenter.Should().Contain("ExecuteAction(SlideShowPresenterViewAction.RehearseTimings)");
        presenter.Should().Contain("ExecuteAction(SlideShowPresenterViewAction.Narration)");
        presenter.Should().Contain("ExecuteAction(SlideShowPresenterViewAction.NarrationAndMedia)");
        presenter.Should().Contain("AutomationProperties.SetName(");
        presenter.Should().Contain("AutomationProperties.SetAutomationId(");
        presenter.Should().Contain("plan.CanGoBack");
        presenter.Should().Contain("plan.CanAdvance");
        presenter.Should().NotContain("_session.GoBack(");
        presenter.Should().NotContain("_session.GoNext(");
        presenter.Should().NotContain("_session.GoToSlide(");
        presenter.Should().NotContain("_session.ToggleTimingIntent(");
        presenter.Should().NotContain("_session.ToggleMediaIntent(");
        presenter.Should().NotContain("_session.SetScreenMode(");
        presenter.Should().NotContain("_session.ClearInk(");
        presenter.Should().NotContain("SlideShowSlideNumberPlanner");
        presenter.Should().NotContain("BuildRecordingSummary");
        slideshow.Should().NotContain("new SlideShowPresenterViewSession(");
    }
}
