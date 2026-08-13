namespace FreeP.App.Compositor;

public sealed record PresentationAltTextPaneHostSnapshot(
    string? Title,
    string? Description,
    bool IsDecorative);

public sealed record PresentationAltTextPaneFieldRenderPlan(
    string Label,
    string Value,
    string Placeholder,
    string? ValidationMessage,
    bool IsEnabled);

public sealed record PresentationAltTextPaneActionRenderPlan(
    string Label,
    bool IsEnabled,
    string? DisabledReason);

public sealed record PresentationAltTextPaneHostRenderPlan(
    string Heading,
    string Message,
    PresentationAltTextPaneFieldRenderPlan Title,
    PresentationAltTextPaneFieldRenderPlan Description,
    bool IsDecorative,
    PresentationAltTextPaneActionRenderPlan DecorativeAction,
    PresentationAltTextPaneActionRenderPlan ApplyAction,
    PresentationAltTextPaneActionRenderPlan CloseAction);

public interface IPresentationAltTextPaneHostView
{
    bool IsPaneVisible { get; }

    PresentationAltTextPaneHostSnapshot CaptureInput();

    void SetPaneVisible(bool visible);

    void SetInput(PresentationAltTextPaneHostSnapshot input);

    void Render(PresentationAltTextPaneHostRenderPlan plan);

    void RefreshAccessibilityMetadata();
}

/// <summary>
/// Owns Alt Text pane state transitions shared by the WPF and Avalonia hosts. Renderers retain
/// only native control snapshots, event wiring, focus, and projection of renderer-ready values.
/// </summary>
public sealed class PresentationAltTextPaneHostCoordinator
{
    private readonly PresentationReviewWorkflowSession _session;
    private readonly PresentationWorkareaPaneSession _panes;
    private readonly IPresentationAltTextPaneHostView _view;
    private int _viewUpdateDepth;

    public PresentationAltTextPaneHostCoordinator(
        PresentationReviewWorkflowSession session,
        PresentationWorkareaPaneSession panes,
        IPresentationAltTextPaneHostView view)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _panes = panes ?? throw new ArgumentNullException(nameof(panes));
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public bool IsUpdating => _viewUpdateDepth > 0;

    public bool IsPaneVisible => _panes.IsVisible(PresentationWorkareaPane.AltText);

    public PresentationAltTextPanePlan Show()
    {
        _panes.Show(PresentationWorkareaPane.AltText);
        _session.RefreshAltTextPlans(null, null, null);
        var plan = RequireLastPanePlan();
        UpdateView(() =>
        {
            _view.Render(BuildRenderPlan(plan));
            _view.SetPaneVisible(true);
        });
        _view.RefreshAccessibilityMetadata();
        return plan;
    }

    public void Hide()
    {
        _panes.Hide(PresentationWorkareaPane.AltText);
        UpdateView(() => _view.SetPaneVisible(false));
        _view.RefreshAccessibilityMetadata();
    }

    public PresentationAltTextPaneHostRenderPlan? Refresh()
    {
        if (IsUpdating || !IsPaneVisible)
            return null;

        var input = _view.CaptureInput();
        _session.RefreshAltTextPlans(input.Description, input.Title, input.IsDecorative);
        var renderPlan = BuildRenderPlan(RequireLastPanePlan());
        UpdateView(() => _view.Render(renderPlan));
        return renderPlan;
    }

    public PresentationAltTextPaneHostRenderPlan? RefreshSelection()
    {
        _session.RefreshAltTextPlans(null, null, null);
        return RenderIfVisible(RequireLastPanePlan());
    }

    public void SetInput(PresentationAltTextPaneHostSnapshot input)
    {
        ArgumentNullException.ThrowIfNull(input);
        EnsureVisible();
        UpdateView(() => _view.SetInput(input));
        Refresh();
    }

    public PresentationAltTextMutationPlan Apply()
    {
        var input = _view.CaptureInput();
        var mutation = _session.ApplySelectedShapeAlternativeText(
            input.Description,
            input.Title,
            input.IsDecorative);

        if (_session.LastAltTextPanePlan is { } panePlan)
            UpdateView(() => _view.Render(BuildRenderPlan(panePlan)));

        return mutation;
    }

    public PresentationAltTextPaneHostRenderPlan? RenderIfVisible(PresentationAltTextPanePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!IsPaneVisible)
            return null;

        var renderPlan = BuildRenderPlan(plan);
        UpdateView(() => _view.Render(renderPlan));
        return renderPlan;
    }

    public static PresentationAltTextPaneHostRenderPlan BuildRenderPlan(
        PresentationAltTextPanePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var apply = GetAction(plan, PresentationReviewWorkflowPlanner.AltTextPaneApplyCommandId);
        var decorative = GetAction(plan, PresentationReviewWorkflowPlanner.AltTextPaneDecorativeCommandId);
        var close = GetAction(plan, PresentationReviewWorkflowPlanner.AltTextPaneCloseCommandId);

        return new(
            plan.Heading,
            plan.Message,
            BuildFieldPlan(plan.Title),
            BuildFieldPlan(plan.Description),
            plan.IsDecorative,
            BuildActionPlan(decorative),
            BuildActionPlan(apply),
            BuildActionPlan(close));
    }

    private static PresentationAltTextPaneFieldRenderPlan BuildFieldPlan(
        PresentationAltTextPaneFieldPlan field) =>
        new(field.Label, field.Value, field.Placeholder, field.ValidationMessage, field.IsEnabled);

    private static PresentationAltTextPaneActionRenderPlan BuildActionPlan(
        PresentationReviewWorkflowActionPlan action) =>
        new(action.Label, action.IsEnabled, action.DisabledReason);

    private static PresentationReviewWorkflowActionPlan GetAction(
        PresentationAltTextPanePlan plan,
        string commandId) =>
        plan.Actions.Single(action => action.CommandId == commandId);

    private PresentationAltTextPanePlan RequireLastPanePlan() =>
        _session.LastAltTextPanePlan ??
        throw new InvalidOperationException("The Alt Text pane plan was not produced.");

    private void EnsureVisible()
    {
        if (!IsPaneVisible || !_view.IsPaneVisible)
            Show();
    }

    private void UpdateView(Action update)
    {
        _viewUpdateDepth++;
        try
        {
            update();
        }
        finally
        {
            _viewUpdateDepth--;
        }
    }
}
