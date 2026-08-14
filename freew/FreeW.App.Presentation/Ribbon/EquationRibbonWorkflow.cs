using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record EquationRibbonPorts(Action<Equation> InsertEquation);

/// <summary>
/// Owns Insert &gt; Equation default/preset dispatch and compatibility aliases for both renderers.
/// Every execution materializes a fresh equation so inserted model instances are never shared.
/// </summary>
public static class EquationRibbonWorkflow
{
    public static void Register(
        IRibbonCommandRegistry bindings,
        EquationRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.InsertEquation);

        var defaultCommand = new ActionRibbonCommand(() =>
            ports.InsertEquation(EquationPresetCatalog.CreateDefaultEquation()));
        bindings.Bind(FreeWRibbonCommandAction.Equation, defaultCommand);
        bindings.Register(EquationPresetCatalog.DefaultCommandId, defaultCommand);
        bindings.Register(EquationPresetCatalog.LegacyDefaultCommandId, defaultCommand);

        foreach (var preset in EquationPresetCatalog.Presets)
        {
            var captured = preset;
            var command = new ActionRibbonCommand(() =>
                ports.InsertEquation(captured.CreateEquation()));
            bindings.Register(captured.CommandId, command);
            bindings.Register(captured.LegacyCommandId, command);
        }
    }
}
