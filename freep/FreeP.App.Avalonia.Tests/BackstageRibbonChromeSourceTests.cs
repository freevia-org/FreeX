using System.IO;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell;

namespace FreeP.App.Avalonia.Tests;

public sealed class BackstageRibbonChromeSourceTests
{
    [Fact]
    public void FreeP_Backstage_uses_shared_ribbon_icon_chrome_with_Delete_close_artwork()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "Backstage",
            "BackstageView.cs"));

        source.Should().Contain(
            "AvaloniaBackstageRibbonChrome.Create(RibbonCommandIconKind.Delete)");
        source.Should().NotContain("CreateRailIcon(");
        source.Should().NotContain("ToRibbonIcon(");
    }

    [Fact]
    public void FreeP_Backstage_uses_the_shared_Avalonia_sister_theme()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "Backstage",
            "BackstageView.cs"));

        source.Should().Contain("AvaloniaSisterBackstageTheme.FreeP");
        source.Should().Contain("BackstageTheme.Accent");
        source.Should().Contain("new SolidColorBrush(BackstageTheme.LinkColor)");
        source.Should().Contain("Width = BackstageTheme.TileWidth");
        source.Should().Contain("Height = BackstageTheme.TileHeight");
        source.Should().NotContain("Sidebar: Color.FromRgb(0xB7, 0x47, 0x2A)");
    }

    [Fact]
    public void Shared_Backstage_ribbon_chrome_keeps_WindowClose_mapping_configurable()
    {
        Enum.GetValues<BackstageIconKind>()
            .Select(kind => AvaloniaBackstageRibbonChrome.ResolveIconKind(
                kind,
                RibbonCommandIconKind.Delete))
            .Should().Equal(
                RibbonCommandIconKind.Generic,
                RibbonCommandIconKind.Previous,
                RibbonCommandIconKind.Grid,
                RibbonCommandIconKind.Info,
                RibbonCommandIconKind.Insert,
                RibbonCommandIconKind.GetData,
                RibbonCommandIconKind.Share,
                RibbonCommandIconKind.Save,
                RibbonCommandIconKind.Print,
                RibbonCommandIconKind.View,
                RibbonCommandIconKind.Delete);

        AvaloniaBackstageRibbonChrome.ResolveIconKind(
                BackstageIconKind.WindowClose,
                RibbonCommandIconKind.WindowClose)
            .Should().Be(RibbonCommandIconKind.WindowClose);
    }
}
