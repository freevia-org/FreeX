using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

public sealed record InsertMediaRibbonPorts(
    IRibbonCommand Chart,
    IRibbonCommand SmartArt,
    IRibbonCommand Icon,
    IRibbonCommand WordArt,
    IRibbonCommand EmbeddedObject);

/// <summary>
/// Owns Insert-tab media command mapping for both renderers. Dialogs, platform services, and editor
/// calls remain native command adapters supplied through the ports.
/// </summary>
public static class InsertMediaRibbonWorkflow
{
    public static IReadOnlyList<FreeWRibbonCommandAction> Actions { get; } =
    [
        FreeWRibbonCommandAction.Chart,
        FreeWRibbonCommandAction.Smartart,
        FreeWRibbonCommandAction.InsertIcon,
        FreeWRibbonCommandAction.Wordart,
        FreeWRibbonCommandAction.Object,
    ];

    public static void Register(IRibbonCommandRegistry bindings, InsertMediaRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);

        bindings.Bind(FreeWRibbonCommandAction.Chart, ports.Chart);
        bindings.Bind(FreeWRibbonCommandAction.Smartart, ports.SmartArt);
        bindings.Bind(FreeWRibbonCommandAction.InsertIcon, ports.Icon);
        bindings.Bind(FreeWRibbonCommandAction.Wordart, ports.WordArt);
        bindings.Bind(FreeWRibbonCommandAction.Object, ports.EmbeddedObject);
    }
}
