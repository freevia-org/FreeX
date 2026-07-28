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

    [Fact]
    public void Avalonia_named_custom_show_restores_owner_focus_on_close()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var avalonia = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var customShowStart = avalonia.IndexOf("internal bool TryStartCustomSlideShow(", StringComparison.Ordinal);

        customShowStart.Should().BeGreaterThanOrEqualTo(0);
        var customShowBody = avalonia[customShowStart..];
        customShowBody.Should().Contain("slideShow.Closed += (_, _) => RestoreOwnerFocus();");
    }
}
