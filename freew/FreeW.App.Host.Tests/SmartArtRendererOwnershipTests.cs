using System.Reflection;

namespace FreeW.App.Host.Tests;

public sealed class SmartArtRendererOwnershipTests
{
    [Fact]
    public void Renderer_keeps_planned_layout_owner_without_superseded_layout_methods()
    {
        var renderer = typeof(MainWindow).Assembly.GetType(
            "FreeW.App.Host.Editing.SmartArtRenderer",
            throwOnError: true)!;
        var methodNames = renderer
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        methodNames.Should().Contain("BuildPlannedLayout");
        methodNames.Should().NotContain([
            "BuildHorizontalList",
            "BuildProcess",
            "MakeArrow",
            "BuildStepProcess",
            "BuildCycle",
            "BuildRadial",
            "BuildMatrix",
        ]);
    }
}
