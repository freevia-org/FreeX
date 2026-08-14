using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record TableOfFiguresRibbonPorts(
    Action<CaptionLabel> Insert,
    Action<CaptionLabel> Refresh,
    Action? PrepareExecution = null);

/// <summary>
/// Owns References &gt; Table of Figures label routing for both renderers. The primary routes use the
/// Figure label, matching Word, and share command identity with the explicit Figure menu entries.
/// </summary>
public static class TableOfFiguresRibbonWorkflow
{
    public static void Register(
        FreeWRibbonEditorCommandFamilyBuilder bindings,
        TableOfFiguresRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        RegisterCore(bindings.Bind, ports);
    }

    public static void Register(IRibbonCommandRegistry bindings, TableOfFiguresRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        RegisterCore((action, command) => bindings.Bind(action, command), ports);
    }

    private static void RegisterCore(
        Func<FreeWRibbonCommandAction, IRibbonCommand, IRibbonCommand> bind,
        TableOfFiguresRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.Insert);
        ArgumentNullException.ThrowIfNull(ports.Refresh);

        var insertFigure = Command(() => ports.Insert(CaptionLabel.Figure));
        bind(FreeWRibbonCommandAction.Tof, insertFigure);
        bind(FreeWRibbonCommandAction.Tof_Figure, insertFigure);
        bind(FreeWRibbonCommandAction.Tof_Table, Command(() => ports.Insert(CaptionLabel.Table)));
        bind(FreeWRibbonCommandAction.Tof_Equation, Command(() => ports.Insert(CaptionLabel.Equation)));

        var refreshFigure = Command(() => ports.Refresh(CaptionLabel.Figure));
        bind(FreeWRibbonCommandAction.TofRefresh, refreshFigure);
        bind(FreeWRibbonCommandAction.TofRefresh_Figure, refreshFigure);
        bind(FreeWRibbonCommandAction.TofRefresh_Table, Command(() => ports.Refresh(CaptionLabel.Table)));
        bind(FreeWRibbonCommandAction.TofRefresh_Equation, Command(() => ports.Refresh(CaptionLabel.Equation)));

        IRibbonCommand Command(Action execute) => new ActionRibbonCommand(() =>
        {
            ports.PrepareExecution?.Invoke();
            execute();
        });
    }
}
