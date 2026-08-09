using System.IO;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class BackstageRibbonChromeSourceTests
{
    [Fact]
    public void FreeW_Backstage_uses_shared_ribbon_icon_chrome_with_Delete_close_artwork()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Backstage",
            "BackstageView.cs"));

        source.Should().Contain(
            "AvaloniaBackstageRibbonChrome.Create(RibbonCommandIconKind.Delete)");
        source.Should().NotContain("CreateRailIcon(");
        source.Should().NotContain("ToRibbonIcon(");
    }
}
