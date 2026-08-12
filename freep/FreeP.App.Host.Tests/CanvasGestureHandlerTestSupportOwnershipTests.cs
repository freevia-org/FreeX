using System.IO;
using System.Reflection;
using FreeP.App.Rendering.Wpf;

namespace FreeP.App.Host.Tests;

public sealed class CanvasGestureHandlerTestSupportOwnershipTests
{
    private static readonly string[] TestSeams =
    [
        "AdornerForTests",
        "CompleteGestureForTests",
        "HandleEscapeForTests",
        "HandleKeyDownForTests",
        "HandleOleDoubleClickForTests",
        "HasPendingGestureStateForTests",
        "HasTransientInteractionVisualsForTests",
        "IsGestureActiveForTests",
        "SeedMoveStateForTests",
        "SeedResizeStateForTests",
        "SeedTransientInteractionVisualsForTests",
        "SimulateStaleMouseUpForTests",
    ];

    [Fact]
    public void TestRendererBinary_ContainsConditionallyCompiledGestureTestSeams()
    {
        var methods = typeof(CanvasGestureHandler)
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
        var rendererDirectory = Path.Combine(root, "freep", "FreeP.App.Rendering.Wpf");
        var supportFile = Path.Combine(
            root,
            "freep",
            "TestSupport",
            "Rendering.Wpf",
            "CanvasGestureHandler.TestAccess.cs");

        File.ReadAllText(Path.Combine(rendererDirectory, "CanvasGestureHandler.cs"))
            .Should().NotContain("SeedMoveStateForTests");
        File.ReadAllText(Path.Combine(rendererDirectory, "SlideCanvas.cs"))
            .Should().NotContain("GestureHandlerForTests")
            .And.NotContain("HasLiveTransformPreviewForTests");
        File.ReadAllText(Path.Combine(rendererDirectory, "SelectionAdorner.cs"))
            .Should().NotContain("HasTransientInteractionVisualsForTests");
        File.Exists(supportFile).Should().BeTrue();
        File.ReadAllText(Path.Combine(rendererDirectory, "FreeP.App.Rendering.Wpf.csproj"))
            .Should().Contain("<ItemGroup Condition=\"'$(FreePRendererTestSupport)' == 'true'\">\r\n    <AssemblyAttribute Include=\"System.Runtime.CompilerServices.InternalsVisibleTo\">");
        File.ReadAllText(Path.Combine(
                root,
                "freep",
                "FreeP.App.Host.Tests",
                "FreeP.App.Host.Tests.csproj"))
            .Should().Contain("AdditionalProperties=\"FreePRendererTestSupport=true\"");
    }
}
