namespace FreeP.App.Compositor;

public sealed record PresentationReadingOrderPaneActionRenderPlan(
    string CommandId,
    string Label,
    bool IsEnabled,
    string? DisabledReason);

public sealed record PresentationReadingOrderPaneHostRenderPlan(
    string Heading,
    string Message,
    IReadOnlyList<PresentationReadingOrderItemPlan> Items,
    bool ShouldShowEmptyState,
    string EmptyStateMessage,
    PresentationReadingOrderPaneActionRenderPlan MoveEarlierAction,
    PresentationReadingOrderPaneActionRenderPlan MoveLaterAction);

public interface IPresentationReadingOrderPaneHostView
{
    void SetPaneVisible(bool visible);

    void Render(PresentationReadingOrderPaneHostRenderPlan plan);

    void RefreshAccessibilityMetadata();
}

/// <summary>
/// Owns Reading Order pane visibility and renderer-ready projection shared by the WPF and
/// Avalonia hosts. Renderers retain native controls, item cards, event wiring, and focus.
/// </summary>
public sealed class PresentationReadingOrderPaneHostCoordinator
{
    private readonly PresentationWorkareaPaneSession _panes;
    private readonly IPresentationReadingOrderPaneHostView _view;

    public PresentationReadingOrderPaneHostCoordinator(
        PresentationWorkareaPaneSession panes,
        IPresentationReadingOrderPaneHostView view)
    {
        _panes = panes ?? throw new ArgumentNullException(nameof(panes));
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public bool IsPaneVisible => _panes.IsVisible(PresentationWorkareaPane.ReadingOrder);

    public PresentationReadingOrderPaneHostRenderPlan? RenderIfVisible(
        PresentationReadingOrderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!IsPaneVisible)
            return null;

        var renderPlan = BuildRenderPlan(plan);
        _view.Render(renderPlan);
        return renderPlan;
    }

    public PresentationReadingOrderPaneHostRenderPlan Present(PresentationReadingOrderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _panes.Show(PresentationWorkareaPane.ReadingOrder);
        var renderPlan = BuildRenderPlan(plan);
        _view.Render(renderPlan);
        _view.SetPaneVisible(true);
        _view.RefreshAccessibilityMetadata();
        return renderPlan;
    }

    public static PresentationReadingOrderPaneHostRenderPlan BuildRenderPlan(
        PresentationReadingOrderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new(
            plan.Heading,
            plan.DisplayMessage,
            plan.Items,
            plan.Items.Count == 0,
            PresentationReviewWorkflowPlanner.EmptyReadingOrderMessage,
            BuildActionPlan(
                plan,
                PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId),
            BuildActionPlan(
                plan,
                PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId));
    }

    private static PresentationReadingOrderPaneActionRenderPlan BuildActionPlan(
        PresentationReadingOrderPlan plan,
        string commandId)
    {
        var action = plan.Actions.Single(candidate => candidate.CommandId == commandId);
        return new(action.CommandId, action.Label, action.IsEnabled, action.DisabledReason);
    }
}
