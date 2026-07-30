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
        presenter.Should().Contain("_goBack?.Invoke();");
        presenter.Should().Contain("_goNext?.Invoke();");
        presenter.Should().Contain("SlideShowSlideNumberPlanner.TryParseSlideNumber");
        presenter.Should().Contain("SubmitSlideNumber();");
        presenter.Should().Contain("_notesText.LostFocus");
        presenter.Should().Contain("_setNotesText(index, _notesText.Text)");
        presenter.Should().Contain("IsReadOnly = _setNotesText is null");
        presenter.Should().Contain("SlideShowScreenMode.Black");
        presenter.Should().Contain("SlideShowScreenMode.White");
        presenter.Should().Contain("_clearInk?.Invoke()");
        presenter.Should().Contain("_selectPointerMode?.Invoke");
        presenter.Should().Contain("Record timings");
        presenter.Should().Contain("Rehearse timings");
        presenter.Should().Contain("Narration + camera");
        presenter.Should().Contain("SlideShowRecordingMediaIntent.Narration");
        presenter.Should().Contain("SlideShowRecordingMediaIntent.NarrationAndMedia");
        presenter.Should().Contain("SlideShowTimingIntent.RecordTimings");
        presenter.Should().Contain("SlideShowTimingIntent.RehearseTimings");
        presenter.Should().Contain("_setTimingIntent");
        presenter.Should().Contain("plan.CanGoBack");
        presenter.Should().Contain("plan.CanAdvance");
    }
}
