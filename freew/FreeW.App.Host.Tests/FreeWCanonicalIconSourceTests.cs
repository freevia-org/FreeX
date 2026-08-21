using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class FreeWCanonicalIconSourceTests
{
    [Fact]
    public void Both_hosts_and_packages_use_the_shared_FreeW_icon_family()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var sharedResources = Path.Combine(root, "shared", "Free.Shared.Shell", "Resources");

        File.Exists(Path.Combine(sharedResources, "FreeW.ico")).Should().BeTrue();
        File.Exists(Path.Combine(sharedResources, "FreeW.svg")).Should().BeTrue();
        File.Exists(Path.Combine(sharedResources, "FreeW.icns")).Should().BeTrue();
        File.ReadAllText(Path.Combine(sharedResources, "FreeW.svg"))
            .Should().Contain("#A26714").And.Contain("#4B2F12").And.Contain("FREE").And.Contain(">W</text>");

        var wpfProject = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "FreeW.App.Host.csproj"));
        wpfProject.Should().Contain(@"shared\Free.Shared.Shell\Resources\FreeW.ico");
        wpfProject.Should().Contain("<ApplicationIcon>");
        var wpfWindow = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "MainWindow.cs"));
        wpfWindow.Should().Contain("pack://application:,,,/FreeW.App.Host;component/Resources/FreeW.ico");

        var avaloniaProject = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj"));
        avaloniaProject.Should().Contain(@"shared\Free.Shared.Shell\Resources\FreeW.ico");
        avaloniaProject.Should().Contain(@"shared\Free.Shared.Shell\Resources\FreeW.svg");
        avaloniaProject.Should().Contain(@"shared\Free.Shared.Shell\Resources\FreeW.icns");

        File.Exists(Path.Combine(root, "freew", "FreeW.App.Host", "Resources", "FreeW.ico")).Should().BeFalse();
        File.Exists(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Packaging", "linux", "io.github.tony-xmelon.freew.svg")).Should().BeFalse();
    }
}
