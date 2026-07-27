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
        var presenter = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "PresenterViewWindow.cs"));

        slideshow.Should().Contain("() => ExecuteBack()");
        slideshow.Should().Contain("() => ExecuteAdvance()");
        slideshow.Should().Contain("SetScreenMode,");
        slideshow.Should().Contain("SetPresenterPointerMode(mode)");
        slideshow.Should().Contain("() => ClearPresenterInkStrokes(),");
        slideshow.Should().Contain("SetPresenterTimingIntent(timing)");
        slideshow.Should().Contain("public SlideShowPresenterToolPlan SetPresenterTimingIntent");
        presenter.Should().Contain("_goBack?.Invoke();");
        presenter.Should().Contain("_goNext?.Invoke();");
        presenter.Should().Contain("SlideShowScreenMode.Black");
        presenter.Should().Contain("SlideShowScreenMode.White");
        presenter.Should().Contain("_clearInk?.Invoke()");
        presenter.Should().Contain("_selectPointerMode?.Invoke");
        presenter.Should().Contain("Record timings");
        presenter.Should().Contain("SlideShowTimingIntent.RecordTimings");
        presenter.Should().Contain("_setTimingIntent");
        presenter.Should().Contain("plan.CanGoBack");
        presenter.Should().Contain("plan.CanAdvance");
    }
}
