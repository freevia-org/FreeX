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
        var presenter = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "PresenterViewWindow.cs"));

        slideshow.Should().Contain("() => ExecuteBack()");
        slideshow.Should().Contain("() => ExecuteAdvance()");
        presenter.Should().Contain("_goBack?.Invoke();");
        presenter.Should().Contain("_goNext?.Invoke();");
        presenter.Should().Contain("plan.CanGoBack");
        presenter.Should().Contain("plan.CanAdvance");
    }
}
