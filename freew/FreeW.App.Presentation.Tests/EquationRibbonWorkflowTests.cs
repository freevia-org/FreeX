using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class EquationRibbonWorkflowTests
{
    [Fact]
    public void SharedWorkflowRegistersDefaultPresetsAndCompatibilityAliases()
    {
        var inserted = new List<Equation>();
        var bindings = new FreeWRibbonCommandBindingPorts();

        EquationRibbonWorkflow.Register(bindings, new EquationRibbonPorts(inserted.Add));

        var primary = Command(bindings, FreeWRibbonCommandAction.Equation);
        Registered(bindings, EquationPresetCatalog.DefaultCommandId).Should().BeSameAs(primary);
        Registered(bindings, EquationPresetCatalog.LegacyDefaultCommandId).Should().BeSameAs(primary);

        primary.Execute(RibbonCommandContext.Empty);
        foreach (var preset in EquationPresetCatalog.Presets)
        {
            var canonical = Registered(bindings, preset.CommandId);
            Registered(bindings, preset.LegacyCommandId).Should().BeSameAs(canonical);
            canonical.Execute(RibbonCommandContext.Empty);
        }

        inserted.Should().HaveCount(EquationPresetCatalog.Presets.Count + 1);
        inserted[0].LinearText.Should().Be(EquationPresetCatalog.CreateDefaultEquation().LinearText);
        inserted.Skip(1).Select(equation => equation.LinearText).Should().Equal(
            EquationPresetCatalog.Presets.Select(preset => preset.CreateEquation().LinearText));
    }

    [Fact]
    public void EveryExecutionCreatesAFreshEquationModel()
    {
        var inserted = new List<Equation>();
        var bindings = new FreeWRibbonCommandBindingPorts();
        EquationRibbonWorkflow.Register(bindings, new EquationRibbonPorts(inserted.Add));
        var fraction = Registered(bindings, EquationPresetCatalog.Get(EquationPresetKind.Fraction).CommandId);

        fraction.Execute(RibbonCommandContext.Empty);
        fraction.Execute(RibbonCommandContext.Empty);

        inserted.Should().HaveCount(2);
        inserted[0].Should().NotBeSameAs(inserted[1]);
        inserted[0].Runs.Should().NotBeSameAs(inserted[1].Runs);
    }

    [Fact]
    public void BothRenderersDelegateEquationDispatchToSharedPresentation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("EquationRibbonWorkflow.Register(");
            source.Should().NotContain("foreach (var preset in EquationPresetCatalog.Presets)");
            source.Should().NotContain("EquationPresetCatalog.DefaultCommandId");
            source.Should().NotContain("EquationPresetCatalog.LegacyDefaultCommandId");
        }
    }

    private static IRibbonCommand Command(
        FreeWRibbonCommandBindingPorts bindings,
        FreeWRibbonCommandAction action)
    {
        var route = FreeWRibbonCommandWorkflow.Routes.Single(candidate => candidate.Action == action);
        return Registered(bindings, route.CommandId);
    }

    private static IRibbonCommand Registered(
        FreeWRibbonCommandBindingPorts bindings,
        RibbonCommandId commandId)
    {
        bindings.TryGet(commandId, out var command).Should().BeTrue(commandId.Value);
        return command!;
    }
}
