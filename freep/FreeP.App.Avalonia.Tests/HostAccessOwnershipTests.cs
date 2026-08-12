using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class HostAccessOwnershipTests
{
    [Fact]
    public void ShippingProject_ConditionallyLinksHostAccess()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var hostDirectory = Path.Combine(root, "freep", "FreeP.App.Avalonia");
        var supportDirectory = Path.Combine(root, "freep", "TestSupport", "HostAccess.Avalonia");

        File.Exists(Path.Combine(hostDirectory, "MainWindow.TestAccess.cs")).Should().BeFalse();
        File.Exists(Path.Combine(supportDirectory, "MainWindow.TestAccess.cs")).Should().BeTrue();

        var project = File.ReadAllText(Path.Combine(hostDirectory, "FreeP.App.Avalonia.csproj"));
        project.Should().Contain("Condition=\"'$(FreePHostAccess)' == 'true'\"");
        project.Should().Contain("..\\TestSupport\\HostAccess.Avalonia\\MainWindow.TestAccess.cs");
    }
}
