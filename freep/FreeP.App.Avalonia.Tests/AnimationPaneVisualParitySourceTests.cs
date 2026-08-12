using System.IO;

public sealed class AnimationPaneVisualParitySourceTests
{
    [Fact]
    public void Avalonia_animation_pane_preserves_the_Wpf_header_and_scrollable_list_hierarchy()
    {
        var avalonia = File.ReadAllText(RepoFile("freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var wpf = File.ReadAllText(RepoFile("freep", "FreeP.App.Host", "AnimationPane.cs"));

        wpf.Should().Contain("Text              = _session.ControlSchema.Heading");
        wpf.Should().Contain("DockPanel.SetDock(_playbackControlsPanel, Dock.Right);");
        wpf.Should().Contain("VerticalScrollBarVisibility   = ScrollBarVisibility.Auto");
        avalonia.Should().Contain("Text = _animationPaneSession.ControlSchema.Heading");
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
            "FreePBrushes.AnimationText",
            "FreePBrushes.PaneMutedText",
            "FreePBrushes.AnimationSelectedSurface",
            "FreePBrushes.PaneSurface",
            "FreePBrushes.GridBorder",
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
        avalonia.Should().Contain("Background = FreePBrushes.CardBorder");
        avalonia.Should().Contain("BorderBrush = FreePBrushes.PaneBorder");
        avalonia.Should().Contain("VerticalAlignment = VerticalAlignment.Center");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(parts);
}
