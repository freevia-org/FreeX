using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class ZoomNavigationParitySourceTests
{
    [Fact]
    public void AvaloniaZoomNavigationForwardsTransitionMetadata()
    {
        var source = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "SlideShowWindow.cs"));
        var zoomCase = source.IndexOf(
            "case SlideShowPointerClickIntentKind.Zoom",
            StringComparison.Ordinal);

        zoomCase.Should().BeGreaterThanOrEqualTo(0);
        var route = source[zoomCase..];
        route.Should().Contain("pointerIntent.TransitionDurationMs");
        route.Should().Contain("pointerIntent.ShowBackground");
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
