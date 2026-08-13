namespace FreeX.App.Presentation.PivotUI;

public enum PivotHeaderActionRouteKind
{
    None,
    CommandFactory,
    Dialog,
    Deferred
}

public enum PivotHeaderDialogKind
{
    None,
    LabelFilter,
    ValueFilter,
    MoreSortOptions,
    FieldSettings,
    ValueFieldSettings
}

public sealed record PivotHeaderActionPlan(
    PivotHeaderActionRouteKind RouteKind,
    PivotHeaderDialogKind DialogKind = PivotHeaderDialogKind.None,
    string? DeferredReason = null)
{
    public static PivotHeaderActionPlan None { get; } = new(PivotHeaderActionRouteKind.None);

    public static PivotHeaderActionPlan CommandFactory { get; } =
        new(PivotHeaderActionRouteKind.CommandFactory);

    public static PivotHeaderActionPlan Dialog(PivotHeaderDialogKind dialogKind) =>
        new(PivotHeaderActionRouteKind.Dialog, dialogKind);

    public static PivotHeaderActionPlan Deferred(string reason) =>
        new(PivotHeaderActionRouteKind.Deferred, DeferredReason: reason);
}

/// <summary>
/// Shared routing for PivotTable header-dropdown actions. Menu construction stays in
/// <see cref="PivotHeaderDropdownMenuBuilder"/>. This planner owns the cross-host route decision while
/// <see cref="PivotHeaderCommandPlanner"/> owns direct command composition; renderers keep dialog realization.
/// </summary>
public static class PivotHeaderActionPlanner
{
    public static PivotHeaderActionPlan Plan(PivotHeaderMenuAction action) =>
        action switch
        {
            PivotHeaderMenuAction.Separator => PivotHeaderActionPlan.None,
            PivotHeaderMenuAction.LabelFilter => PivotHeaderActionPlan.Dialog(PivotHeaderDialogKind.LabelFilter),
            PivotHeaderMenuAction.ValueFilter => PivotHeaderActionPlan.Dialog(PivotHeaderDialogKind.ValueFilter),
            PivotHeaderMenuAction.MoreSortOptions => PivotHeaderActionPlan.Dialog(PivotHeaderDialogKind.MoreSortOptions),
            // The field-menu entry uses the same value-field settings surface as WPF. The shared
            // The shell handler resolves the data-field index from the target caption, with the
            // one-value-field fallback used by the desktop host.
            PivotHeaderMenuAction.FieldSettings => PivotHeaderActionPlan.Dialog(PivotHeaderDialogKind.FieldSettings),
            PivotHeaderMenuAction.ValueFieldSettings => PivotHeaderActionPlan.Dialog(PivotHeaderDialogKind.ValueFieldSettings),
            _ => PivotHeaderActionPlan.CommandFactory,
        };
}
