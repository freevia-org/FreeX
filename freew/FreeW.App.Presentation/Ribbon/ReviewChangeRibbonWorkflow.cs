using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

public sealed record ReviewChangeRibbonPorts(
    Action? PreviousChange,
    Action? NextChange,
    Action? AcceptSelectedChange,
    Action? RejectSelectedChange);

/// <summary>
/// Owns Review &gt; Changes navigation and selected-change routing for both renderers. The host
/// supplies Reviewing Pane selection operations; missing native selection endpoints fail closed.
/// </summary>
public static class ReviewChangeRibbonWorkflow
{
    public static void Register(
        FreeWRibbonCommandBindingPorts bindings,
        ReviewChangeRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);

        BindOptional(FreeWRibbonCommandAction.PreviousChange, ports.PreviousChange);
        BindOptional(FreeWRibbonCommandAction.NextChange, ports.NextChange);
        var accept = BindOptional(FreeWRibbonCommandAction.AcceptThis, ports.AcceptSelectedChange);
        var reject = BindOptional(FreeWRibbonCommandAction.RejectThis, ports.RejectSelectedChange);
        bindings.Register("freew.accept-change", accept);
        bindings.Register("freew.reject-change", reject);

        IRibbonCommand BindOptional(FreeWRibbonCommandAction action, Action? execute)
        {
            var command = execute is null
                ? FreeWRibbonExecutionProfile.UnavailableCommand
                : new ActionRibbonCommand(execute);
            return bindings.Bind(action, command);
        }
    }
}
