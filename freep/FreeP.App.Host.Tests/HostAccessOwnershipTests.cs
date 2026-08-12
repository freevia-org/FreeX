using System.IO;
using FreeP.TestSupport;

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
        project.Should().Contain("<InternalsVisibleTo Include=\"FreeP.App.Host.Tests\"");
        project.Should().Contain("<InternalsVisibleTo Include=\"FreeP.VisualEvidence.Wpf\"");
        ShippingTestHookOwnershipAssertions.FindUnconditionalSupportItems(
                Path.Combine(hostDirectory, "FreeP.App.Host.csproj"),
                "TestSupport\\HostAccess.Wpf",
                "FreePHostAccess")
            .Should().BeEmpty();
        ShippingTestHookOwnershipAssertions.FindFriendItemsMissingCondition(
                Path.Combine(hostDirectory, "FreeP.App.Host.csproj"),
                "FreeP.App.Host.Tests",
                "FreePHostAccess")
            .Should().BeEmpty();
        ShippingTestHookOwnershipAssertions.FindFriendItemsMissingCondition(
                Path.Combine(hostDirectory, "FreeP.App.Host.csproj"),
                "FreeP.VisualEvidence.Wpf",
                "FreePVisualEvidenceHost")
            .Should().BeEmpty();
    }

    [Fact]
    public void ShippingSourceAndAssembly_ExcludeHostTestHooks()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var hostDirectory = Path.Combine(root, "freep", "FreeP.App.Host");
        ShippingTestHookOwnershipAssertions.FindShippingSourceViolations(hostDirectory)
            .Should().BeEmpty();

        var assemblyPath = ShippingTestHookOwnershipAssertions.ShippingAssemblyPath(
            root,
            "FreeP.App.Host",
            "FreeP.App.Host.dll");
        File.Exists(assemblyPath).Should().BeTrue(
            "the normal shipping variant is built before ownership tests");
        ShippingTestHookOwnershipAssertions.ReadCompiledTestHookNames(assemblyPath)
            .Should().BeEmpty();
    }
}
