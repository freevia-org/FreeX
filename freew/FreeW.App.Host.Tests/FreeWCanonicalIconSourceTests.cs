using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class FreeWCanonicalIconSourceTests
{
    [Fact]
    public void Both_hosts_and_packages_use_the_shared_FreeW_icon_family()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var sharedResources = Path.Combine(root, "shared", "Free.Shared.Shell", "Resources");
        var brandAssets = File.ReadAllText(Path.Combine(root, "shared", "Free.Shared.Shell", "BrandAssets.props"));

        File.Exists(Path.Combine(sharedResources, "FreeW.ico")).Should().BeTrue();
        File.Exists(Path.Combine(sharedResources, "FreeW.svg")).Should().BeTrue();
        File.Exists(Path.Combine(sharedResources, "FreeW.icns")).Should().BeTrue();
        File.ReadAllText(Path.Combine(sharedResources, "FreeW.svg"))
            .Should().Contain("#A26714").And.Contain("#4B2F12").And.Contain("FREE").And.Contain(">W</text>");
        brandAssets.Should().Contain("<BrandAssetBaseName>FreeW</BrandAssetBaseName>");
        brandAssets.Should().Contain("<BrandWindowsIconPath>$(BrandAssetsDirectory)$(BrandWindowsIconFileName)</BrandWindowsIconPath>");
        brandAssets.Should().Contain("<BrandScalableIconPath>$(BrandAssetsDirectory)$(BrandScalableIconFileName)</BrandScalableIconPath>");
        brandAssets.Should().Contain("<BrandMacOsIconPath>$(BrandAssetsDirectory)$(BrandMacOsIconFileName)</BrandMacOsIconPath>");

        var wpfProject = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "FreeW.App.Host.csproj"));
        wpfProject.Should().Contain(@"<Import Project=""..\..\shared\Free.Shared.Shell\BrandAssets.props"" />");
        wpfProject.Should().Contain(@"<Resource Include=""$(BrandWindowsIconPath)"" Link=""Resources\$(BrandWindowsIconFileName)"" />");
        wpfProject.Should().Contain(@"<Content Include=""$(BrandWindowsIconPath)""");
        wpfProject.Should().Contain("<ApplicationIcon>$(BrandWindowsIconPath)</ApplicationIcon>");
        wpfProject.Should().NotContain(@"shared\Free.Shared.Shell\Resources\FreeW.ico");
        wpfProject.Should().Contain("<ApplicationIcon>");
        var wpfWindow = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "MainWindow.cs"));
        wpfWindow.Should().Contain("IconUri = Program.ActiveTheme.VisualAssets.GetWpfPackUri(\"FreeW.App.Host\")");

        var avaloniaProject = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj"));
        avaloniaProject.Should().Contain(@"<Import Project=""..\..\shared\Free.Shared.Shell\BrandAssets.props"" />");
        avaloniaProject.Should().Contain(@"<Content Include=""$(BrandWindowsIconPath)""");
        avaloniaProject.Should().Contain(@"Link=""Resources\$(BrandWindowsIconFileName)""");
        avaloniaProject.Should().Contain(@"<Content Include=""$(BrandScalableIconPath)""");
        avaloniaProject.Should().Contain(@"Link=""Resources\$(BrandScalableIconFileName)""");
        avaloniaProject.Should().Contain(@"<Content Include=""$(BrandMacOsIconPath)""");
        avaloniaProject.Should().Contain(@"Link=""$(BrandMacOsIconFileName)""");
        avaloniaProject.Should().NotContain(@"shared\Free.Shared.Shell\Resources\FreeW.ico");
        avaloniaProject.Should().NotContain(@"shared\Free.Shared.Shell\Resources\FreeW.svg");
        avaloniaProject.Should().NotContain(@"shared\Free.Shared.Shell\Resources\FreeW.icns");

        foreach (var script in new[] { "build-appimage.sh", "build-deb.sh", "package-linux-app.sh" })
        {
            var source = File.ReadAllText(Path.Combine(
                root, "freew", "FreeW.App.Avalonia", "Packaging", "linux", script));
            source.Should().Contain("shared/Free.Shared.Shell/Resources/FreeW.svg", script);
        }

        File.ReadAllText(Path.Combine(
                root, "freew", "FreeW.App.Avalonia", "Packaging", "macos", "Info.plist"))
            .Should().Contain("<string>FreeW.icns</string>");

        File.Exists(Path.Combine(root, "freew", "FreeW.App.Host", "Resources", "FreeW.ico")).Should().BeFalse();
        File.Exists(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Packaging", "linux", "io.github.tony-xmelon.freew.svg")).Should().BeFalse();
    }
}
