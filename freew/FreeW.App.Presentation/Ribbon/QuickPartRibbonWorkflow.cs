using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record QuickPartRibbonPorts(
    IRibbonCommand InsertSavedPart,
    IRibbonCommand SaveSelection,
    IRibbonCommand OpenOrganizer,
    Action<RunFieldKind> InsertField);

/// <summary>Owns Quick Parts command identity and alias routing for both renderers.</summary>
public static class QuickPartRibbonWorkflow
{
    public static void Register(
        IRibbonCommandRegistry bindings,
        QuickPartRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.InsertSavedPart);
        ArgumentNullException.ThrowIfNull(ports.SaveSelection);
        ArgumentNullException.ThrowIfNull(ports.OpenOrganizer);
        ArgumentNullException.ThrowIfNull(ports.InsertField);

        bindings.Register("freew.insert-quickpart", ports.InsertSavedPart);
        bindings.Register("freew.quick-parts", ports.InsertSavedPart);
        bindings.Register("freew.quick-parts.snippet", ports.InsertSavedPart);
        bindings.Bind(FreeWRibbonCommandAction.SaveQuickpart, ports.SaveSelection);
        bindings.Bind(FreeWRibbonCommandAction.BuildingBlocksOrganizer, ports.OpenOrganizer);

        DocumentPropertyFieldPlanner.RegisterCommands(bindings, ports.InsertField);
        bindings.Register(
            "freew.quick-parts.date",
            new ActionRibbonCommand(() => ports.InsertField(RunFieldKind.Date)));
    }
}
