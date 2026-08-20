using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// freex-hyperlinks F1: the WPF host never surfaced a hyperlinked cell's ScreenTip/target on hover
/// -- only the Ctrl+hover hand cursor (SheetGrid.HyperlinkCells) existed. This pins
/// MainWindow.Viewport.cs's population of the new SheetGrid.HyperlinkTooltips text (the custom
/// ScreenTip if one was set via the Insert Hyperlink dialog, otherwise the raw target, mirroring
/// FreeX.App.Avalonia's FormatHyperlinkTooltip/R88-app-hyperlink-navigation-5-4) and
/// MainWindow.WorkbookLifecycle.cs's teardown of the same property.
/// </summary>
public sealed class MainWindowHyperlinkScreenTipSourceTests
{
    [Fact]
    public void MainWindow_PopulatesAndClearsHyperlinkScreenTipText()
    {
        var viewportSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");
        var lifecycleSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookLifecycle.cs");

        viewportSource.Should().Contain("SheetGrid.HyperlinkTooltips = sheet is null");
        viewportSource.Should().Contain("sheet.HyperlinkMetadata.TryGetValue(entry.Key, out var metadata)");
        viewportSource.Should().Contain("!string.IsNullOrWhiteSpace(metadata.ScreenTip)");
        viewportSource.Should().Contain("? metadata.ScreenTip.Trim()");
        viewportSource.Should().Contain(": entry.Value.Trim());");
        lifecycleSource.Should().Contain("SheetGrid.HyperlinkTooltips = null;");
    }
}
