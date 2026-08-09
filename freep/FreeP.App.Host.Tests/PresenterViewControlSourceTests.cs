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
        presenter.Should().Contain("SlideShowPresenterViewSession");
        presenter.Should().Contain("_session.GoBack(");
        presenter.Should().Contain("_session.GoNext(");
        presenter.Should().Contain("_session.GoToSlide(");
        presenter.Should().Contain("SubmitSlideNumber();");
        presenter.Should().Contain("_notesText.LostKeyboardFocus");
        presenter.Should().Contain("_session.CommitNotes(");
        presenter.Should().Contain("IsReadOnly = !_session.CanSetNotes");
        presenter.Should().Contain("SlideShowScreenMode.Black");
        presenter.Should().Contain("SlideShowScreenMode.White");
        presenter.Should().Contain("_session.ClearInk");
        presenter.Should().Contain("_session.SelectPointerMode");
        presenter.Should().Contain("_session.ToggleTimingIntent(");
        presenter.Should().Contain("_session.ToggleMediaIntent(");
        presenter.Should().Contain("SlideShowRecordingMediaIntent.Narration");
        presenter.Should().Contain("SlideShowRecordingMediaIntent.NarrationAndMedia");
        presenter.Should().Contain("SlideShowTimingIntent.RecordTimings");
        presenter.Should().Contain("SlideShowTimingIntent.RehearseTimings");
        presenter.Should().Contain("plan.CanGoBack");
        presenter.Should().Contain("plan.CanAdvance");
        presenter.Should().NotContain("SlideShowSlideNumberPlanner");
        presenter.Should().NotContain("BuildRecordingSummary");
        slideshow.Should().NotContain("new SlideShowPresenterViewSession(");
    }
}
