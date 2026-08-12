using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class HostAccessOwnershipTests
{
    [Fact]
    public void ShippingProject_ConditionallyLinksHostAccess()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var hostDirectory = Path.Combine(root, "freep", "FreeP.App.Host");
        var supportDirectory = Path.Combine(root, "freep", "TestSupport", "HostAccess.Wpf");

        File.Exists(Path.Combine(hostDirectory, "MainWindow.TestAccess.cs")).Should().BeFalse();
        File.Exists(Path.Combine(supportDirectory, "MainWindow.TestAccess.cs")).Should().BeTrue();

        var project = File.ReadAllText(Path.Combine(hostDirectory, "FreeP.App.Host.csproj"));
        project.Should().Contain("'$(FreePHostAccess)' == 'true'");
        project.Should().Contain("..\\TestSupport\\HostAccess.Wpf\\MainWindow.TestAccess.cs");
    }
}
