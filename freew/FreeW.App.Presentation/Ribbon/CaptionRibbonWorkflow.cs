using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record CaptionRibbonPorts(
    IRibbonCommand InsertCaption,
    Action<CaptionLabel>? InsertCaptionWithLabel,
    IRibbonCommand CrossReference);

/// <summary>
/// Owns References &gt; Captions command routing for both renderers. Native dialogs remain renderer
/// ports, while command identity, fixed-label routing, compatibility aliases, and fail-closed
/// availability stay shared.
/// </summary>
public static class CaptionRibbonWorkflow
{
    public static void Register(
        FreeWRibbonEditorCommandFamilyBuilder bindings,
        CaptionRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        RegisterCore(bindings.Bind, bindings.Register, ports);
    }

    public static void Register(IRibbonCommandRegistry bindings, CaptionRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        RegisterCore(
            (action, command) => bindings.Bind(action, command),
            bindings.Register,
            ports);
    }

    private static void RegisterCore(
        Func<FreeWRibbonCommandAction, IRibbonCommand, IRibbonCommand> bind,
        Action<RibbonCommandId, IRibbonCommand> register,
        CaptionRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.InsertCaption);
        ArgumentNullException.ThrowIfNull(ports.CrossReference);

        bind(FreeWRibbonCommandAction.Caption, ports.InsertCaption);
        register("freew.insert-caption", ports.InsertCaption);
        bind(FreeWRibbonCommandAction.InsertCaption_Figure, LabelCommand(CaptionLabel.Figure));
        bind(FreeWRibbonCommandAction.InsertCaption_Table, LabelCommand(CaptionLabel.Table));
        bind(FreeWRibbonCommandAction.InsertCaption_Equation, LabelCommand(CaptionLabel.Equation));
        bind(FreeWRibbonCommandAction.CrossReference, ports.CrossReference);

        IRibbonCommand LabelCommand(CaptionLabel label) =>
            ports.InsertCaptionWithLabel is { } insert
                ? new ActionRibbonCommand(() => insert(label))
                : FreeWRibbonExecutionProfile.UnavailableCommand;
    }
}
