using System.IO;
using System.Reflection;
using FreeP.App.Avalonia;

namespace FreeP.App.Avalonia.Tests;

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
                "AvaloniaVisualCaptureAdapter",
                BindingFlags.NonPublic)
            .Should().BeNull();
    }

    [Fact]
    public void EvidenceProject_OwnsConditionallyCompiledAdapter()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var shippingDirectory = Path.Combine(root, "freep", "FreeP.App.Avalonia");
        var evidenceDirectory = Path.Combine(root, "freep", "TestSupport", "VisualEvidence.Avalonia");

        File.Exists(Path.Combine(shippingDirectory, "MainWindow.VisualCaptureAdapter.cs"))
            .Should().BeFalse();
        File.Exists(Path.Combine(evidenceDirectory, "MainWindow.VisualCaptureAdapter.cs"))
            .Should().BeTrue();

        var shippingProject = File.ReadAllText(Path.Combine(shippingDirectory, "FreeP.App.Avalonia.csproj"));
        var evidenceProject = File.ReadAllText(Path.Combine(evidenceDirectory, "FreeP.VisualEvidence.Avalonia.csproj"));
        shippingProject.Should().Contain("Condition=\"'$(FreePVisualEvidenceHost)' == 'true'\"");
        shippingProject.Should().Contain("..\\TestSupport\\VisualEvidence.Avalonia\\MainWindow.VisualCaptureAdapter.cs");
        evidenceProject.Should().Contain("Compile Remove=\"MainWindow.VisualCaptureAdapter.cs\"");
        evidenceProject.Should().Contain("AdditionalProperties=\"FreePVisualEvidenceHost=true\"");
    }
}
