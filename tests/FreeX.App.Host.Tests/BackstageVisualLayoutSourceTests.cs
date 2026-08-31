using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class BackstageVisualLayoutSourceTests
{
    [Fact]
    public void FileBackstage_UsesLightRailAndCompactHomeComposition()
    {
        var frame = DialogSourceTestSupport.ReadHostSources("MainWindow.BackstageFrame.cs");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        var sharedFrame = DialogSourceTestSupport.ReadShellSources("BackstageFrame.cs");

        frame.Should().Contain("Sidebar: Color.FromRgb(0xF3, 0xF4, 0xF6)");
        frame.Should().Contain("Foreground: Color.FromRgb(0x24, 0x2A, 0x31)");
        sharedFrame.Should().Contain("ChromeBackstageSidebarTextBrush");
        sharedFrame.Should().Contain("ResolveRailForegroundBrush()");

        xaml.Should().Contain("FontSize=\"24\" FontWeight=\"Normal\"");
        xaml.Should().Contain("Margin=\"32,24,32,0\"");
        xaml.Should().Contain("Padding=\"32,14,32,4\"");
        xaml.Should().Contain("Width=\"100\" Height=\"74\"");
        xaml.Should().Contain("Padding=\"0,5\"");
        xaml.Split("TextTrimming=\"CharacterEllipsis\"").Length.Should().BeGreaterThanOrEqualTo(5,
            "both recent and pinned rows must keep long file names and directories within their columns");
    }
}
