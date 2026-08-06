namespace FreeP.App.Compositor.Tests;

public sealed class PresentationTextStateProjectionOwnershipTests
{
    [Fact]
    public void MainWindowRenderers_ConsumeSharedTextAndStateProjections()
    {
        var root = FindWorkspaceRoot();
        var wpf = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("item.AltTextDisplayText");
            source.Should().Contain("choice.DisplayLabel");
            source.Should().Contain("track.DisplayText");
            source.Should().Contain("track.IsSelected ? \"Selected\" : \"Not selected\"");
            source.Should().Contain("plan.SelectedTrackListIndex");
            source.Should().Contain("PresentationExportPlanner.BuildCurrentSlideRangeRequest(Editor.CurrentSlideIndex)");
            source.Should().NotContain("BuildReadingOrderAltTextLine");
            source.Should().NotContain("BuildLayoutChoiceLabel");
            source.Should().NotContain("BuildCurrentSlideImageExportRange");
            source.Should().NotContain("GetSingleSelectedShapeId");
            source.Should().NotContain("private static string FormatAvailability(bool isAvailable)");
            source.Should().NotContain("track.TrackIndex == plan.SelectedTrackIndex");
        }
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the FreeP workspace root.");
    }
}
