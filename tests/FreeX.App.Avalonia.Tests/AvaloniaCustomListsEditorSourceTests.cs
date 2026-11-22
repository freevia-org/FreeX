using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaCustomListsEditorSourceTests
{
    [Fact]
    public void CustomListsEditor_MatchesWpfSizeAndScrollbarContract()
    {
        var avalonia = File.ReadAllText(
            TestWorkspaceFileLocator.FindFileFromBaseDirectory("src", "FreeX.App.Avalonia", "MainWindow.CustomListsEditor.cs"));
        var wpf = File.ReadAllText(
            TestWorkspaceFileLocator.FindFileFromBaseDirectory("src", "FreeX.App.Host", "OptionsDialog.xaml.cs"));

        // WPF: 460 is the outer window width, no wrap, auto vertical scrollbar, fixed 220 editor height.
        wpf.Should().Contain("Width = 460,");
        wpf.Should().Contain("VerticalScrollBarVisibility = ScrollBarVisibility.Auto,");

        // Avalonia sizes are client sizes, so the Windows frame comes off the outer width.
        avalonia.Should().Contain("Width = 460 - WpfNoResizeFrameWidth,");
        avalonia.Should().Contain("TextWrapping = TextWrapping.NoWrap,");
        avalonia.Should().Contain("ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);");
        avalonia.Should().Contain("ScrollViewer.SetHorizontalScrollBarVisibility(editor, ScrollBarVisibility.Hidden);");
        avalonia.Should().Contain("Height = 220,");
        avalonia.Should().Contain("MinWidth = 80");
    }
}
