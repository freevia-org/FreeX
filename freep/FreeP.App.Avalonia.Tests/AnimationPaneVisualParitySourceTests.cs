using System.IO;

public sealed class AnimationPaneVisualParitySourceTests
{
    [Fact]
    public void Avalonia_animation_pane_preserves_the_Wpf_header_and_scrollable_list_hierarchy()
    {
        var avalonia = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var wpf = File.ReadAllText(RepoFile("freep", "FreeP.App.Host", "AnimationPane.cs"));

        wpf.Should().Contain("Text              = \"Animation Pane\"");
        wpf.Should().Contain("DockPanel.SetDock(_playbackControlsPanel, Dock.Right);");
        wpf.Should().Contain("VerticalScrollBarVisibility   = ScrollBarVisibility.Auto");
        avalonia.Should().Contain("Text = \"Animation Pane\"");
        avalonia.Should().Contain("DockPanel.SetDock(_animationPanePlaybackControlsPanel, Dock.Right);");
        avalonia.Should().Contain("VerticalScrollBarVisibility = ScrollBarVisibility.Auto");
        avalonia.Should().Contain("_animationPanePlaybackControlsPanel,");
        avalonia.Should().Contain("_animationPaneHeading,");
    }

    [Fact]
    public void Avalonia_animation_rows_keep_Wpf_typography_spacing_and_state_chrome()
    {
        var avalonia = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var wpf = File.ReadAllText(RepoFile("freep", "FreeP.App.Host", "AnimationPane.cs"));

        foreach (var token in new[]
        {
            "Color.FromRgb(0x22, 0x22, 0x22)",
            "Color.FromRgb(0x66, 0x66, 0x66)",
            "Color.FromRgb(0xFF, 0xE0, 0xD6)",
            "Color.FromRgb(0xFA, 0xFA, 0xFA)",
            "Color.FromRgb(0xDD, 0xDD, 0xDD)",
            "Width",
            "Height",
            "Margin",
        })
        {
            wpf.Should().Contain(token);
            avalonia.Should().Contain(token);
        }

        wpf.Should().Contain("Width               = 18");
        wpf.Should().Contain("Height              = 18");
        wpf.Should().Contain("Margin              = new Thickness(1)");
        avalonia.Should().Contain("Width = 18");
        avalonia.Should().Contain("Height = 18");
        avalonia.Should().Contain("Margin = new Thickness(1)");

        avalonia.Should().Contain("MaxWidth = 70");
        avalonia.Should().Contain("Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))");
        avalonia.Should().Contain("BorderBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0))");
        avalonia.Should().Contain("VerticalAlignment = VerticalAlignment.Center");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(RepoFile);
}
