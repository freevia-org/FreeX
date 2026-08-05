using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class ZoomNavigationParitySourceTests
{
    [Fact]
    public void AvaloniaZoomNavigationDelegatesTransitionMetadataToPortableSession()
    {
        var hostSource = File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Avalonia",
            "SlideShowWindow.cs"));
        var sessionSource = File.ReadAllText(RepoFile(
            "freep",
            "FreeP.App.Presentation",
            "SlideShowSessionController.cs"));
        var zoomCase = sessionSource.IndexOf(
            "SlideShowPointerClickIntentKind.Zoom",
            StringComparison.Ordinal);

        zoomCase.Should().BeGreaterThanOrEqualTo(0);
        var route = sessionSource[zoomCase..];
        route.Should().Contain("intent.TransitionDurationMs");
        route.Should().Contain("intent.ShowBackground");
        hostSource.Should().Contain("_session.PlanPointerInput(");
        hostSource.Should().NotContain("case SlideShowPointerClickIntentKind.Zoom");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull();
        return Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray());
    }
}
