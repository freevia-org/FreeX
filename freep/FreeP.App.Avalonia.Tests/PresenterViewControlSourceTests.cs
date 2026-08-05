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

        slideshow.Should().Contain("() => ExecuteBack()");
        slideshow.Should().Contain("() => ExecuteAdvance()");
        slideshow.Should().Contain("slideNumber => ExecuteSlideNumberJump(slideNumber)");
        slideshow.Should().Contain("(slideIndex, text) => _setSlideNotesText?.Invoke(slideIndex, text)");
        mainWindow.Should().Contain("Editor.SetSlideNotesText");
        slideshow.Should().Contain("SetScreenMode,");
        slideshow.Should().Contain("SetPresenterPointerMode(mode)");
        slideshow.Should().Contain("() => ClearPresenterInkStrokes(),");
        slideshow.Should().Contain("SetPresenterTimingIntent(timing)");
        slideshow.Should().Contain("public SlideShowPresenterToolPlan SetPresenterTimingIntent");
        slideshow.Should().Contain("SetPresenterMediaIntent(media)");
        slideshow.Should().Contain("public SlideShowPresenterToolPlan SetPresenterMediaIntent");
        presenter.Should().Contain("SlideShowPresenterViewSession");
        presenter.Should().Contain("_session.GoBack(");
        presenter.Should().Contain("_session.GoNext(");
        presenter.Should().Contain("_session.GoToSlide(");
        presenter.Should().Contain("SubmitSlideNumber();");
        presenter.Should().Contain("_notesText.LostFocus");
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
    }
}
