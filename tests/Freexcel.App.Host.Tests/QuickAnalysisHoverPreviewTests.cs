using FluentAssertions;
using System.IO;

namespace Freexcel.App.Host.Tests;

public sealed class QuickAnalysisHoverPreviewTests
{
    [Fact]
    public void QuickAnalysisHoverAndKeyboardFocus_SetAndClearGridPreviewRange()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.QuickAnalysis.cs"));

        source.Should().Contain("item.GotKeyboardFocus += QuickAnalysisMenuItem_GotKeyboardFocus;");
        source.Should().Contain("item.LostKeyboardFocus += QuickAnalysisMenuItem_LostKeyboardFocus;");
        source.Should().Contain("private void QuickAnalysisMenuItem_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)");
        source.Should().Contain("private void QuickAnalysisMenuItem_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)");
        source.Should().Contain("ShowQuickAnalysisPreview(sender);");
        source.Should().Contain("ClearQuickAnalysisPreview();");
        source.Should().Contain("SheetGrid.QuickAnalysisPreviewRange = preview.Range");
        source.Should().Contain("SheetGrid.QuickAnalysisPreviewRange = null");
    }

    [Fact]
    public void QuickAnalysisHoverAndKeyboardFocus_SetAndClearGridPreviewVisual()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.QuickAnalysis.cs"));

        source.Should().Contain("SheetGrid.QuickAnalysisPreviewVisual = MapQuickAnalysisPreviewVisual(preview.PreviewVisual.Kind)");
        source.Should().Contain("SheetGrid.QuickAnalysisPreviewVisual = GridQuickAnalysisPreviewVisualKind.None");
        source.Should().Contain("private static GridQuickAnalysisPreviewVisualKind MapQuickAnalysisPreviewVisual(");
    }
}
