using System.Reflection;
using FreeP.App.Rendering.Avalonia;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class CanvasGestureHandlerTestSupportOwnershipTests
{
    private static readonly string[] TestSeams =
    [
        "CompleteGestureForTests",
        "HasPendingGestureStateForTests",
        "HasTransientInteractionVisualsForTests",
        "IsGestureActiveForTests",
        "SeedMoveStateForTests",
        "SeedResizeState",
        "SeedTransientInteractionVisualsForTests",
        "SimulateCaptureLossForTests",
        "SimulateStalePointerUpForTests",
    ];

    [Fact]
    public void TestRendererBinary_ContainsConditionallyCompiledGestureTestSeams()
    {
        var methods = typeof(AvaloniaCanvasGestureHandler)
            .GetMembers(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(member => member.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var seam in TestSeams)
            methods.Should().Contain(seam);
    }

    [Fact]
    public void TestProject_OwnsConditionallyCompiledGestureTestSeams()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var rendererDirectory = Path.Combine(root, "freep", "FreeP.App.Rendering.Avalonia");
        var supportFile = Path.Combine(
            root,
            "freep",
            "TestSupport",
            "Rendering.Avalonia",
            "AvaloniaCanvasGestureHandler.TestAccess.cs");

        File.ReadAllText(Path.Combine(rendererDirectory, "AvaloniaCanvasGestureHandler.cs"))
            .Should().NotContain("SeedMoveStateForTests");
        File.Exists(supportFile).Should().BeTrue();
        File.ReadAllText(Path.Combine(rendererDirectory, "FreeP.App.Rendering.Avalonia.csproj"))
            .Should().Contain("'$(FreePRendererTestSupport)' == 'true'");
        File.ReadAllText(Path.Combine(
                root,
                "freep",
                "FreeP.App.Rendering.Avalonia.Tests",
                "FreeP.App.Rendering.Avalonia.Tests.csproj"))
            .Should().Contain("AdditionalProperties=\"FreePRendererTestSupport=true\"");
    }
}
