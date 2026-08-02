using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class SmartArtTextPaneReachabilitySourceTests
{
    [Fact]
    public void AvaloniaSmartArtTextPane_UsesWrappingCommandBandForFixedWidthHost()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../FreeP.App.Avalonia/MainWindow.cs"));
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("_smartArtTextPaneCommandActions = new WrapPanel");
        source.Should().Contain("HorizontalAlignment = HorizontalAlignment.Left");
        source.Should().Contain("DockPanel.SetDock(_smartArtTextPaneCommandActions, Dock.Bottom)");
        source.Should().Contain("_smartArtTextPaneCommandActions.Children.Add(_smartArtTextPaneAssistantButton)");
        source.Should().Contain("_smartArtTextPaneCommandActions.Children.Add(_smartArtTextPaneApplyButton)");
        source.Should().Contain("_smartArtTextPaneCommandActions.Children.Add(_smartArtTextPaneCloseButton)");
        source.Should().Contain("Width = 320");
    }

    [Fact]
    public void AvaloniaSmartArtTextPane_RefreshesAfterEditorUndoRedo()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../FreeP.App.Avalonia/MainWindow.cs"));
        var source = File.ReadAllText(sourcePath);

        source.Should().MatchRegex(
            @"if\s*\(IsSmartArtTextPaneVisible\)\s*ShowSmartArtTextPane\(\);");
    }

    [Fact]
    public void AvaloniaSmartArtAuthoring_RejectsFailedNativeRefreshBeforeUndoCommit()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../FreeP.App.Avalonia/MainWindow.cs"));
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain(
            "private bool CommitSmartArtTextPaneMutation(");
        source.Should().Contain(
            "if (LastSmartArtDataPartRewriteResult is not { Applied: true })");
        source.Should().Contain(
            "return LastSmartArtDrawingCacheRegenerationResult is { Applied: true };");
        source.Should().Contain(
            "Message = \"SmartArt native data or drawing cache refresh failed.\"");
    }
}
