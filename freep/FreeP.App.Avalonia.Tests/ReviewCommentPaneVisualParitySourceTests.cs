using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class ReviewCommentPaneVisualParitySourceTests
{
    [Fact]
    public void WpfAndAvaloniaCommentPanes_ConsumeTheSharedCompactVisualContract()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var sharedMetrics = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationCommentPaneVisualMetrics.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));
        var wpf = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));

        sharedMetrics.Should().Contain("public const double CompactControlHeight = 22;");
        sharedMetrics.Should().Contain("public const double CardBottomMargin = 6;");
        avalonia.Should().Contain("PresentationCommentPaneVisualMetrics.CompactControlHeight");
        avalonia.Should().Contain("PresentationCommentPaneVisualMetrics.CardBottomMargin");
        avalonia.Should().NotContain("PlaceholderText = \"Comment\"");
        wpf.Should().Contain("MinWidth = 64");
        wpf.Should().Contain("MinWidth = 220");
    }
}
