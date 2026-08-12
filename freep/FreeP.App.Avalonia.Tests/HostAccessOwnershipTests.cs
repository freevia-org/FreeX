using System.IO;
using FreeP.TestSupport;

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
        project.Should().Contain("'$(FreePHostAccess)' == 'true'");
        project.Should().Contain("..\\TestSupport\\HostAccess.Avalonia\\MainWindow.TestAccess.cs");
        project.Should().Contain("..\\TestSupport\\HostAccess.Avalonia\\MainWindow.DiagnosticsAccess.cs");
        project.Should().Contain("<InternalsVisibleTo Include=\"FreeP.App.Avalonia.Tests\"");
        project.Should().Contain("<InternalsVisibleTo Include=\"FreeP.VisualEvidence.Avalonia\"");
        project.Should().Contain("<InternalsVisibleTo Include=\"FreeP.Validation.Avalonia\"");
        ShippingTestHookOwnershipAssertions.FindUnconditionalSupportItems(
                Path.Combine(hostDirectory, "FreeP.App.Avalonia.csproj"),
                "TestSupport\\HostAccess.Avalonia",
                "FreePHostAccess")
            .Should().BeEmpty();
        ShippingTestHookOwnershipAssertions.FindFriendItemsMissingCondition(
                Path.Combine(hostDirectory, "FreeP.App.Avalonia.csproj"),
                "FreeP.App.Avalonia.Tests",
                "FreePHostAccess")
            .Should().BeEmpty();
        ShippingTestHookOwnershipAssertions.FindFriendItemsMissingCondition(
                Path.Combine(hostDirectory, "FreeP.App.Avalonia.csproj"),
                "FreeP.VisualEvidence.Avalonia",
                "FreePVisualEvidenceHost")
            .Should().BeEmpty();
        ShippingTestHookOwnershipAssertions.FindFriendItemsMissingCondition(
                Path.Combine(hostDirectory, "FreeP.App.Avalonia.csproj"),
                "FreeP.Validation.Avalonia",
                "FreePValidationHost")
            .Should().BeEmpty();
    }

    [Fact]
    public void ShippingSourceAndAssembly_ExcludeHostTestHooks()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var hostDirectory = Path.Combine(root, "freep", "FreeP.App.Avalonia");
        ShippingTestHookOwnershipAssertions.FindShippingSourceViolations(hostDirectory)
            .Should().BeEmpty();

        var assemblyPath = ShippingTestHookOwnershipAssertions.ShippingAssemblyPath(
            root,
            "FreeP.App.Avalonia",
            "FreeP.dll");
        File.Exists(assemblyPath).Should().BeTrue(
            "the normal shipping variant is built before ownership tests");
        ShippingTestHookOwnershipAssertions.ReadCompiledTestHookNames(assemblyPath)
            .Should().BeEmpty();
    }
}
