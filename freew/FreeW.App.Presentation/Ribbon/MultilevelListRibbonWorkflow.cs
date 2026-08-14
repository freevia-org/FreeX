using Free.Shared.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

/// <summary>Renderer adapters consumed by the shared Multilevel List command family.</summary>
public sealed record MultilevelListRibbonPorts(
    Action<MultilevelListDefinition> ApplyDefinition,
    Action<int> ChangeLevel,
    Action? OpenDefineDialog);

/// <summary>
/// Owns the complete Home &gt; Paragraph &gt; Multilevel List command policy for both renderers.
/// Native hosts provide only model adapters and the toolkit-specific definition dialog.
/// </summary>
public static class MultilevelListRibbonWorkflow
{
    public static void Register(
        FreeWRibbonCommandBindingPorts bindings,
        MultilevelListRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.ApplyDefinition);
        ArgumentNullException.ThrowIfNull(ports.ChangeLevel);

        bindings.Bind(
            FreeWRibbonCommandAction.MultilevelList,
            new ActionRibbonCommand(() =>
                ports.ApplyDefinition(MultilevelListDialogPlanner.DefaultDefinition)));
        bindings.Bind(
            FreeWRibbonCommandAction.MultilevelDemote,
            new ActionRibbonCommand(() => ports.ChangeLevel(+1)));
        bindings.Bind(
            FreeWRibbonCommandAction.MultilevelPromote,
            new ActionRibbonCommand(() => ports.ChangeLevel(-1)));

        foreach (var preset in MultilevelListDialogPlanner.Presets)
        {
            var captured = preset;
            bindings.Register(
                captured.CommandId,
                new ActionRibbonCommand(() => ports.ApplyDefinition(captured.Definition)));
        }

        bindings.Bind(
            FreeWRibbonCommandAction.MultilevelDefine,
            ports.OpenDefineDialog is null
                ? FreeWRibbonExecutionProfile.UnavailableCommand
                : new ActionRibbonCommand(ports.OpenDefineDialog));
    }
}
