using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AvaloniaRibbonRegistryArchitectureTests
{
    [Fact]
    public void Ribbon_registry_does_not_mask_missing_host_routes_with_enabled_no_ops()
    {
        var composition = System.IO.File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Avalonia", "Ribbon", "AvaloniaRibbonHost.cs"));
        var renderer = System.IO.File.ReadAllText(RepositoryFileLocator.Find(
            "shared", "Free.Shared.Ribbon.Avalonia", "AvaloniaRibbonRenderer.cs"));

        composition.Should().NotContain("registry.Register(id, EmptyRibbonCommand.Instance)");
        composition.Should().NotContain("DisabledNoOpRibbonCommand");
        composition.Should().Contain("Null callbacks leave the command unregistered");
        composition.Should().Contain("InsertChartRibbonCommand : IRibbonStatefulCommand");
        composition.Should().Contain("RibbonCommandState GetState() => new(IsEnabled: _session() is not null)");

        renderer.Should().Contain("if (!registry.TryGet(commandId, out var cmd))");
        renderer.Should().Contain("element.IsEnabled = false;");
    }
}
