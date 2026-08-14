using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

public sealed record IndexRibbonPorts(
    Action? MarkEntry,
    Action? InsertIndex,
    Action? RefreshIndex);

/// <summary>
/// Owns References &gt; Index routing and availability for both renderers. Dialog and focus behavior
/// remain native action ports; absent ports are disabled rather than silently applying defaults.
/// </summary>
public static class IndexRibbonWorkflow
{
    public static void Register(
        FreeWRibbonEditorCommandFamilyBuilder bindings,
        IndexRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        RegisterCore(bindings.Bind, ports);
    }

    public static void Register(IRibbonCommandRegistry bindings, IndexRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        RegisterCore((action, command) => bindings.Bind(action, command), ports);
    }

    private static void RegisterCore(
        Func<FreeWRibbonCommandAction, IRibbonCommand, IRibbonCommand> bind,
        IndexRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        bind(FreeWRibbonCommandAction.IndexMark, Command(ports.MarkEntry));
        bind(FreeWRibbonCommandAction.IndexInsert, Command(ports.InsertIndex));
        bind(FreeWRibbonCommandAction.IndexRefresh, Command(ports.RefreshIndex));
    }

    private static IRibbonCommand Command(Action? execute) =>
        execute is null
            ? FreeWRibbonExecutionProfile.UnavailableCommand
            : new ActionRibbonCommand(execute);
}
