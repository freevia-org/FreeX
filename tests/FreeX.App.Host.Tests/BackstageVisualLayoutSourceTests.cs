using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class BackstageVisualLayoutSourceTests
{
    [Fact]
    public void FileBackstage_UsesLightRailAndBalancedHomeComposition()
    {
        var frame = DialogSourceTestSupport.ReadHostSources("MainWindow.BackstageFrame.cs");
        var xaml = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        var sharedFrame = DialogSourceTestSupport.ReadShellSources("BackstageFrame.cs");

        frame.Should().Contain("Sidebar: Color.FromRgb(0xF3, 0xF4, 0xF6)");
        frame.Should().Contain("Foreground: Color.FromRgb(0x24, 0x2A, 0x31)");
        sharedFrame.Should().Contain("ChromeBackstageSidebarTextBrush");
        sharedFrame.Should().Contain("ResolveRailForegroundBrush()");

        xaml.Should().Contain("FontSize=\"28\" FontWeight=\"Light\"");
        xaml.Should().Contain("Margin=\"40,30,40,0\"");
        xaml.Should().Contain("Width=\"144\" Height=\"108\"");
        xaml.Should().Contain("Grid.Column=\"2\" Click=\"SsMoreTemplatesBtn_Click\"");
        xaml.Should().Contain("Text=\"{local:Loc Key=MainWindow_AutomationName_MoreTemplatesUnavailable}\"");
        xaml.Should().Contain("<ColumnDefinition Width=\"*\"/>");
        xaml.Should().Contain("<Border Grid.Column=\"2\"");
        xaml.Should().Contain("Margin=\"40,24,40,16\"");
        xaml.Split("MinHeight=\"56\"").Length.Should().Be(3,
            "recent and pinned rows should use the same Office-scale density");
        xaml.Split("Text=\"{Binding DisplayDirectory}\"").Length.Should().Be(3,
            "recent and pinned rows should show compact locations");
        xaml.Split("ToolTip=\"{Binding Directory}\"").Length.Should().Be(3,
            "the full recent-file location remains discoverable");
        xaml.Split("TextTrimming=\"CharacterEllipsis\"").Length.Should().BeGreaterThanOrEqualTo(5,
            "both recent and pinned rows must keep long file names and directories within their columns");
    }
}
