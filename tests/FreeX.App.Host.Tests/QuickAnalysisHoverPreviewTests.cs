using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class QuickAnalysisHoverPreviewTests
{
    [Fact]
    public void QuickAnalysisHoverAndKeyboardFocus_SetAndClearGridPreviewRange()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAnalysis.cs");

        source.Should().Contain("menuItem.GotKeyboardFocus += QuickAnalysisMenuItem_GotKeyboardFocus;");
        source.Should().Contain("menuItem.LostKeyboardFocus += QuickAnalysisMenuItem_LostKeyboardFocus;");
        source.Should().Contain("private void QuickAnalysisMenuItem_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)");
        source.Should().Contain("private void QuickAnalysisMenuItem_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)");
        source.Should().Contain("ShowQuickAnalysisPreview(sender);");
        source.Should().Contain("ClearQuickAnalysisPreview();");
        source.Should().Contain("ApplyQuickAnalysisPreview(");
        source.Should().Contain("preview.Range");
        source.Should().Contain("var preview = _quickAnalysisSession.PlanPreview(item)");
        source.Should().Contain("var preview = _quickAnalysisSession.PlanPreviewClear(resetStatus)");
        source.Should().Contain("if (SheetGrid.QuickAnalysisPreviewRange != range)");
        source.Should().NotContain("QuickAnalysisPlanner.BuildHoverPreview(range, item)");
    }

    [Fact]
    public void QuickAnalysisHoverAndKeyboardFocus_SetAndClearGridPreviewVisual()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.QuickAnalysis.cs");

        source.Should().Contain("preview.Visual");
        source.Should().Contain("_quickAnalysisSession.PlanPreviewClear(resetStatus)");
        source.Should().Contain("if (SheetGrid.QuickAnalysisPreviewVisual != visual)");
        source.Should().NotContain("MapQuickAnalysisPreviewVisual(");
    }
}
