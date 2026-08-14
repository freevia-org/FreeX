using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

public sealed record DropCapRibbonPorts(
    IRibbonCommand Dropped,
    IRibbonCommand InMargin,
    IRibbonCommand None,
    IRibbonCommand Options);

/// <summary>
/// Owns Insert &gt; Drop Cap command identity for both renderers. The primary split-button route and
/// both historical submenu id families resolve to the same native commands.
/// </summary>
public static class DropCapRibbonWorkflow
{
    public static IReadOnlyList<FreeWRibbonCommandAction> Actions { get; } =
    [
        FreeWRibbonCommandAction.DropCap,
        FreeWRibbonCommandAction.DropCap_Dropped,
        FreeWRibbonCommandAction.DropCap_InMargin,
        FreeWRibbonCommandAction.DropCap_None,
        FreeWRibbonCommandAction.DropCapDropped,
        FreeWRibbonCommandAction.DropCapInMargin,
        FreeWRibbonCommandAction.DropCapNone,
        FreeWRibbonCommandAction.DropCapOptions,
    ];

    public static void Register(
        IRibbonCommandRegistry bindings,
        DropCapRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);

        bindings.Bind(FreeWRibbonCommandAction.DropCap, ports.Dropped);
        bindings.Bind(FreeWRibbonCommandAction.DropCap_Dropped, ports.Dropped);
        bindings.Bind(FreeWRibbonCommandAction.DropCapDropped, ports.Dropped);
        bindings.Bind(FreeWRibbonCommandAction.DropCap_InMargin, ports.InMargin);
        bindings.Bind(FreeWRibbonCommandAction.DropCapInMargin, ports.InMargin);
        bindings.Bind(FreeWRibbonCommandAction.DropCap_None, ports.None);
        bindings.Bind(FreeWRibbonCommandAction.DropCapNone, ports.None);
        bindings.Bind(FreeWRibbonCommandAction.DropCapOptions, ports.Options);
    }
}
