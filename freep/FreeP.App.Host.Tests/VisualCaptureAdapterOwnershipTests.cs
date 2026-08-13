using System.IO;
using System.Reflection;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

public sealed class VisualCaptureAdapterOwnershipTests
{
    [Fact]
    public void ShippingAssembly_DoesNotContainVisualCaptureAdapter()
    {
        typeof(MainWindow).GetMethod(
                "CreateVisualCaptureAdapter",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Should().BeNull();
        typeof(MainWindow).GetNestedType(
                "WpfVisualCaptureAdapter",
                BindingFlags.NonPublic)
            .Should().BeNull();
    }

    [Fact]
    public void EvidenceProject_OwnsConditionallyCompiledAdapter()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var shippingDirectory = Path.Combine(root, "freep", "FreeP.App.Host");
        var evidenceDirectory = Path.Combine(root, "freep", "TestSupport", "VisualEvidence.Wpf");

        File.Exists(Path.Combine(shippingDirectory, "MainWindow.VisualCaptureAdapter.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(evidenceDirectory, "MainWindow.VisualCaptureAdapter.cs"))
            .Should().BeTrue();

        var shippingProject = File.ReadAllText(Path.Combine(shippingDirectory, "FreeP.App.Host.csproj"));
        var evidenceProject = File.ReadAllText(Path.Combine(evidenceDirectory, "FreeP.VisualEvidence.Wpf.csproj"));
        shippingProject.Should().Contain("Condition=\"'$(FreePVisualEvidenceHost)' == 'true'\"");
        shippingProject.Should().Contain("..\\TestSupport\\VisualEvidence.Wpf\\MainWindow.VisualCaptureAdapter.cs");
        evidenceProject.Should().Contain("Compile Remove=\"MainWindow.VisualCaptureAdapter.cs\"");
        evidenceProject.Should().Contain("AdditionalProperties=\"FreePVisualEvidenceHost=true\"");
    }
}
