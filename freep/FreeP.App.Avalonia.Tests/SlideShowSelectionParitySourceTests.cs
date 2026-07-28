namespace FreeP.App.Avalonia.Tests;

public sealed class SlideShowSelectionParitySourceTests
{
    [Fact]
    public void Wpf_and_Avalonia_launch_paths_keep_editor_selection_outside_playback_close()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        wpf.Should().NotContain("route.GetSourceSlideIndex(",
            "WPF does not reselect the editor slide when slideshow playback closes");
        avalonia.Should().NotContain("route.GetSourceSlideIndex(",
            "Avalonia must retain the WPF editor-selection authority");
        avalonia.Should().Contain("RestoreOwnerFocus();",
            "focus restoration remains a separate Avalonia window-lifecycle concern");
    }
}
