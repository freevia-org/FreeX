using Free.Shared.Ribbon;

namespace FreeW.App.Presentation.Ribbon;

public sealed record TableOfAuthoritiesRibbonPorts(
    Action? MarkCitation,
    Action? InsertTableOfAuthorities,
    Action? RefreshTableOfAuthorities,
    Action? PrepareRefresh = null);

/// <summary>
/// Owns References &gt; Table of Authorities routing for both renderers. Native dialog actions remain
/// host ports; absent ports are unavailable rather than silently inserting default content.
/// </summary>
public static class TableOfAuthoritiesRibbonWorkflow
{
    public static void Register(
        FreeWRibbonEditorCommandFamilyBuilder bindings,
        TableOfAuthoritiesRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        RegisterCore(bindings.Bind, ports);
    }

    public static void Register(IRibbonCommandRegistry bindings, TableOfAuthoritiesRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        RegisterCore((action, command) => bindings.Bind(action, command), ports);
    }

    private static void RegisterCore(
        Func<FreeWRibbonCommandAction, IRibbonCommand, IRibbonCommand> bind,
        TableOfAuthoritiesRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        bind(FreeWRibbonCommandAction.MarkCitation, Command(ports.MarkCitation));
        bind(FreeWRibbonCommandAction.TableOfAuthorities, Command(ports.InsertTableOfAuthorities));
        bind(
            FreeWRibbonCommandAction.TableOfAuthoritiesRefresh,
            Command(ports.RefreshTableOfAuthorities, ports.PrepareRefresh));
    }

    private static IRibbonCommand Command(Action? execute, Action? prepare = null) =>
        execute is null
            ? FreeWRibbonExecutionProfile.UnavailableCommand
            : new ActionRibbonCommand(() =>
            {
                prepare?.Invoke();
                execute();
            });
}
